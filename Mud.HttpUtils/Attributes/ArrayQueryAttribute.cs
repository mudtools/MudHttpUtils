// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// HTTP 声明式数组查询参数特性
/// </summary>
/// <remarks>
/// 支持数组参数的不同格式：
/// 1. 数组元素用指定分隔符连接：user_ids=id0;id1;id2
/// 2. 数组元素重复键名：user_ids=id0&user_ids=id1&user_ids=id2
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Parameter,
    AllowMultiple = true)]
public sealed class ArrayQueryAttribute : Attribute
{
    /// <summary>
    ///     <inheritdoc cref="ArrayQueryAttribute" />
    /// </summary>
    /// <remarks>特性作用于参数时有效。</remarks>
    public ArrayQueryAttribute()
    {
    }

    /// <summary>
    ///     <inheritdoc cref="ArrayQueryAttribute" />
    /// </summary>
    /// <remarks>
    ///     <para>当特性作用于方法或接口时，则表示移除指定查询参数操作。</para>
    ///     <para>当特性作用于参数时，则表示添加查询参数，同时设置查询参数键为 <c>name</c> 的值。</para>
    /// </remarks>
    /// <param name="name">查询参数键</param>
    public ArrayQueryAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// <inheritdoc cref="ArrayQueryAttribute" />
    /// </summary>
    /// <param name="name">查询参数键</param>
    /// <param name="separator">数组元素分隔符，如果为null或空字符串，则使用重复键名格式</param>
    public ArrayQueryAttribute(string name, string? separator)
        : this(name) =>
        Separator = separator;

    /// <summary>
    /// 查询参数键
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 数组元素分隔符
    /// </summary>
    /// <remarks>
    /// 如果为null或空字符串，则使用重复键名格式：user_ids=id0&user_ids=id1&user_ids=id2
    /// 如果有值，则使用分隔符连接格式：user_ids=id0;id1;id2
    /// </remarks>
    public string? Separator { get; set; }
}