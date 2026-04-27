// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------


namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数或方法作为数组查询参数。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数、接口或方法上，指示该参数为数组类型，应作为查询字符串发送。
/// 支持自定义参数名称和分隔符配置。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class ArrayQueryAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="ArrayQueryAttribute"/> 类的新实例。
    /// </summary>
    public ArrayQueryAttribute()
    {
    }

    /// <summary>
    /// 初始化 <see cref="ArrayQueryAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="name">查询参数的名称。</param>
    public ArrayQueryAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// 初始化 <see cref="ArrayQueryAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="name">查询参数的名称。</param>
    /// <param name="separator">数组元素的分隔符，如 ","、";" 等。如果为 null，将作为多个同名参数发送。</param>
    public ArrayQueryAttribute(string name, string? separator)
        : this(name) =>
        Separator = separator;

    /// <summary>
    /// 获取或设置查询参数的名称。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 获取或设置数组元素的分隔符。
    /// </summary>
    /// <remarks>
    /// 如果设置了分隔符，数组将序列化为单个查询参数（如 ?ids=1,2,3）。
    /// 如果为 null，数组将作为多个同名参数发送（如 ?ids=1&ids=2&ids=3）。
    /// </remarks>
    public string? Separator { get; set; }
}
