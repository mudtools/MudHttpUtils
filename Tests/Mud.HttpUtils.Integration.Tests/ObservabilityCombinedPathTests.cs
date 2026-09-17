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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mud.HttpUtils.Observability;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Integration.Tests;

/// <summary>
/// G25/G32/OBS-2 组合路径（AddMudHttpClient + EnhancedHttpClient 外层观察窗口）可观测性行为矩阵测试。
/// </summary>
/// <remarks>
/// 对应《可观测性专项修复与完善方案》§3.1.3 行为矩阵与 §3.1.5 验收标准：
/// 组合路径上任一请求<strong>至多产生一个请求 Span、一组请求指标、一对请求事件</strong>（标记先行协议），
/// 并验证 G32 取消语义（outcome=cancelled、Span 不设 Error）与 OBS-2 4xx 语义校准（outcome=client_error、Span Ok）。
/// </remarks>
public class ObservabilityCombinedPathTests : IDisposable
{
    private readonly TestServer _server;
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _startedActivities = new();
    private int _retryEndpointHits;

    public ObservabilityCombinedPathTests()
    {
        UrlValidator.AddAllowedDomain("localhost");

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
                    endpoints.MapGet("/api/notfound", async context =>
                    {
                        context.Response.StatusCode = 404;
                        await context.Response.WriteAsync("missing");
                    });
                    endpoints.MapGet("/api/servererror", async context =>
                    {
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync("boom");
                    });
                    endpoints.MapGet("/api/retry", async context =>
                    {
                        var hits = Interlocked.Increment(ref _retryEndpointHits);
                        if (hits < 3)
                        {
                            context.Response.StatusCode = 500;
                            await context.Response.WriteAsync("flaky");
                            return;
                        }
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("\"ok\"");
                    });
                    endpoints.MapGet("/api/slow", async context =>
                    {
                        // 挂起直到客户端取消（TestServer 中止请求时 RequestAborted 触发）
                        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        context.RequestAborted.Register(() => tcs.TrySetResult());
                        await tcs.Task;
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

    public void Dispose()
    {
        _activityListener.Dispose();
        _server.Dispose();
    }

    private void ResetCaptures()
    {
        _startedActivities.Clear();
        Interlocked.Exchange(ref _retryEndpointHits, 0);
    }

    private IEnhancedHttpClient CreateCombinedClient(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("combined_path", c => c.BaseAddress = new Uri("http://localhost/api/"))
            .ConfigurePrimaryHttpMessageHandler(() => _server.CreateHandler());
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IEnhancedHttpClient>();
    }

    [Fact]
    public async Task CombinedPath_Success_Produces_Single_Span_And_Metric()
    {
        ResetCaptures();
        var requestMetrics = new List<(long value, KeyValuePair<string, object?>[] tags)>();
        using var meterListener = CreateRequestMetricListener(requestMetrics);

        var client = CreateCombinedClient();
        using var response = await client.SendRawAsync(new HttpRequestMessage(HttpMethod.Get, "ok"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var requestActivities = RequestActivities();
        requestActivities.Should().ContainSingle("组合路径上内层 TracingDelegatingHandler 应被标记先行协议短路");
        requestMetrics.Should().ContainSingle("请求指标应仅由外层观察窗口采集一次");
        requestMetrics[0].tags.Should().Contain(t => t.Key == "outcome" && Equals(t.Value, "success"));

        var activity = requestActivities[0];
        var events = activity.Events.ToList();
        events.Count(e => e.Name == MudHttpDiagnosticNames.RequestStarted).Should().Be(1);
        events.Count(e => e.Name == MudHttpDiagnosticNames.RequestStopped).Should().Be(1);
    }

    [Fact]
    public async Task CombinedPath_4xx_ApiException_Records_ClientError_With_Ok_Span()
    {
        // OBS-2：4xx 触发 EnsureSuccessStatusCodeAsync 抛 ApiException 属正常业务流，
        // 与 Handler 路径同语义：outcome=client_error + Span Ok；RequestFailed 事件仍发出（成对性）。
        ResetCaptures();
        var requestMetrics = new List<(long value, KeyValuePair<string, object?>[] tags)>();
        using var meterListener = CreateRequestMetricListener(requestMetrics);

        var client = CreateCombinedClient();
        var act = async () => await client.GetAsync<string>("notfound");

        await act.Should().ThrowAsync<ApiException>().Where(e => (int)e.StatusCode == 404);

        var requestActivities = RequestActivities();
        requestActivities.Should().ContainSingle();
        requestMetrics.Should().ContainSingle();
        requestMetrics[0].tags.Should().Contain(t => t.Key == "outcome" && Equals(t.Value, "client_error"));
        requestMetrics[0].tags.Should().Contain(t => t.Key == "status_code" && Equals(t.Value, 404));

        var activity = requestActivities[0];
        activity.Status.Should().Be(ActivityStatusCode.Ok, "4xx 属客户端正常业务流（G10/OBS-2）");
        var events = activity.Events.ToList();
        events.Should().Contain(e => e.Name == MudHttpDiagnosticNames.RequestFailed);
        events.Should().NotContain(e => e.Name == MudHttpDiagnosticNames.RequestStopped, "失败路径不发 RequestStopped");
    }

    [Fact]
    public async Task CombinedPath_5xx_ApiException_Records_Error_With_Error_Span()
    {
        ResetCaptures();
        var requestMetrics = new List<(long value, KeyValuePair<string, object?>[] tags)>();
        using var meterListener = CreateRequestMetricListener(requestMetrics);

        var client = CreateCombinedClient();
        var act = async () => await client.GetAsync<string>("servererror");

        await act.Should().ThrowAsync<ApiException>().Where(e => (int)e.StatusCode == 500);

        var requestActivities = RequestActivities();
        requestActivities.Should().ContainSingle();
        requestMetrics.Should().ContainSingle();
        requestMetrics[0].tags.Should().Contain(t => t.Key == "outcome" && Equals(t.Value, "error"));

        var activity = requestActivities[0];
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task CombinedPath_TransportException_Produces_Single_Span_And_Metric()
    {
        // 传输层异常（连接失败模拟）：主处理器直接抛 HttpRequestException
        ResetCaptures();
        var requestMetrics = new List<(long value, KeyValuePair<string, object?>[] tags)>();
        using var meterListener = CreateRequestMetricListener(requestMetrics);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("combined_path_throwing", c => c.BaseAddress = new Uri("http://localhost/api/"))
            .ConfigurePrimaryHttpMessageHandler(() => new ThrowingPrimaryHandler());
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEnhancedHttpClient>();

        var act = async () => await client.SendRawAsync(new HttpRequestMessage(HttpMethod.Get, "ok"));
        await act.Should().ThrowAsync<HttpRequestException>();

        var requestActivities = RequestActivities();
        requestActivities.Should().ContainSingle();
        requestMetrics.Should().ContainSingle();
        requestMetrics[0].tags.Should().Contain(t => t.Key == "outcome" && Equals(t.Value, "error"));

        var events = requestActivities[0].Events.ToList();
        events.Should().Contain(e => e.Name == MudHttpDiagnosticNames.RequestFailed);
        events.Should().NotContain(e => e.Name == MudHttpDiagnosticNames.RequestStopped);
    }

    [Fact]
    public async Task CombinedPath_Cancellation_Records_Cancelled_With_Unset_Span()
    {
        // G32：取消（OCE 且调用方令牌已触发）→ outcome=cancelled；Span 保持未设置（OTel 语义）；
        // RequestFailed 事件保持发出（成对性，G19）。
        ResetCaptures();
        var requestMetrics = new List<(long value, KeyValuePair<string, object?>[] tags)>();
        using var meterListener = CreateRequestMetricListener(requestMetrics);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("combined_path_cancel", c => c.BaseAddress = new Uri("http://localhost/api/"))
            .ConfigurePrimaryHttpMessageHandler(() => new CancellingPrimaryHandler());
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEnhancedHttpClient>();

        using var cts = new CancellationTokenSource(100);
        var act = async () => await client.SendRawAsync(
            new HttpRequestMessage(HttpMethod.Get, "ok"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var requestActivities = RequestActivities();
        requestActivities.Should().ContainSingle();
        requestMetrics.Should().ContainSingle();
        requestMetrics[0].tags.Should().Contain(t => t.Key == "outcome" && Equals(t.Value, "cancelled"));

        var activity = requestActivities[0];
        activity.Status.Should().Be(ActivityStatusCode.Unset, "被取消的 Span 不设置 Error 状态（OTel）");
        activity.Events.Should().Contain(e => e.Name == MudHttpDiagnosticNames.RequestFailed);
    }

    [Fact]
    public async Task CombinedPath_WithPollyRetry_Records_Single_Span_And_Metric()
    {
        // 组合路径 + Polly 重试（克隆携带 __mud_* 完整拷贝）：重试尝试全部短路，仅首次尝试采集一次。
        ResetCaptures();
        var requestMetrics = new List<(long value, KeyValuePair<string, object?>[] tags)>();
        using var meterListener = CreateRequestMetricListener(requestMetrics);

        var client = CreateCombinedClient(services =>
        {
            services.AddMudHttpResilienceDecorator(o =>
            {
                o.Retry.Enabled = true;
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.DelayMilliseconds = 1;
                o.Timeout.Enabled = false;
                o.CircuitBreaker.Enabled = false;
            });
        });

        var result = await client.SendAsync<string>(new HttpRequestMessage(HttpMethod.Get, "retry"));

        result.Should().Be("ok");
        _retryEndpointHits.Should().Be(3, "两次 500 失败后第三次成功");

        var requestActivities = RequestActivities();
        requestActivities.Should().ContainSingle("重试克隆携带 __mud_observed 标记，应全部短路");
        requestMetrics.Should().ContainSingle("重试场景下请求指标仍只采集一次");
    }

    [Fact]
    public async Task CombinedPath_TokenRecovery_RecoveryClone_Collected_Independently()
    {
        // 行为矩阵第 5 行：令牌恢复克隆剥离 __mud_* → 恢复尝试由 TracingDelegatingHandler 独立采集（NEW-HC-10）。
        ResetCaptures();
        var requestMetrics = new List<(long value, KeyValuePair<string, object?>[] tags)>();
        using var meterListener = CreateRequestMetricListener(requestMetrics);

        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpClient("combined_path_recovery", c => c.BaseAddress = new Uri("http://localhost/api/"))
            .ConfigurePrimaryHttpMessageHandler(() => new RecoveryPrimaryHandler());
        services.AddSingleton<ITokenManager>(mockTokenManager.Object);
        services.AddTransient<TokenRecoveryExecutor>();
        // 组合路径集成方式：以 TokenRecoveryEnhancedClient 装饰器承载恢复（克隆经 base.SendCoreAsync 重入完整管道）
        services.RemoveAll<IEnhancedHttpClient>();
        services.AddTransient<IEnhancedHttpClient>(sp => new TokenRecoveryEnhancedClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            "combined_path_recovery",
            sp.GetRequiredService<TokenRecoveryExecutor>()));
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IEnhancedHttpClient>();

        using var request = new HttpRequestMessage(HttpMethod.Get, "ok");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "expired");
        using var response = await client.SendRawAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 原始请求 1 个 Span（外层窗口）+ 恢复克隆 1 个 Span（剥离 __mud_* 后由管道内 TracingDelegatingHandler 独立采集）
        var requestActivities = RequestActivities();
        requestActivities.Count.Should().Be(2, "原始请求外层 1 个 + 恢复克隆独立 1 个（NEW-HC-10 语义保持）");
        requestMetrics.Count.Should().Be(2, "原始请求与恢复尝试各 1 组指标");
    }

    private MeterListener CreateRequestMetricListener(
        List<(long value, KeyValuePair<string, object?>[] tags)> sink)
    {
        var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName && instrument.Name == "mud.http.requests")
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            sink.Add((value, tags.ToArray())));
        meterListener.Start();
        return meterListener;
    }

    private List<Activity> RequestActivities()
        => _startedActivities
            .Where(a => a.OperationName == MudHttpActivitySource.ActivityNameRequest)
            .ToList();

    /// <summary>始终抛出 HttpRequestException 的主处理器（模拟传输层失败）。</summary>
    private sealed class ThrowingPrimaryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("模拟连接失败");
    }

    /// <summary>挂起至调用方令牌触发后抛 OCE 的主处理器（确定性取消模拟）。</summary>
    private sealed class CancellingPrimaryHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var reg = cancellationToken.Register(
                () => tcs.TrySetCanceled(cancellationToken));
            await tcs.Task.ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>首次返回 401，后续返回 200 的主处理器（令牌恢复场景）。</summary>
    private sealed class RecoveryPrimaryHandler : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            var statusCode = call == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("recovered")
            });
        }
    }
}
