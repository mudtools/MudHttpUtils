// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// M7-HC-01：只读顺序组合流 —— 先读尽 <c>first</c>，随后顺序读取 <c>second</c>。
/// </summary>
/// <remarks>
/// 用于 Trace 日志路径：日志只需前 <c>MaxDebugLogBodyLength</c> 字节的截断缓冲（memoryStream），
/// 而业务反序列化必须读到<b>全量</b>响应体 —— 组合流以"已缓冲前缀 + 原始流剩余"的顺序拼接，
/// 使两者共用同一份已消费数据，内存有界且 chunked（无 Content-Length）响应同样可用。
/// <para>
/// 所有权语义与 <c>EnhancedHttpClient.DisposableStream</c> 范式一致：<c>leaveOpen: true</c> 时
/// <see cref="Stream.Dispose(bool)"/> 不释放底层两段流（由外层既有 <c>using</c>/<c>await using</c> 收口）。
/// </para>
/// <para>
/// 零反射、AOT 安全；<see cref="CanSeek"/> 恒为 <c>false</c>（顺序单次读取，不可回卷，
/// 与 System.Text.Json 反序列化对不可 seek 流的顺序读取契约一致）。
/// </para>
/// </remarks>
internal sealed class ConcatReadOnlyStream : Stream
{
    private readonly Stream _first;
    private readonly Stream _second;
    private readonly bool _leaveOpen;
    private bool _firstExhausted;
    private bool _disposed;

    /// <param name="first">先读的流（通常为日志截断缓冲）。</param>
    /// <param name="second"><c>first</c> 读尽后接续读取的流（通常为原始响应流）。</param>
    /// <param name="leaveOpen">是否在释放时不级联释放底层两段流（默认 <c>false</c>）。</param>
    public ConcatReadOnlyStream(Stream first, Stream second, bool leaveOpen = false)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _second = second ?? throw new ArgumentNullException(nameof(second));
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException("组合流为顺序只读流，不支持查询长度。");
    public override long Position
    {
        get => throw new NotSupportedException("组合流为顺序只读流，不支持查询位置。");
        set => throw new NotSupportedException("组合流为顺序只读流，不支持设置位置。");
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0) return 0;
        if (!_firstExhausted)
        {
            var read = _first.Read(buffer, offset, count);
            if (read > 0) return read;
            _firstExhausted = true;
        }
        return _second.Read(buffer, offset, count);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count == 0) return 0;
        if (!_firstExhausted)
        {
#if NETSTANDARD2_0
            var read = await _first.ReadAsync(buffer, offset, count).ConfigureAwait(false);
#else
            var read = await _first.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
#endif
            if (read > 0) return read;
            _firstExhausted = true;
        }
#if NETSTANDARD2_0
        return await _second.ReadAsync(buffer, offset, count).ConfigureAwait(false);
#else
        return await _second.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
#endif
    }

#if !NETSTANDARD2_0
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;
        if (!_firstExhausted)
        {
            var read = await _first.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0) return read;
            _firstExhausted = true;
        }
        return await _second.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
#endif

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("组合流为顺序只读流，不支持寻址。");
    public override void SetLength(long value) => throw new NotSupportedException("组合流为顺序只读流，不支持设置长度。");
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("组合流为只读流，不支持写入。");

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing && !_leaveOpen)
            {
                _first.Dispose();
                _second.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
