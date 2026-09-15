// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Helpers;

/// <summary>
/// N-2：成功响应体守卫流 —— 在 <b>读取阶段</b> 统计累计字节数，一旦超过上限立即抛出
/// <see cref="Mud.HttpUtils.ApiRequestException"/>，避免把超限内容全量读入内存后才发现超限（OOM 防护）。
/// </summary>
/// <remarks>
/// 供 <c>EnhancedHttpClient</c>（JSON/XML 反序列化、<c>byte[]</c> 下载）与
/// <c>DefaultHttpRequestExecutor</c>（生成代码路径响应读取）共用，确保
/// <c>MaxSuccessResponseBytes</c> 语义一致：
/// <list type="bullet">
/// <item>与 Content-Length 预判配合：已知长度超限在读取前即失败，未知长度（chunked）由本流兜底；</item>
/// <item>只读包装，不复制缓冲 —— 合规响应零额外拷贝，仅在累计读取超过上限的瞬间抛出；</item>
/// <item>累计读取 <b>等于</b> 上限不抛出（语义为"超过"才拒绝）。</item>
/// </list>
/// </remarks>
internal sealed class SuccessResponseGuardStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly string? _requestUri;
    private long _totalRead;

    public SuccessResponseGuardStream(Stream inner, long maxBytes, string? requestUri)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxBytes = maxBytes;
        _requestUri = requestUri;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set
        {
            // STJ 反序列化对可 seek 流会先探测 BOM 再回卷重读（Position = 0 / Seek(0, Begin)）。
            // 必须支持回卷而非抛 NotSupportedException，否则可 seek 的响应体在守卫模式下功能全坏。
            // 回卷时同步减少已读计数，避免重复读取同一段导致超限误报。
            _inner.Position = value;
            _totalRead = Math.Min(_totalRead, value);
        }
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        OnRead(read);
        return read;
    }

#if NET6_0_OR_GREATER
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        OnRead(read);
        return read;
    }
#else
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        OnRead(read);
        return read;
    }
#endif

    private void OnRead(int read)
    {
        if (read <= 0) return;
        _totalRead += read;
        if (_totalRead > _maxBytes)
        {
            throw new Mud.HttpUtils.ApiRequestException(
                $"成功响应体大小超过限制（{_maxBytes} 字节），已读取 {_totalRead} 字节",
                requestUri: _requestUri);
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        // 与 Position 回卷语义一致：STJ 等反序列化器对可 seek 流的回卷探测不得误伤计数。
        var newPos = _inner.Seek(offset, origin);
        _totalRead = Math.Min(_totalRead, newPos);
        return newPos;
    }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
