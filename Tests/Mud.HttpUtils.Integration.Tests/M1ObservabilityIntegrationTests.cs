// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils.Integration.Tests;

/// <summary>
/// M1 端到端验收：#5（URL 脱敏 Span tag / 诊断事件 / 开关）+ #6（cache_key 不进指标 tag、保留于 Activity 事件）。
/// </summary>
/// <remarks>
/// 依赖进程级 ActivitySource / Meter 与 ActivityListener / MeterListener 断言；
/// 集成测试程序集已声明 <c>DisableTestParallelization</c>，本类静态开关翻转（T-5.2）在 finally 中恢复。
/// </remarks>
public class M1ObservabilityIntegrationTests : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _startedActivities = new();

    public M1ObservabilityIntegrationTests()
    {
        // 放行 https://api.example.com（白名单命中即跳过私有 IP / DNS 预检，避免测试依赖网络）
        UrlValidator.AddAllowedDomain("api.example.com");

        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => _startedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    public void Dispose() => _activityListener.Dispose();

    private static DirectEnhancedHttpClient CreateClient(HttpMessageHandler innerHandler)
    {
        var httpClient = new HttpClient(innerHandler);
        return new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });
    }

    [Fact]
    public async Task Span_HttpUrl_Redacts_SensitiveQuery_T5_1()
    {
        // T-5.1：TokenInjectionMode.Query 等价场景——query 携带 access_token，Span http.url 不得泄露令牌原文
        var client = CreateClient(new OkHandler());

        using var response = await client.SendRawAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/v1/data?access_token=secret-token-value&page=1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var span = _startedActivities.LastOrDefault(a => a.OperationName == MudHttpActivitySource.ActivityNameRequest);
        span.Should().NotBeNull();
        var httpUrl = span!.GetTagItem(MudHttpActivitySource.Tags.HttpUrl) as string;
        httpUrl.Should().NotBeNull();
        httpUrl.Should().Contain("***REDACTED***");
        httpUrl.Should().NotContain("secret-token-value");
        httpUrl.Should().Contain("page=1");   // 非敏感参数保留（排障可用性）
    }

    [Fact]
    public async Task Span_HttpUrl_SwitchOff_PreservesFullUrl_T5_2()
    {
        // T-5.2：RedactUrlInTelemetry = false → Span http.url 保留完整令牌（开关有效性）
        MudHttpObservabilityOptions.RedactUrlInTelemetry = false;
        try
        {
            var client = CreateClient(new OkHandler());

            using var response = await client.SendRawAsync(
                new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/v1/data?access_token=secret-token-value"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var span = _startedActivities.LastOrDefault(a => a.OperationName == MudHttpActivitySource.ActivityNameRequest);
            span.Should().NotBeNull();
            var httpUrl = span!.GetTagItem(MudHttpActivitySource.Tags.HttpUrl) as string;
            httpUrl.Should().Be("https://api.example.com/v1/data?access_token=secret-token-value");
        }
        finally
        {
            MudHttpObservabilityOptions.RedactUrlInTelemetry = true;
        }
    }

    [Fact]
    public async Task DiagnosticEvent_Url_Redacts_SensitiveQuery_T5_4()
    {
        // T-5.4：诊断事件（RequestStarted）payload 的 url 同样脱敏
        var client = CreateClient(new OkHandler());

        using var response = await client.SendRawAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/v1/data?access_token=secret-token-value"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var span = _startedActivities.LastOrDefault(a => a.OperationName == MudHttpActivitySource.ActivityNameRequest);
        span.Should().NotBeNull();
        var started = span!.Events.FirstOrDefault(e => e.Name == MudHttpDiagnosticNames.RequestStarted);
        started.Name.Should().NotBeNull();
        var urlTag = started.Tags.FirstOrDefault(t => t.Key == "url");
        urlTag.Value.Should().NotBeNull();
        (urlTag.Value as string).Should().Contain("***REDACTED***");
        (urlTag.Value as string).Should().NotContain("secret-token-value");
    }

    [Fact]
    public void CacheMetric_ExcludesCacheKeyTag_T6_1()
    {
        // T-6.1：mud.http.cache 指标 tag 集合不含 cache_key（高基数不进时序后端），含 client_name/outcome
        var captured = new List<(long value, string[] tagKeys)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name != "mud.http.cache")
                return;
            captured.Add((value, tags.ToArray().Select(t => t.Key).ToArray()));
        });
        meterListener.Start();

        var cache = new MemoryHttpResponseCache();
        var interceptor = new CacheResponseInterceptor(cache, NullLogger<CacheResponseInterceptor>.Instance);
        cache.Set("cache-key-value", "cv", TimeSpan.FromSeconds(60));

        using (var activity = MudHttpActivitySource.Instance.StartActivity("cache-test", ActivityKind.Client))
        {
            interceptor.TryGet<string>("cache-key-value", out var value).Should().BeTrue();
            value.Should().Be("cv");
        }

        captured.Should().ContainSingle();
        captured[0].value.Should().Be(1);
        captured[0].tagKeys.Should().NotContain("cache_key");
        captured[0].tagKeys.Should().Contain("client_name");
        captured[0].tagKeys.Should().Contain("outcome");
    }

    [Fact]
    public void CacheActivityEvent_KeepsCacheKey_T6_2()
    {
        // T-6.2：Activity 事件 mud.http.cache.hit 仍保留 cache_key（信息未丢失，仅不入指标）
        var cache = new MemoryHttpResponseCache();
        var interceptor = new CacheResponseInterceptor(cache, NullLogger<CacheResponseInterceptor>.Instance);
        cache.Set("cache-key-value", "cv", TimeSpan.FromSeconds(60));

        using var activity = MudHttpActivitySource.Instance.StartActivity("cache-test", ActivityKind.Client);
        interceptor.TryGet<string>("cache-key-value", out var value).Should().BeTrue();

        var hitEvent = activity!.Events.FirstOrDefault(e => e.Name == MudHttpDiagnosticNames.CacheHit);
        hitEvent.Name.Should().NotBeNull();
        var cacheKeyTag = hitEvent.Tags.FirstOrDefault(t => t.Key == "cache_key");
        cacheKeyTag.Value.Should().Be("cache-key-value");
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
                RequestMessage = request,
                Version = request.Version,
            });
    }
}
