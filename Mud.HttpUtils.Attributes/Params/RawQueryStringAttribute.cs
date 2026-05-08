// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数作为原始查询字符串，直接追加到 URL 的查询部分。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数，将参数值作为原始查询字符串直接追加到请求 URL 中。
/// 不会对参数值进行任何编码或格式化处理。
/// </para>
/// <para>
/// <strong>警告</strong>：使用此特性时，需要确保参数值是合法的查询字符串，
/// 不包含恶意内容。仅在信任参数来源时使用。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("/api/search")]
/// Task&lt;SearchResult&gt; SearchAsync([RawQueryString] string queryString);
/// 
/// // 调用: api.SearchAsync("keyword=test&amp;page=1");
/// // 生成: /api/search?keyword=test&amp;page=1
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false)]
public sealed class RawQueryStringAttribute : Attribute
{
}
