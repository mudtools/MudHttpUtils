// -----------------------------------------------------------------------
//  M5-HC-05 回归：重试克隆快照 / 不可重放预判 / 原始异常回填
// -----------------------------------------------------------------------

using System.Net;
using System.Text;
using Mud.HttpUtils; // IRequestContentReplayHint
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>HC-05：HttpRequestMessageCloner 快照与预判。</summary>
public class ClonerM5RegressionTests
{
    /// <summary>非 seekable 流包装（模拟网络流/管道）。</summary>
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

    /// <summary>声明不可重放且无 Content-Length 的内容。</summary>
    private sealed class NonReplayableChunkedContent : HttpContent, IRequestContentReplayHint
    {
        private readonly byte[] _data;
        public NonReplayableChunkedContent(byte[] data) => _data = data;
        bool IRequestContentReplayHint.IsReplayable => false;
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => stream.WriteAsync(_data, 0, _data.Length);
        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    [Fact]
    public async Task CloneAsync_ByteArrayContent_ThreeTimes_SameBytes()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/x")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello-world")),
        };

        for (var i = 0; i < 3; i++)
        {
            var clone = await HttpRequestMessageCloner.CloneAsync(request, 1024 * 1024);
            var bytes = await clone.Content!.ReadAsByteArrayAsync();
            Encoding.UTF8.GetString(bytes).Should().Be("hello-world");
            clone.Dispose();
        }
    }

    [Fact]
    public async Task CloneAsync_NonSeekableStreamContent_ThreeTimes_SnapshotPreventsEmptyBody()
    {
        // 关键回归：非 seekable + 无 Content-Length → 首次读完后源流 EOF；
        // 快照写入后第 2/3 次克隆必须仍能拿到完整字节（修复前为空体）
        var payload = Encoding.UTF8.GetBytes(new string('A', 512));
        var request = new HttpRequestMessage(HttpMethod.Put, "https://api.example.com/x")
        {
            Content = new StreamContent(new NonSeekableStream(payload)),
        };

        var sizes = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var clone = await HttpRequestMessageCloner.CloneAsync(request, 10 * 1024 * 1024);
            var bytes = await clone.Content!.ReadAsByteArrayAsync();
            sizes.Add(bytes.Length);
            clone.Dispose();
        }

        sizes.Should().Equal(512, 512, 512);
    }

    [Fact]
    public void ShouldSkipRetry_DeclaredLengthExceedsLimit_ReturnsTrue()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/x")
        {
            Content = new ByteArrayContent(new byte[100]),
        };
        // 声明长度 100 > 限制 10
        request.Content.Headers.ContentLength = 100;

        var skip = HttpRequestMessageCloner.ShouldSkipRetryForContent(request, 10, out var reason);

        skip.Should().BeTrue();
        reason.Should().Be("declared-length-exceeds-limit");
    }

    [Fact]
    public void ShouldSkipRetry_NonReplayableChunked_ReturnsTrue()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/x")
        {
            Content = new NonReplayableChunkedContent(Encoding.UTF8.GetBytes("data")),
        };

        var skip = HttpRequestMessageCloner.ShouldSkipRetryForContent(request, 1024 * 1024, out var reason);

        skip.Should().BeTrue();
        reason.Should().Be("non-replayable-chunked-content");
    }

    [Fact]
    public void ShouldSkipRetry_NormalContent_ReturnsFalse()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/x")
        {
            Content = new StringContent("ok"),
        };

        var skip = HttpRequestMessageCloner.ShouldSkipRetryForContent(request, 1024 * 1024, out var reason);

        skip.Should().BeFalse();
        reason.Should().BeEmpty();
    }

    [Fact]
    public async Task CopyMetadata_ExcludesSnapshotKey()
    {
        var source = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/a");
        var payload = new byte[] { 1, 2, 3 };
#if NETSTANDARD2_0
        source.Properties[HttpRequestMessageCloner.CloneSnapshotPropertyKey] = payload;
#else
        ((IDictionary<string, object>)source.Options)[HttpRequestMessageCloner.CloneSnapshotPropertyKey] = payload;
#endif

        var target = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/a");
        HttpRequestMessageCloner.CopyMetadata(source, target);

#if NETSTANDARD2_0
        target.Properties.ContainsKey(HttpRequestMessageCloner.CloneSnapshotPropertyKey).Should().BeFalse();
#else
        target.Options.TryGetValue(new HttpRequestOptionsKey<byte[]>(HttpRequestMessageCloner.CloneSnapshotPropertyKey), out _).Should().BeFalse();
#endif
    }
}
