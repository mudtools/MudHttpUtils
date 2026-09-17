// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Mud.HttpUtils.Buffers;

/// <summary>
/// 栈分配初始缓冲区的字符串构建器（<c>ref struct</c>），超出栈缓冲时回退到 <see cref="ArrayPool{T}"/> 堆分配。
/// </summary>
/// <remarks>
/// <para>
/// 用于源生成器生成代码中的 URL 拼接，消费方通过 Abstractions 包引用。
/// </para>
/// <para>
/// 放置于 <c>Mud.HttpUtils.Abstractions</c>（抽象包），<c>internal</c> 可见性。
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal ref struct ValueStringBuilder
{
    private char[]? _arrayToReturnToPool;
    private Span<char> _chars;
    private int _pos;

    /// <summary>使用初始栈缓冲区初始化。</summary>
    /// <param name="initialBuffer">初始栈缓冲区。</param>
    public ValueStringBuilder(Span<char> initialBuffer)
    {
        _arrayToReturnToPool = null;
        _chars = initialBuffer;
        _pos = 0;
    }

    /// <summary>使用池化缓冲区初始化。</summary>
    /// <param name="initialCapacity">初始容量。</param>
    public ValueStringBuilder(int initialCapacity)
    {
        _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
        _chars = _arrayToReturnToPool;
        _pos = 0;
    }

    /// <summary>获取或设置当前字符数。</summary>
    public int Length
    {
        readonly get => _pos;
        set
        {
            Debug.Assert(value >= 0 && value <= _chars.Length);
            _pos = value;
        }
    }

    /// <summary>获取当前缓冲区总容量。</summary>
    public readonly int Capacity => _chars.Length;

    /// <summary>获取底层存储。</summary>
    public readonly Span<char> RawChars => _chars;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly string DebuggerDisplay => AsSpan().ToString();

    /// <summary>获取指定索引处的字符引用。</summary>
    public ref char this[int index]
    {
        get
        {
            Debug.Assert(index < _pos);
            return ref _chars[index];
        }
    }

    /// <summary>确保容量足够。</summary>
    public void EnsureCapacity(int capacity)
    {
        if ((uint)capacity <= (uint)_chars.Length) return;
        Grow(capacity - _pos);
    }

    /// <summary>追加字符串。</summary>
    public void Append(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        Append(value.AsSpan());
    }

    /// <summary>追加只读字符 span。</summary>
    public void Append(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return;
        EnsureCapacity(_pos + value.Length);
        value.CopyTo(_chars.Slice(_pos));
        _pos += value.Length;
    }

    /// <summary>追加单个字符。</summary>
    public void Append(char value)
    {
        EnsureCapacity(_pos + 1);
        _chars[_pos] = value;
        _pos++;
    }

    /// <summary>返回已写入内容的只读 span。</summary>
    public readonly ReadOnlySpan<char> AsSpan() => _chars.Slice(0, _pos);

    /// <summary>转换为字符串并释放缓冲区。</summary>
    public override string ToString()
    {
        var s = _chars.Slice(0, _pos).ToString();
        Dispose();
        return s;
    }

    /// <summary>释放池化缓冲区。</summary>
    public void Dispose()
    {
        if (_arrayToReturnToPool != null)
        {
            ArrayPool<char>.Shared.Return(_arrayToReturnToPool);
            _arrayToReturnToPool = null;
        }
        _chars = Span<char>.Empty;
        _pos = 0;
    }

    private void Grow(int additionalCapacity)
    {
        var newArray = ArrayPool<char>.Shared.Rent(Math.Max(_chars.Length + additionalCapacity, _chars.Length * 2));
        _chars.Slice(0, _pos).CopyTo(newArray);
        if (_arrayToReturnToPool != null)
            ArrayPool<char>.Shared.Return(_arrayToReturnToPool);
        _arrayToReturnToPool = newArray;
        _chars = newArray;
    }
}
