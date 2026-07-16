// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Buffers;

namespace Mud.HttpUtils.Buffers;

/// <summary>
/// 基于 <see cref="ArrayPool{T}"/> 的高性能二进制写入器。
/// </summary>
/// <remarks>
/// <para>
/// 用于大 payload 序列化与 URL 拼接，减少大对象 GC 压力。
/// </para>
/// <para>
/// 放置于 <c>Mud.HttpUtils.Abstractions</c>（抽象包），使源生成器生成的代码可引用。
/// </para>
/// </remarks>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    /// <summary>默认初始缓冲区大小。</summary>
    public const int DefaultSize = 1024;

    private byte[] _buffer;
    private int _position;

    /// <summary>初始化 <see cref="PooledBufferWriter"/> 实例。</summary>
    public PooledBufferWriter()
    {
        _buffer = ArrayPool<byte>.Shared.Rent(DefaultSize);
        _position = 0;
    }

    /// <inheritdoc/>
    public void Advance(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count must not be negative.");
        if (_position > _buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(count), "Advanced too far.");
        _position += count;
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureFreeCapacity(sizeHint);
        return _buffer.AsMemory(_position);
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureFreeCapacity(sizeHint);
        return _buffer.AsSpan(_position);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_buffer.Length == 0) return;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = Array.Empty<byte>();
        _position = 0;
    }

    /// <summary>获取已写入数据的只读 span。</summary>
    public ReadOnlySpan<byte> AsReadOnlySpan() => _buffer.AsSpan(0, _position);

    /// <summary>获取已写入数据的长度。</summary>
    public int WrittenCount => _position;

    /// <summary>
    /// 将已写入数据复制到新数组并返回。
    /// </summary>
    public byte[] ToArray()
    {
        return _buffer.AsSpan(0, _position).ToArray();
    }

    private void EnsureFreeCapacity(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count must not be negative.");
        if (count == 0) count = 1;

        int currentLength = _buffer.Length;
        int freeCapacity = currentLength - _position;

        if (count <= freeCapacity) return;

        int growBy = Math.Max(count, currentLength);
        int newSize = checked(currentLength + growBy);

        var rent = ArrayPool<byte>.Shared.Rent(newSize);
        Array.Copy(_buffer, rent, _position);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = rent;
    }
}
