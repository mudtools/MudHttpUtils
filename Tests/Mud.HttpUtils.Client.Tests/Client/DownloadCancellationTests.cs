// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// M7-HC-02 回归：下载取消/超时原样透传（不包装为 HttpRequestException）+ 指标 outcome 三态口径

using System.Diagnostics.Metrics;
using Moq;
using Moq.Protected;
using Mud.HttpUtils.Tests;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// M7-HC-02：<c>DownloadLargeAsync</c> 失败路径的异常口径与可观测性 ——
/// 取消/超时（OCE）原样透传（与 byte[] 下载、执行器路径口径一致），
/// <c>RecordDownloadFailed</c> 按三态记 <c>outcome=cancelled/error</c>；
/// IO/HttpRequestException 行为回归不变。
/// </summary>
public class DownloadCancellationTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    public void Dispose() => _fixture.RestoreDomains();

    private static DirectEnhancedHttpClient CreateClient(HttpContent content)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        return new DirectEnhancedHttpClient(httpClient);
    }

    /// <summary>Handler 直接抛指定异常（模拟发送期失败，downloadStarted=false 路径）。</summary>
    private static DirectEnhancedHttpClient CreateThrowingClient(Exception ex)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(ex);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        return new DirectEnhancedHttpClient(httpClient);
    }

    private static HttpRequestMessage Request() =>
        new(HttpMethod.Get, new Uri("https://api.example.com/file"));

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"mud_test_{Guid.NewGuid():N}.bin");

    private static void Cleanup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".mudtmp")) File.Delete(path + ".mudtmp");
    }

    /// <summary>T5：中途取消 → 抛 OCE（非 HttpRequestException 包装），.mudtmp 清理、最终路径不存在。</summary>
    [Fact]
    public async Task DownloadLarge_CancelMidway_ThrowsOCE_AndCleansUp()
    {
        var data = new byte[1024 * 1024];
        new Random(11).NextBytes(data);
        var contentStream = new PartialThenBlockingStream(data, blockAfterBytes: 81920);
        var client = CreateClient(new StreamContent(contentStream));

        var tempFile = TempFile();
        using var cts = new CancellationTokenSource();
        try
        {
            var downloadTask = client.DownloadLargeAsync(Request(), tempFile, bufferSize: 8192, cancellationToken: cts.Token);

            await contentStream.WhenBlocked.WaitAsync(TimeSpan.FromSeconds(10));
            cts.Cancel();

            var act = async () => await downloadTask;
            // 修复前：包装为 HttpRequestException → 本断言失败（红测转绿）
            var ex = await act.Should().ThrowAsync<OperationCanceledException>();
            ex.Which.Should().NotBeOfType<HttpRequestException>(
                "HC-02：取消必须原样透传，不得包装为 HttpRequestException");

            File.Exists(tempFile).Should().BeFalse("取消后最终路径不得残留半写文件");
            File.Exists(tempFile + ".mudtmp").Should().BeFalse("取消后 .mudtmp 必须被清理");
        }
        finally
        {
            Cleanup(tempFile);
        }
    }

    /// <summary>T6：平台超时（TCE + inner TimeoutException，调用方令牌未触发）→ 原样透传，
    /// 内层保留，可被 TaskCancellationClassifier 归类；指标仍记 error（G32 三态：平台超时非用户取消）。</summary>
    [Fact]
    public async Task DownloadLarge_PlatformTimeout_TCE_Passthrough_WithInnerTimeout()
    {
        var tce = new TaskCanceledException("平台超时", new TimeoutException("timeout"));
        var client = CreateClient(new StreamContent(new ThrowingStream(tce)));

        var tempFile = TempFile();
        try
        {
            var act = async () => await client.DownloadLargeAsync(Request(), tempFile);

            var ex = await act.Should().ThrowAsync<TaskCanceledException>();
            ex.Which.InnerException.Should().BeOfType<TimeoutException>(
                "HC-02：TCE 必须原样透传且保留 inner TimeoutException（分类器依赖）");
            File.Exists(tempFile + ".mudtmp").Should().BeFalse("超时失败后 .mudtmp 必须被清理");
        }
        finally
        {
            Cleanup(tempFile);
        }
    }

    /// <summary>T7a：写盘期 IOException → 仍包装为 HttpRequestException（回归不变），inner 保留。</summary>
    [Fact]
    public async Task DownloadLarge_WriteIOException_StillWrappedAsHttpRequestException()
    {
        var client = CreateClient(new StreamContent(new ThrowingAfterBytesStream(new byte[81920])));

        var tempFile = TempFile();
        try
        {
            var act = async () => await client.DownloadLargeAsync(Request(), tempFile, bufferSize: 8192);

            var ex = await act.Should().ThrowAsync<HttpRequestException>();
            ex.Which.InnerException.Should().BeOfType<IOException>(
                "回归：非取消类异常仍包装为 HttpRequestException 并保留原始 inner");
            File.Exists(tempFile + ".mudtmp").Should().BeFalse();
        }
        finally
        {
            Cleanup(tempFile);
        }
    }

    /// <summary>T7b：发送期 HttpRequestException → 同一实例原样重抛（回归不变）。</summary>
    [Fact]
    public async Task DownloadLarge_SendHttpRequestException_PassthroughSameInstance()
    {
        var original = new HttpRequestException("连接失败");
        var client = CreateThrowingClient(original);

        var tempFile = TempFile();
        try
        {
            var act = async () => await client.DownloadLargeAsync(Request(), tempFile);

            var ex = await act.Should().ThrowAsync<HttpRequestException>();
            ex.Which.Should().BeSameAs(original, "HttpRequestException 必须原样重抛而非重新包装");
        }
        finally
        {
            Cleanup(tempFile);
        }
    }

    /// <summary>T12：用户取消（调用方 CT 已触发）→ DownloadDuration 记 outcome=cancelled；
    /// 平台超时（CT 未触发）→ 记 outcome=error（与 ExecuteWithObservabilityAsync 三态口径一致）。</summary>
    [Fact]
    public async Task DownloadLarge_Cancel_RecordsOutcomeCancelled()
    {
        var outcomes = CaptureDownloadOutcomes(async (client, tempFile, cts) =>
        {
            var data = new byte[1024 * 1024];
            new Random(13).NextBytes(data);
            var contentStream = new PartialThenBlockingStream(data, blockAfterBytes: 81920);
            var streamClient = CreateClient(new StreamContent(contentStream));

            var downloadTask = streamClient.DownloadLargeAsync(Request(), tempFile, bufferSize: 8192, cancellationToken: cts.Token);
            await contentStream.WhenBlocked.WaitAsync(TimeSpan.FromSeconds(10));
            cts.Cancel();

            var act = async () => await downloadTask;
            await act.Should().ThrowAsync<OperationCanceledException>();
        });

        outcomes.Should().Contain("cancelled", "T12：用户取消的下载失败指标 outcome 必须为 cancelled");
    }

    /// <summary>T12 反例：平台超时（CT 未触发）→ outcome=error，不误记 cancelled。</summary>
    [Fact]
    public async Task DownloadLarge_PlatformTimeout_RecordsOutcomeError()
    {
        var outcomes = CaptureDownloadOutcomes(async (client, tempFile, _) =>
        {
            var tce = new TaskCanceledException("平台超时", new TimeoutException("timeout"));
            var timeoutClient = CreateClient(new StreamContent(new ThrowingStream(tce)));

            var act = async () => await timeoutClient.DownloadLargeAsync(Request(), tempFile);
            await act.Should().ThrowAsync<TaskCanceledException>();
        });

        outcomes.Should().Contain("error", "G32：平台超时（调用方 CT 未触发）不记 cancelled");
        outcomes.Should().NotContain("cancelled");
    }

    /// <summary>启动 MeterListener 捕获 <see cref="MudHttpMeter.DownloadDuration"/> 的 outcome tag 并返回。</summary>
    private static List<string> CaptureDownloadOutcomes(
        Func<DirectEnhancedHttpClient, string, CancellationTokenSource, Task> scenario)
    {
        var outcomes = new List<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName && instrument.Name == "mud.http.download.duration")
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome" && tag.Value is string value)
                    outcomes.Add(value);
            }
        });
        meterListener.Start();

        var tempFile = TempFile();
        using var cts = new CancellationTokenSource();
        try
        {
            var client = CreateClient(new ByteArrayContent(new byte[16]));
            scenario(client, tempFile, cts).GetAwaiter().GetResult();
        }
        finally
        {
            Cleanup(tempFile);
        }

        return outcomes;
    }

    /// <summary>产出前 <c>blockAfterBytes</c> 字节后阻塞直至取消（确定性触发中途取消）。</summary>
    private sealed class PartialThenBlockingStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _blockAfterBytes;
        private readonly TaskCompletionSource _whenBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;

        public PartialThenBlockingStream(byte[] data, int blockAfterBytes)
        {
            _data = data;
            _blockAfterBytes = blockAfterBytes;
        }

        public Task WhenBlocked => _whenBlocked.Task;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("仅支持异步读取路径");

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = _data.Length - _position;
            if (remaining <= _blockAfterBytes)
            {
                _whenBlocked.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            var toCopy = Math.Min(buffer.Length, remaining);
            _data.AsMemory(_position, toCopy).CopyTo(buffer);
            _position += toCopy;
            return toCopy;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>首次读取即抛指定异常。</summary>
    private sealed class ThrowingStream : Stream
    {
        private readonly Exception _exception;

        public ThrowingStream(Exception exception) => _exception = exception;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => throw _exception;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(_exception);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromException<int>(_exception);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>先产出 <c>prefix</c> 字节，随后抛 <see cref="IOException"/>（模拟写盘期中断）。</summary>
    private sealed class ThrowingAfterBytesStream : Stream
    {
        private readonly byte[] _prefix;
        private int _position;

        public ThrowingAfterBytesStream(byte[] prefix) => _prefix = prefix;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _prefix.Length)
                throw new IOException("模拟下载中途网络中断");

            var toCopy = Math.Min(count, _prefix.Length - _position);
            Array.Copy(_prefix, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var temp = new byte[buffer.Length];
            var read = Read(temp, 0, buffer.Length);
            temp.AsMemory(0, read).CopyTo(buffer);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
