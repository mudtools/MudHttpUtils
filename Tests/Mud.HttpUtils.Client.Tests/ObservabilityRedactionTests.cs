// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// G26/G27/G33 验收测试：下载遥测脱敏与双路径对齐。
/// </summary>
/// <remarks>
/// 覆盖：
/// - G26：下载事件（DownloadStarted/Completed）payload 与 tags 中的 URL 经脱敏；
/// - G27：大文件下载失败日志 URL 脱敏；
/// - G33：直连路径（DirectEnhancedHttpClient）下载产出 mud.http.download.bytes/duration 指标与事件。
/// </remarks>
public class ObservabilityRedactionTests : IDisposable
{
    private readonly ActivityListener _activityListener;
    private readonly List<Activity> _startedActivities = new();

    public ObservabilityRedactionTests()
    {
        UrlValidator.AddAllowedDomain("api.example.com");

        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MudHttpActivitySource.Name,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => _startedActivities.Add(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    public void Dispose() => _activityListener.Dispose();

    private static DirectEnhancedHttpClient CreateClient(HttpMessageHandler handler, ILogger? logger = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };
        var options = new EnhancedHttpClientOptions { AllowCustomBaseUrls = true };
        if (logger != null)
            options.Logger = logger;
        return new DirectEnhancedHttpClient(httpClient, options);
    }

    [Fact]
    public async Task Download_DirectPath_Emits_Redacted_Events_And_Download_Metrics()
    {
        var payload = new byte[1024];

        var downloadBytes = new List<(long value, KeyValuePair<string, object?>[] tags)>();
        var downloadDurations = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != MudHttpMeter.MeterName)
                    return;
                if (instrument.Name is "mud.http.download.bytes" or "mud.http.download.duration")
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "mud.http.download.bytes")
                downloadBytes.Add((value, tags.ToArray()));
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "mud.http.download.duration")
                downloadDurations.Add(1);
        });
        meterListener.Start();

        var handler = new StubContentHandler(payload);
        var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "dl?access_token=supersecret777");
        var bytes = await client.DownloadAsync(request);

        bytes.Should().NotBeNull();
        bytes!.Length.Should().Be(payload.Length);

        // G33：直连路径产出下载指标（不同仪表，不构成请求指标重复计数）
        downloadBytes.Should().ContainSingle();
        downloadBytes[0].value.Should().Be(payload.Length);
        downloadDurations.Should().ContainSingle();

        // G26：下载事件 tags 中 URL 脱敏
        var requestActivity = _startedActivities.LastOrDefault(a =>
            a.OperationName == MudHttpActivitySource.ActivityNameRequest);
        requestActivity.Should().NotBeNull();
        var events = requestActivity!.Events.ToList();
        events.Should().Contain(e => e.Name == MudHttpDiagnosticNames.DownloadStarted);
        events.Should().Contain(e => e.Name == MudHttpDiagnosticNames.DownloadCompleted);

        var startedUrl = events.First(e => e.Name == MudHttpDiagnosticNames.DownloadStarted)
            .Tags.FirstOrDefault(t => t.Key == "url").Value as string;
        startedUrl.Should().NotBeNull();
        startedUrl.Should().NotContain("supersecret777", "下载事件 URL 必须脱敏（G26）");
        startedUrl.Should().Contain("***REDACTED***");
    }

    [Fact]
    public async Task DownloadLarge_Failure_Log_Does_Not_Contain_Sensitive_Query()
    {
        // G27：大文件下载失败日志的 URL 经脱敏（500 触发 SendAndValidateAsync 抛 ApiException）
        var logger = new CapturingLogger();
        var handler = new StubStatusCodeHandler(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler, logger);

        var filePath = Path.Combine(Path.GetTempPath(), $"mud_g27_{Guid.NewGuid():N}.tmp");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "big?access_token=supersecret555");
            var act = async () => await client.DownloadLargeAsync(request, filePath);

            await act.Should().ThrowAsync<HttpRequestException>();

            logger.Messages.Should().NotBeEmpty("失败路径必须记录日志");
            logger.Messages.Should().NotContain(m => m.Contains("supersecret555"), "大文件下载失败日志 URL 必须脱敏（G27）");
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private sealed class StubContentHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public StubContentHandler(byte[] payload) => _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            });
    }

    private sealed class StubStatusCodeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StubStatusCodeHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("error")
            });
    }
}

/// <summary>测试用日志捕获器（记录格式化后的消息文本）。</summary>
internal sealed class CapturingLogger : ILogger
{
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));
}

/// <summary>测试用日志捕获器（泛型，适配 ILogger&lt;T&gt; 构造参数）。</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));
}
