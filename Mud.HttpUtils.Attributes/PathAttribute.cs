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
/// 应用于方法参数，指示该参数应替换请求 URI 中的路径占位符（如 /api/users/{id}）。
/// 支持自定义格式化字符串。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("/api/users/{id}")]
/// Task&lt;User&gt; GetUserAsync([Path] int id);
/// 
/// // 使用格式化字符串（如日期格式）
/// [Get("/api/reports/{reportDate}")]
/// Task&lt;Report&gt; GetReportAsync([Path("yyyy-MM-dd")] DateTime reportDate);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
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
    /// 获取或设置格式化字符串，用于格式化路径参数值。
    /// </summary>
    public string? FormatString { get; set; }
}
