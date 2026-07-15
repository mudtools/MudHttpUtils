// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;

namespace Mud.HttpUtils.Generator.Models;

/// <summary>
/// 不可变且实现值相等的数组包装。用于 Roslyn 增量生成器的中间模型，
/// 使 <see cref="IEquatable{T}.Equals"/> 可用于判断两步之间是否需要重新 Emit。
/// 参考 Refit 的 ImmutableEquatableArray&lt;T&gt; 实现（for 循环 + 私有 Combine，非 LINQ）。
/// </summary>
/// <typeparam name="T">元素类型，必须实现 <see cref="IEquatable{T}"/>。</typeparam>
internal sealed class ImmutableEquatableArray<T> : IEquatable<ImmutableEquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[] _values;

    public ImmutableEquatableArray(T[] values) => _values = values;

    public int Count => _values.Length;

    public T this[int index] => _values[index];

    public bool Equals(ImmutableEquatableArray<T>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (other._values.Length != _values.Length)
        {
            return false;
        }

        for (var i = 0; i < _values.Length; i++)
        {
            if (!_values[i].Equals(other._values[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ImmutableEquatableArray<T> other && Equals(other);

    // 私有 Combine：rol5 旋转 + 加法混合，避免 LINQ Aggregate 分配
    private static int Combine(int h1, int h2)
    {
        var rol5 = ((uint)h1 << 5) | ((uint)h1 >> 27);
        return ((int)rol5 + h1) ^ h2;
    }

    public override int GetHashCode()
    {
        var hash = 0;
        for (var i = 0; i < _values.Length; i++)
        {
            hash = Combine(hash, _values[i].GetHashCode());
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}

/// <summary>
/// <see cref="ImmutableEquatableArray{T}"/> 的扩展方法。
/// </summary>
internal static class ImmutableEquatableArrayExtensions
{
    public static ImmutableEquatableArray<T> ToImmutableEquatableArray<T>(this IEnumerable<T> source)
        where T : IEquatable<T>
    {
        if (source is T[] array)
        {
            return new ImmutableEquatableArray<T>(array);
        }

        return new ImmutableEquatableArray<T>(new List<T>(source).ToArray());
    }
}
