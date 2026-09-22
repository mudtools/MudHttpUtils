// -----------------------------------------------------------------------
//  M6-HC-12 / M6-HC-14 回归：EnhancedHttpClient.DownloadLargeAsync 原子落盘与缓冲区钳制
// -----------------------------------------------------------------------

using Moq;
using Moq.Protected;
using Mud.HttpUtils.Tests;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// M6-HC-12/HC-14：<c>EnhancedHttpClient.DownloadLargeFileAsync</c> 原直接写最终路径 ——
/// 中途失败会残留半写文件；且 <c>bufferSize</c> 仅拒绝 <c>&lt;= 0</c>，超大值会直接
/// <c>new byte[bufferSize]</c> 触发 OOM。修复后改为 <c>.mudtmp</c> 临时文件 + 流关闭后原子
/// <c>File.Move</c>，并把 <c>bufferSize</c> 钳制到 [4 KiB, 4 MiB]。
/// </summary>
public class DownloadCleanupTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    public void Dispose() => _fixture.RestoreDomains();

    private static DirectEnhancedHttpClient CreateClient(HttpContent content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode) { Content = content });

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

    [Fact]
    public async Task DownloadLarge_MidStreamIOException_LeavesNoResidualFiles()
    {
        // HC-12：写入期 IOException —— 必须清理 .mudtmp，最终路径不得出现半写文件
        var client = CreateClient(new StreamContent(new ThrowingAfterBytesStream(new byte[81920])));

        var tempFile = TempFile();
        try
        {
            var act = async () => await client.DownloadLargeAsync(Request(), tempFile, bufferSize: 8192);

            await act.Should().ThrowAsync<HttpRequestException>();

            File.Exists(tempFile).Should().BeFalse(
                "HC-12：写入期失败后最终路径不得残留半写文件");
            File.Exists(tempFile + ".mudtmp").Should().BeFalse(
                "HC-12：写入期失败后临时文件必须被清理");
        }
        finally
        {
            Cleanup(tempFile);
        }
    }

    [Fact]
    public async Task DownloadLarge_OverwriteFalse_ExistingFile_ThrowsIOExceptionAndLeavesFileUntouched()
    {
        // HC-12：构造期 IOException（目标已存在）→ 维持既有语义直接重抛，不清理、不包裹
        var client = CreateClient(new ByteArrayContent(new byte[] { 9, 8, 7 }));

        var tempFile = TempFile();
        try
        {
            await File.WriteAllTextAsync(tempFile, "OLD");

            var act = async () => await client.DownloadLargeAsync(Request(), tempFile, overwrite: false);

            await act.Should().ThrowAsync<IOException>().WithMessage("文件已存在:*");

            (await File.ReadAllTextAsync(tempFile)).Should().Be("OLD",
                "HC-12：Overwrite=false 拒绝后原文件字节必须保持不变");
            File.Exists(tempFile + ".mudtmp").Should().BeFalse(
                "构造期失败尚未触碰任何文件，不得留下临时文件");
        }
        finally
        {
            Cleanup(tempFile);
        }
    }

    [Fact]
    public async Task DownloadLarge_WithHugeBufferSize_ClampedAndSucceeds()
    {
        // HC-14：int.MaxValue 必须被钳制到上限而非抛 OOM / ArgumentOutOfRangeException
        var data = new byte[300 * 1024];
        new Random(7).NextBytes(data);
        var client = CreateClient(new ByteArrayContent(data));

        var tempFile = TempFile();
        try
        {
            await client.DownloadLargeAsync(Request(), tempFile, bufferSize: int.MaxValue);

            (await File.ReadAllBytesAsync(tempFile)).Should().Equal(data,
                "HC-14：超大 bufferSize 被钳制后下载内容必须完整");
        }
        finally
        {
            Cleanup(tempFile);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2048)]   // 小于下界 4 KiB → 归一为默认值
    public async Task DownloadLarge_BufferSizeOutOfRange_UsesDefault(int bufferSize)
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var client = CreateClient(new ByteArrayContent(data));

        var tempFile = TempFile();
        try
        {
            await client.DownloadLargeAsync(Request(), tempFile, bufferSize: bufferSize);

            (await File.ReadAllBytesAsync(tempFile)).Should().Equal(data);
        }
        finally
        {
            Cleanup(tempFile);
        }
    }

    [Fact]
    public async Task DownloadLarge_Success_LeavesNoTmpResidual()
    {
        var data = new byte[64 * 1024];
        new Random(3).NextBytes(data);
        var client = CreateClient(new ByteArrayContent(data));

        var tempFile = TempFile();
        try
        {
            var fileInfo = await client.DownloadLargeAsync(Request(), tempFile);

            fileInfo.Length.Should().Be(data.Length);
            File.Exists(tempFile + ".mudtmp").Should().BeFalse(
                "HC-14：成功后临时文件必须被原子 Move 为最终文件");
        }
        finally
        {
            Cleanup(tempFile);
        }
    }

    /// <summary>先产出 <c>prefix</c> 字节，随后抛 <see cref="IOException"/>（模拟下载中途网络中断）。</summary>
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
            Array.Copy(_prefix, 0, buffer, offset, toCopy);
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

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}