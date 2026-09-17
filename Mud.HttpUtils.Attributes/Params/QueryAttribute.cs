// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;


/// <summary>
/// 标记参数或方法作为 HTTP 查询参数（Query String）。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数、接口或方法上，指示将参数添加为 URL 查询字符串。支持自定义参数名称、格式化和别名。
/// 可以在方法级别应用多次以添加多个固定查询参数。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 参数作为查询字符串
/// [Get("/api/users")]
/// Task&lt;List&lt;User&gt;&gt; GetUsersAsync([Query] string? keyword, [Query] int page = 1);
/// 
/// // 自定义参数名称
/// [Get("/api/users")]
/// Task&lt;List&lt;User&gt;&gt; GetUsersAsync([Query("page_size")] int pageSize);
/// 
/// // 方法级别添加固定查询参数
/// [Get("/api/users")]
/// [Query("status", "active")]
/// Task&lt;List&lt;User&gt;&gt; GetActiveUsersAsync();
/// 
/// // 使用别名
/// [Get("/api/users")]
/// Task&lt;List&lt;User&gt;&gt; SearchAsync([Query("q", AliasAs = "keyword")] string search);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = true)]
public sealed class QueryAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="QueryAttribute"/> 类的新实例。
    /// </summary>
    public QueryAttribute()
    {
    }

    /// <summary>
    /// 初始化 <see cref="QueryAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="name">查询参数的名称。</param>
    public QueryAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// 初始化 <see cref="QueryAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="name">查询参数的名称。</param>
    /// <param name="format">格式化字符串，用于格式化参数值。</param>
    public QueryAttribute(string name, string? format)
        : this(name) =>
        Format = format;

    /// <summary>
    /// 获取或设置查询参数的名称。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 获取或设置格式化字符串，用于格式化参数值（如日期格式 "yyyy-MM-dd"）。
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// 获取或设置参数的别名，用于映射到不同的查询参数名。
    /// </summary>
    public string? AliasAs { get; set; }

    /// <summary>
    /// 获取或设置数组元素的分隔符。仅对数组类型参数有效。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 如果设置了分隔符（如 ";"、","），数组将序列化为单个查询参数（如 ?ids=1;2;3）。
    /// </para>
    /// <para>
    /// 如果为 <c>null</c>（默认值），数组将作为多个同名参数发送（如 ?ids=1&amp;ids=2&amp;ids=3）。
    /// </para>
    /// </remarks>
    public string? Separator { get; set; }

    /// <summary>
    /// 获取或设置查询参数名的前缀。前缀将拼接到参数名前（如 Prefix="filter" + Name="keyword" => "filter.keyword"）。
    /// </summary>
    /// <remarks>
    /// 适用于复杂对象展平时为属性名添加统一前缀。
    /// 默认为 <c>null</c>（无前缀）。
    /// </remarks>
    public string? Prefix { get; set; }

    /// <summary>
    /// 获取或设置集合类型参数的序列化格式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 控制数组/集合参数在查询字符串中的格式化方式。
    /// </para>
    /// <para>
    /// 默认为 <see cref="QueryCollectionFormat.Multi"/>（重复参数模式：?ids=1&amp;ids=2）。
    /// 设置为 <see cref="QueryCollectionFormat.Csv"/> 等分隔模式时，将覆盖 <see cref="Separator"/> 的值。
    /// </para>
    /// </remarks>
    public QueryCollectionFormat CollectionFormat { get; set; } = QueryCollectionFormat.Multi;

    /// <summary>
    /// 获取或设置一个值，指示是否将参数值强制视为字符串（使用 ToString() 而非 JSON 序列化）。
    /// </summary>
    /// <remarks>
    /// 对于复杂类型参数，默认使用 JSON 序列化。
    /// 设置为 <c>true</c> 时，参数值将直接调用 ToString() 作为查询参数值，跳过 JSON 序列化。
    /// 默认为 <c>false</c>。
    /// </remarks>
    public bool TreatAsString { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示是否序列化 null 值（作为空查询参数）。
    /// </summary>
    /// <remarks>
    /// 默认情况下 null 值被跳过（不添加到查询字符串）。
    /// 设置为 <c>true</c> 时，null 值将序列化为空查询参数（如 ?keyword=）。
    /// 默认为 <c>false</c>。
    /// </remarks>
    public bool SerializeNull { get; set; }
}
