// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mud.HttpUtils.Observability;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Integration.Tests;

/// <summary>
/// 可观测性端到端集成测试：
/// 验证"真实 HTTP 请求 → Activity 含完整属性与事件 → 指标产出"的全链路。
/// </summary>
/// <remarks>
/// 覆盖 v2 方案验收标准：
/// - 正常请求 span 含 RequestStarted/RequestStopped 事件
/// - 缓存命中场景 span 含 mud.http.cache.hit tag + CacheHit 事件
/// - 令牌刷新场景 span 含 mud.token.manager_key tag + TokenRefreshed 事件
/// - 熔断器状态变化 span 含 mud.http.circuit_breaker.state tag + CircuitBreakerStateChanged 事件
/// - 指标值正确递增
/// </remarks>
public class ObservabilityIntegrationTests : IDisposable
{
    private readonly TestServer _server;
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _startedActivities = new();

    public ObservabilityIntegrationTests()
    {
        _server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services => services.AddRouting())
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/api/ok", async context =>
                    {
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("ok");
                    });
                });
            }));

        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                // 持有引用，便于断言时访问 Tags/Events（Activity.Dispose 后仍可读 Tags/Events）
                _startedActivities.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    [Fact]
    public async Task NormalRequest_Produces_Span_With_RequestStarted_And_RequestStopped_Events()
    {
        // 使用 TestServer 的 Handler 作为传输层，确保请求真实命中本地服务器
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => _server.CreateHandler())
            .AddHttpMessageHandler<TracingDelegatingHandler>();
        services.AddTransient<TracingDelegatingHandler>();
        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("test");
        client.BaseAddress = new Uri("http://localhost/api/");

        using var response = await client.GetAsync("ok");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 应有一个 MudHttpActivitySource 创建的请求 span
        var requestActivity = _startedActivities.LastOrDefault(a =>
            a.OperationName == MudHttpActivitySource.ActivityNameRequest);
        requestActivity.Should().NotBeNull();

        // span.Tags 应含 OTel 标准属性
        requestActivity!.GetTagItem(MudHttpActivitySource.Tags.HttpMethod).Should().Be("GET");
        requestActivity.GetTagItem(MudHttpActivitySource.Tags.HttpStatusCode).Should().Be(200);
        requestActivity.GetTagItem(MudHttpActivitySource.Tags.MudCorrelationId).Should().NotBeNull();

        // span.Events 应含 RequestStarted 与 RequestStopped
        var events = requestActivity.Events.ToList();
        events.Should().Contain(e => e.Name == MudHttpDiagnosticNames.RequestStarted);
        events.Should().Contain(e => e.Name == MudHttpDiagnosticNames.RequestStopped);
    }

    [Fact]
    public async Task NormalRequest_Increments_RequestCounter_Metric()
    {
        var counterValues = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.requests")
                counterValues.Add(value);
        });
        meterListener.Start();

        // 使用 TestServer 的 Handler 作为传输层，确保请求真实命中本地服务器
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("metric_test")
            .ConfigurePrimaryHttpMessageHandler(() => _server.CreateHandler())
            .AddHttpMessageHandler<TracingDelegatingHandler>();
        services.AddTransient<TracingDelegatingHandler>();
        var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("metric_test");
        client.BaseAddress = new Uri("http://localhost/api/");

        using var response = await client.GetAsync("ok");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        counterValues.Should().ContainSingle().Which.Should().Be(1);
    }

    [Fact]
    public void CacheInterceptor_Hit_Produces_CacheHit_Event_And_MudCacheHit_Tag()
    {
        var cache = new MemoryHttpResponseCache();
        var interceptor = new CacheResponseInterceptor(cache, NullLogger<CacheResponseInterceptor>.Instance);
        cache.Set("ck", "cv", TimeSpan.FromSeconds(60));

        using var activity = MudHttpActivitySource.Instance.StartActivity("cache-test", ActivityKind.Client);
        interceptor.TryGet<string>("ck", out var value);

        value.Should().Be("cv");
        activity!.GetTagItem(MudHttpActivitySource.Tags.MudCacheHit).Should().Be(true);
        var events = activity.Events.ToList();
        events.Should().Contain(e => e.Name == MudHttpDiagnosticNames.CacheHit);
    }

    [Fact]
    public void CircuitBreaker_SetState_Produces_StateChanged_Event_And_Tag()
    {
        CircuitBreakerStateObserver.Clear();

        using var activity = MudHttpActivitySource.Instance.StartActivity("cb-test", ActivityKind.Client);
        CircuitBreakerStateObserver.SetState("integration_cb", CircuitBreakerState.Open);

        activity!.GetTagItem(MudHttpActivitySource.Tags.MudCircuitBreakerState).Should().Be("Open");
        var events = activity.Events.ToList();
        events.Should().Contain(e => e.Name == MudHttpDiagnosticNames.CircuitBreakerStateChanged);

        CircuitBreakerStateObserver.Clear();
    }

    [Fact]
    public void TokenRecovery_Counter_Increments_With_Correct_Outcome_Tag()
    {
        var captured = new List<(long value, string outcome)>();
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
            if (instrument.Name != "mud.token.recovery")
                return;
            var tagList = tags.ToArray();
            var outcome = tagList.FirstOrDefault(t => t.Key == "outcome").Value as string ?? "(unknown)";
            captured.Add((value, outcome));
        });
        meterListener.Start();

        MudHttpMeter.TokenRecoveryCounter.Add(1,
            new KeyValuePair<string, object?>("token_manager_key", "test_tm"),
            new KeyValuePair<string, object?>("outcome", "success"));
        MudHttpMeter.TokenRecoveryCounter.Add(1,
            new KeyValuePair<string, object?>("token_manager_key", "test_tm"),
            new KeyValuePair<string, object?>("outcome", "failure"));

        captured.Should().HaveCount(2);
        captured[0].value.Should().Be(1);
        captured[0].outcome.Should().Be("success");
        captured[1].outcome.Should().Be("failure");
    }

    [Fact]
    public void TokenRecoveryDelegatingHandler_On401_Creates_RecoveryChildActivity()
    {
        // 重置活动捕获列表
        _startedActivities.Clear();

        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var callCount = 0;
        var innerHandler = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("recovered") };
        });

        var handler = new TokenRecoveryDelegatingHandler(
            mockTokenManager.Object,
            new TokenRecoveryOptions { Enabled = true, RecoveryMaxRetries = 1 });
        handler.InnerHandler = innerHandler;
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old");
        var response = invoker.SendAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 应有一个 mud.token.recovery 子 Activity
        var recoveryActivity = _startedActivities.FirstOrDefault(a =>
            a.OperationName == MudHttpActivitySource.ActivityNameTokenRecovery);
        recoveryActivity.Should().NotBeNull();
        recoveryActivity!.GetTagItem(MudHttpActivitySource.Tags.MudTokenManagerKey).Should().NotBeNull();
        recoveryActivity.GetTagItem("mud.token.recovery.success").Should().Be(true);
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _server.Dispose();
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
