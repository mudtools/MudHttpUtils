// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// M1 回归用例：#2（克隆限量 + CT + 不修改原请求）+ #3（VersionPolicy / Properties / Options 拷贝）

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// M1-#2/#3：克隆器限量缓冲、取消传播、原请求不被修改、元数据完整拷贝。
/// </summary>
public class ClonerM1RegressionTests
{
    private static HttpRequestMessage CreatePost(string body, long? contentLengthHeader = null)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        if (contentLengthHeader.HasValue)
            content.Headers.ContentLength = contentLengthHeader;
        return new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/echo") { Content = content };
    }

    [Fact]
    public async Task CloneAsync_MissingContentLength_ExceedingLimit_Throws()
    {
        // T-2.1：Content-Length 缺失（chunked）+ 实际 1 MB + 限制 1024 → 抛 InvalidOperationException
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/upload")
        {
            Content = new ClientChunkedContent(new byte[1024 * 1024]),
        };

        var act = () => HttpRequestMessageCloner.CloneAsync(request, maxContentSize: 1024);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*最大克隆限制*");
    }

    [Fact]
    public async Task CloneAsync_DeclaredContentLength_ExceedingLimit_ThrowsWithoutReading()
    {
        // 声明长度已超限 → 直接判定（不多读一个字节）
        var request = CreatePost(new string('a', 4096), contentLengthHeader: 4096);

        var act = () => HttpRequestMessageCloner.CloneAsync(request, maxContentSize: 1024);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CloneAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        // T-2.2：克隆前已取消 → 快速失败 OperationCanceledException（不读内容）
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/upload")
        {
            Content = new ClientChunkedContent(new byte[1024 * 1024]),
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => HttpRequestMessageCloner.CloneAsync(request, maxContentSize: -1, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CloneAsync_CancelDuringLimitedRead_PropagatesThroughStreamRead()
    {
        // 限量路径（chunked + 限制）：取消经 Stream.ReadAsync 透传（慢速内容保证取消窗口内仍在读）
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/upload")
        {
            Content = new SlowChunkedContent(totalBytes: 10 * 1024 * 1024, writeDelayMs: 20),
        };
        using var cts = new CancellationTokenSource(100);

        var act = () => HttpRequestMessageCloner.CloneAsync(request, maxContentSize: 100 * 1024 * 1024, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CloneAsync_DoesNotModifyOriginalRequest()
    {
        // T-2.3：克隆后原请求未被修改（Content 引用与头部不变）
        var request = CreatePost("""{"name":"test"}""");
        var originalContentRef = request.Content;
        var originalHeaders = request.Content!.Headers.ContentLength;

        var clone = await HttpRequestMessageCloner.CloneAsync(request, maxContentSize: 1024 * 1024);

        request.Content.Should().BeSameAs(originalContentRef);
        request.Content!.Headers.ContentLength.Should().Be(originalHeaders);
        clone.Should().NotBeSameAs(request);
        (await clone.Content!.ReadAsStringAsync()).Should().Be("""{"name":"test"}""");
    }

    [Fact]
    public async Task CloneAsync_NegativeLimit_NoLimit()
    {
        // T-2.4 / 既有语义：-1 = 不限制
        var request = CreatePost(new string('b', 100));

        var clone = await HttpRequestMessageCloner.CloneAsync(request, maxContentSize: -1);

        (await clone.Content!.ReadAsStringAsync()).Should().HaveLength(100);
    }

    [Fact]
    public async Task CloneAsync_SameRequestCanBeClonedTwice_Replay()
    {
        // 重试场景：同一请求需可多次克隆（内容可重放）
        var request = CreatePost("""{"name":"test"}""");

        var clone1 = await HttpRequestMessageCloner.CloneAsync(request, maxContentSize: 1024 * 1024);
        var clone2 = await HttpRequestMessageCloner.CloneAsync(request, maxContentSize: 1024 * 1024);

        (await clone1.Content!.ReadAsStringAsync()).Should().Be("""{"name":"test"}""");
        (await clone2.Content!.ReadAsStringAsync()).Should().Be("""{"name":"test"}""");
    }

    [Fact]
    public async Task CloneAsync_PreservesVersionPolicy()
    {
        // T-3.1：克隆保留 VersionPolicy（HTTP/2-only 重试不静默降级）
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/values")
        {
            Version = new Version(2, 0),
        };
#if NET5_0_OR_GREATER
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
#endif

        var clone = await HttpRequestMessageCloner.CloneAsync(request, maxContentSize: -1);

        clone.Version.Should().Be(new Version(2, 0));
#if NET5_0_OR_GREATER
        clone.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionExact);
#endif
    }

    [Fact]
    public async Task CloneAsync_PreservesRequestHeadersAndContentHeaders()
    {
        var request = CreatePost("""{"a":1}""");
        request.Headers.Add("X-Custom", "value");
        var clone = await HttpRequestMessageCloner.CloneAsync(request, maxContentSize: -1);

        clone.Headers.GetValues("X-Custom").Should().Contain("value");
        clone.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }
}

/// <summary>不声明 Content-Length 的流式内容（模拟 chunked 上传）。</summary>
file sealed class ClientChunkedContent : HttpContent
{
    private readonly byte[] _payload;

    public ClientChunkedContent(byte[] payload) => _payload = payload;

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[81920];
        var offset = 0;
        while (offset < _payload.Length)
        {
            var size = Math.Min(buffer.Length, _payload.Length - offset);
            Array.Copy(_payload, offset, buffer, 0, size);
            await stream.WriteAsync(buffer.AsMemory(0, size)).ConfigureAwait(false);
            offset += size;
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false; // 无 Content-Length → chunked 语义
    }
}

/// <summary>慢速流式内容：每块写入间隔 delay，确保取消窗口内仍在读取。</summary>
file sealed class SlowChunkedContent : HttpContent
{
    private readonly int _totalBytes;
    private readonly int _writeDelayMs;

    public SlowChunkedContent(int totalBytes, int writeDelayMs)
    {
        _totalBytes = totalBytes;
        _writeDelayMs = writeDelayMs;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[81920];
        var written = 0;
        while (written < _totalBytes)
        {
            await stream.WriteAsync(buffer.AsMemory()).ConfigureAwait(false);
            written += buffer.Length;
            await Task.Delay(_writeDelayMs).ConfigureAwait(false);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
