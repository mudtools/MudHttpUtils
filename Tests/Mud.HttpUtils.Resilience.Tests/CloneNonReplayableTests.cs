// -----------------------------------------------------------------------
//  M6-HC-11 回归：无声明长度且非内存型白名单内容不可重放，克隆须显式拒绝
// -----------------------------------------------------------------------

using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// M6-HC-11：<c>ShouldSkipRetryForContent</c> 原仅预判「显式声明 <c>IsReplayable=false</c>」的内容，
/// 普通 <c>StreamContent</c> / 未知自定义 <c>HttpContent</c>（无 Content-Length）被漏判 ——
/// 克隆阶段读取会耗尽一次性源流，重试静默发送空体。修复后按类型白名单（<c>ByteArrayContent</c> /
/// <c>MultipartContent</c> 家族）判定，并在无声明长度且读得 0 字节时拒绝克隆。
/// </summary>
public class CloneNonReplayableTests
{
    /// <summary>不可 seek 的流（无 Content-Length，模拟网络流 / 管道）。</summary>
    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] data) => _inner = new MemoryStream(data, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>无 Content-Length、未实现可重放提示的自定义内容（模拟第三方 HttpContent）。</summary>
    private sealed class OpaqueChunkedContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static HttpRequestMessage Post(HttpContent content) =>
        new(HttpMethod.Post, "https://api.example.com/x") { Content = content };

    [Fact]
    public void ShouldSkipRetry_NonSeekableStreamContentWithoutLength_ReturnsTrue()
    {
        // StreamContent(non-seekable) → 无 Content-Length 且非 ByteArrayContent 家族
        var request = Post(new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes("payload"))));

        var skip = HttpRequestMessageCloner.ShouldSkipRetryForContent(request, 1024 * 1024, out var reason);

        skip.Should().BeTrue(
            "HC-11：无声明长度的 StreamContent 克隆会耗尽一次性源流，重试将静默发送空体");
        reason.Should().Be("undeclared-non-replayable-content");
    }

    [Fact]
    public void ShouldSkipRetry_OpaqueContentWithoutLength_ReturnsTrue()
    {
        var request = Post(new OpaqueChunkedContent());

        var skip = HttpRequestMessageCloner.ShouldSkipRetryForContent(request, 1024 * 1024, out var reason);

        skip.Should().BeTrue("HC-11：未知自定义内容按类型白名单判定为不可重放");
        reason.Should().Be("undeclared-non-replayable-content");
    }

    [Fact]
    public void ShouldSkipRetry_StreamContentWithDeclaredLength_ReturnsFalse()
    {
        // 可 seek 流的 StreamContent 会声明 Content-Length → 落入"已声明长度"路径，不触发本分支
        var request = Post(new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("payload"))));
        request.Content!.Headers.ContentLength.Should().NotBeNull();

        var skip = HttpRequestMessageCloner.ShouldSkipRetryForContent(request, 1024 * 1024, out var reason);

        skip.Should().BeFalse();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void ShouldSkipRetry_MultipartContent_ReturnsFalse()
    {
        // MultipartContent 在白名单内（每次发送重新生成流，可重放）
        var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("v"), "field");
        var request = Post(multipart);

        var skip = HttpRequestMessageCloner.ShouldSkipRetryForContent(request, 1024 * 1024, out var reason);

        skip.Should().BeFalse("HC-11：MultipartContent 属可重放白名单");
        reason.Should().BeEmpty();
    }

    [Fact]
    public async Task CloneAsync_ExhaustedNonSeekableStream_ThrowsInvalidOperationException()
    {
        // 源流已被读空：无声明长度 + 读得 0 字节 → 无法区分"合法空体"与"源流耗尽"
        var stream = new NonSeekableStream(Encoding.UTF8.GetBytes("hello"));
        var drained = new byte[5];
        stream.Read(drained, 0, drained.Length).Should().Be(5);

        var request = Post(new StreamContent(stream));

        var act = async () => await HttpRequestMessageCloner.CloneAsync(request, 1024 * 1024);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*无法确认可重放*");
    }

    [Fact]
    public async Task TryCloneAsync_ExhaustedNonSeekableStream_ReturnsNull()
    {
        // 降级语义：克隆不可行 → TryCloneAsync 返回 null，由调用方走 ExecuteWithoutRetryAsync
        var stream = new NonSeekableStream(Encoding.UTF8.GetBytes("hello"));
        var drained = new byte[5];
        stream.Read(drained, 0, drained.Length).Should().Be(5);

        var request = Post(new StreamContent(stream));

        var clone = await HttpRequestMessageCloner.TryCloneAsync(request, 1024 * 1024);

        clone.Should().BeNull("HC-11：克隆被拒时 TryCloneAsync 必须返回 null 而非抛异常");
    }

    [Fact]
    public async Task TryCloneAsync_ByteArrayContent_Succeeds()
    {
        var request = Post(new ByteArrayContent(Encoding.UTF8.GetBytes("hello")));

        var clone = await HttpRequestMessageCloner.TryCloneAsync(request, 1024 * 1024);

        clone.Should().NotBeNull();
        (await clone!.Content!.ReadAsStringAsync()).Should().Be("hello");
        clone.Dispose();
    }
}