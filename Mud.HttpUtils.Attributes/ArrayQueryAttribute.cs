/// <summary>
/// 标记参数或方法作为数组查询参数。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数、接口或方法上，指示该参数为数组类型，应作为查询字符串发送。
/// 支持自定义参数名称和分隔符配置。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 数组作为多个查询参数（如 ?ids=1&ids=2&ids=3）
/// [Get("/api/users")]
/// Task&lt;List&lt;User&gt;&gt; GetUsersByIdsAsync([ArrayQuery("ids")] int[] ids);
/// 
/// // 使用分隔符（如 ?ids=1,2,3）
/// [Get("/api/products")]
/// Task&lt;List&lt;Product&gt;&gt; GetProductsAsync([ArrayQuery("ids", ",")] int[] ids);
/// </code>
/// </example>
namespace Mud.HttpUtils.Attributes;

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
