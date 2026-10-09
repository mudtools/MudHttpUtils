// -----------------------------------------------------------------------
//  M7-HC-01 回归：ConcatReadOnlyStream 顺序读取与流所有权
// -----------------------------------------------------------------------

using System.Text;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// M7-HC-01：<see cref="ConcatReadOnlyStream"/>（internal，经 InternalsVisibleTo 直测）——
/// Trace 日志路径以"32KB 截断缓冲 + 原始流剩余"拼接做全量反序列化的基础原语。
/// 覆盖：跨边界顺序读、三种读取重载、<c>leaveOpen</c> 所有权、不可 seek 契约。
/// </summary>
public class ConcatReadOnlyStreamTests
{
    private static ConcatReadOnlyStream CreateConcat(string first, string second, bool leaveOpen = false)
        => new(
            new MemoryStream(Encoding.UTF8.GetBytes(first)),
            new TrackingStream(Encoding.UTF8.GetBytes(second)),
            leaveOpen);

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task ReadAsync_SequentialAcrossBoundary_ReturnsFullContent()
    {
        // 跨边界：first 10 字节 + second 90 字节，分块读取不得丢字节、不得串序
        var first = "0123456789";
        var second = new string('x', 90);
        using var concat = CreateConcat(first, second, leaveOpen: true);

        var buffer = new byte[16];
        var sb = new StringBuilder();
        int read;
        while ((read = await concat.ReadAsync(buffer, 0, buffer.Length)) > 0)
            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));

        sb.ToString().Should().Be(first + second);
    }

    [Fact]
    public void Read_SyncAcrossBoundary_ReturnsFullContent()
    {
        using var concat = CreateConcat("abc", "defghij", leaveOpen: true);

        var buffer = new byte[4];
        var sb = new StringBuilder();
        int read;
        while ((read = concat.Read(buffer, 0, buffer.Length)) > 0)
            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));

        sb.ToString().Should().Be("abcdefghij");
    }

    [Fact]
    public async Task ReadAsync_MemoryOverload_ReadsAcrossBoundary()
    {
        using var concat = CreateConcat(new string('a', 5), new string('b', 5), leaveOpen: true);
        var buffer = new byte[10];
        var sb = new StringBuilder();

        int read;
        while ((read = await concat.ReadAsync(buffer.AsMemory())) > 0)
            sb.Append(Encoding.UTF8.GetString(buffer, 0, read));

        sb.ToString().Should().Be(new string('a', 5) + new string('b', 5));
    }

    [Fact]
    public async Task FirstStreamEmpty_ReadsFromSecond()
    {
        using var concat = CreateConcat(string.Empty, "only-second", leaveOpen: true);
        (await ReadAllAsync(concat)).Should().Be("only-second");
    }

    [Fact]
    public void ZeroCount_ReturnsZeroWithoutConsuming()
    {
        using var concat = CreateConcat("abc", "def", leaveOpen: true);
        concat.Read(Array.Empty<byte>(), 0, 0).Should().Be(0);
        // 未消耗任何数据：后续仍可完整读出
        var buffer = new byte[6];
        concat.Read(buffer, 0, 6).Should().Be(3, "count=0 不得推进 first 的读取位置");
        Encoding.UTF8.GetString(buffer, 0, 3).Should().Be("abc");
    }

    [Fact]
    public void LeaveOpenTrue_DoesNotDisposeUnderlyingStreams()
    {
        var first = new MemoryStream(Encoding.UTF8.GetBytes("a"));
        var second = new TrackingStream(Encoding.UTF8.GetBytes("b"));
        var concat = new ConcatReadOnlyStream(first, second, leaveOpen: true);

        concat.Dispose();

        second.Disposed.Should().BeFalse("leaveOpen=true：不得释放底层流（由外层 using 收口）");
        // MemoryStream 无 Disposed 观测，但可继续读证明未被释放
        first.Length.Should().Be(1);
    }

    [Fact]
    public void LeaveOpenFalse_DisposesBothUnderlyingStreams()
    {
        var first = new MemoryStream(Encoding.UTF8.GetBytes("a"));
        var second = new TrackingStream(Encoding.UTF8.GetBytes("b"));
        var concat = new ConcatReadOnlyStream(first, second, leaveOpen: false);

        concat.Dispose();

        second.Disposed.Should().BeTrue("leaveOpen=false：应级联释放底层流");
    }

    [Fact]
    public void SeekContract_CanSeekFalse_AndAccessorsThrow()
    {
        using var concat = CreateConcat("a", "b", leaveOpen: true);

        concat.CanSeek.Should().BeFalse();
        concat.CanWrite.Should().BeFalse();
        concat.CanRead.Should().BeTrue();

        Action readLength = () => _ = concat.Length;
        Action readPosition = () => _ = concat.Position;
        Action writePosition = () => concat.Position = 0;
        Action seek = () => concat.Seek(0, SeekOrigin.Begin);
        Action setLength = () => concat.SetLength(1);
        Action write = () => concat.Write(new byte[1], 0, 1);

        readLength.Should().Throw<NotSupportedException>();
        readPosition.Should().Throw<NotSupportedException>();
        writePosition.Should().Throw<NotSupportedException>();
        seek.Should().Throw<NotSupportedException>();
        setLength.Should().Throw<NotSupportedException>();
        write.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task StreamReaderOverConcat_ReadsCombinedContent()
    {
        // 反序列化路径等价性：StreamContent(JsonSerializer) 走的正是流式顺序读
        var payload = "{\"v\":" + new string('1', 20000) + "}";
        var half = payload.Length / 2;
        using var concat = CreateConcat(payload[..half], payload[half..], leaveOpen: true);

        (await ReadAllAsync(concat)).Should().Be(payload);
    }

    /// <summary>可观测 Dispose 的只读流（<c>MemoryStream</c> 无 Dispose 标志）。</summary>
    private sealed class TrackingStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public TrackingStream(byte[] data) => _data = data;

        public bool Disposed { get; private set; }

        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var toCopy = Math.Min(count, _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
