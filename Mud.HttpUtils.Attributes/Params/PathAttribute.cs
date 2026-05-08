// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数作为 URL 路径参数。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数或属性，指示该参数应替换请求 URI 中的路径占位符（如 /api/users/{id}）。
/// 支持自定义格式化字符串和 URL 编码控制。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("/api/users/{id}")]
/// Task&lt;User&gt; GetUserAsync([Path] int id);
/// 
/// // 使用格式化字符串
/// [Get("/api/reports/{reportDate}")]
/// Task&lt;Report&gt; GetReportAsync([Path(FormatString = "yyyy-MM-dd")] DateTime reportDate);
/// 
/// // 自定义占位符名称
/// [Get("/api/users/{userId}")]
/// Task&lt;User&gt; GetUserAsync([Path(Name = "userId")] int id);
/// 
/// // 禁用 URL 编码（用于传递包含 / 的路径）
/// [Get("/api/resource/{path}")]
/// Task GetResourceAsync([Path(UrlEncode = false)] string path);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false)]
public sealed class PathAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="PathAttribute"/> 类的新实例。
    /// </summary>
    public PathAttribute()
    {
    }

    /// <summary>
    /// 初始化 <see cref="PathAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="formatString">格式化字符串，用于格式化参数值（如日期格式 "yyyy-MM-dd"）。</param>
    public PathAttribute(string? formatString) =>
        FormatString = formatString;

    /// <summary>
    /// 获取或设置路径参数的名称（占位符名称）。
    /// </summary>
    /// <remarks>
    /// 如果未指定，则使用参数名称作为占位符名称。
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// 获取或设置格式化字符串，用于格式化路径参数值。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 支持以下格式化方式：
    /// <list type="bullet">
    /// <item>如果格式包含 {0}，则使用 string.Format 格式化</item>
    /// <item>如果参数实现 IFormattable，则调用 ToString(format, CultureInfo.InvariantCulture)</item>
    /// <item>否则调用 ToString()</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 日期格式化
    /// [Path(FormatString = "yyyy-MM-dd")]
    /// 
    /// // 数字格式化
    /// [Path(FormatString = "D5")]
    /// 
    /// // GUID 格式化
    /// [Path(FormatString = "N")]
    /// </code>
    /// </example>
    public string? FormatString { get; set; }

    /// <summary>
    /// 获取或设置格式化字符串的别名属性，等效于 <see cref="FormatString"/>。
    /// </summary>
    public string? Format
    {
        get => FormatString;
        set => FormatString = value;
    }

    /// <summary>
    /// 获取或设置一个值，该值指示是否对路径参数值进行 URL 编码。
    /// </summary>
    /// <value>默认为 true（启用 URL 编码）。</value>
    /// <remarks>
    /// <para>
    /// 默认情况下，路径参数值会进行 URL 编码，将特殊字符（如 /、?、&amp;）转义。
    /// 设置为 false 可以禁用编码，用于传递包含 / 的路径段或完整 URL 路径。
    /// </para>
    /// <para>
    /// <strong>警告</strong>：禁用 URL 编码时，需要确保参数值不包含恶意内容，
    /// 避免 URL 注入攻击。仅在信任参数来源时使用。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 传递包含 / 的路径
    /// [Get("/api/resource/{path}")]
    /// Task GetResourceAsync([Path(UrlEncode = false)] string path);
    /// 
    /// // 调用代码
    /// await api.GetResourceAsync("folder/subfolder/file.txt");
    /// // 实际请求: /api/resource/folder/subfolder/file.txt
    /// </code>
    /// </example>
    public bool UrlEncode { get; set; } = true;
}
