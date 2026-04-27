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
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
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
    /// <param name="formatString">格式化字符串，用于格式化参数值。</param>
    public QueryAttribute(string name, string? formatString)
        : this(name) =>
        FormatString = formatString;

    /// <summary>
    /// 获取或设置查询参数的名称。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 获取或设置格式化字符串，用于格式化参数值（如日期格式 "yyyy-MM-dd"）。
    /// </summary>
    public string? FormatString { get; set; }

    /// <summary>
    /// 获取或设置参数的别名，用于映射到不同的查询参数名。
    /// </summary>
    public string? AliasAs { get; set; }
}
