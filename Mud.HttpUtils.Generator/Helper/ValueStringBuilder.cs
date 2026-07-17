// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Diagnostics;

namespace Mud.HttpUtils.Helpers;

/// <summary>
/// 栈分配初始缓冲区的字符串构建器（<c>ref struct</c>），超出栈缓冲时回退到 <see cref="ArrayPool{T}"/> 堆分配。
/// </summary>
/// <remarks>
/// 用于源生成器热路径（如 <see cref="Models.InterfaceModel"/> 指纹构建），减少 GC 压力。
/// Generator 项目不引用 Abstractions（避免循环依赖），故此处独立维护一份精简实现。
/// 注意：<see cref="ref struct"/> 不支持链式调用（无法返回 ref this），请使用分步 Append。
/// </remarks>
[DebuggerDisplay("Length = {Length}")]
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

    /// <summary>获取当前字符数。</summary>
    public int Length
    {
        get => _pos;
        set
        {
            Debug.Assert(value >= 0 && value <= _chars.Length);
            _pos = value;
        }
    }

    /// <summary>追加单个字符。</summary>
    public void Append(char value)
    {
        if (_pos >= _chars.Length)
            Grow(1);
        _chars[_pos++] = value;
    }

    /// <summary>追加字符串。</summary>
    public void Append(string value)
    {
        if (_pos + value.Length > _chars.Length)
            Grow(value.Length);
        value.AsSpan().CopyTo(_chars.Slice(_pos));
        _pos += value.Length;
    }

    /// <summary>追加只读字符跨度。</summary>
    public void Append(ReadOnlySpan<char> value)
    {
        if (_pos + value.Length > _chars.Length)
            Grow(value.Length);
        value.CopyTo(_chars.Slice(_pos));
        _pos += value.Length;
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

    /// <summary>构建结果字符串并归还池化缓冲区。</summary>
    public override string ToString()
    {
        var s = _pos == 0 ? string.Empty : _chars.Slice(0, _pos).ToString();
        if (_arrayToReturnToPool != null)
        {
            ArrayPool<char>.Shared.Return(_arrayToReturnToPool);
            _arrayToReturnToPool = null;
        }
        return s;
    }
}
