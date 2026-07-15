// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数为 HTTP 请求头集合（字典动态头）。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数上，参数类型须为 <c>IDictionary&lt;string, string?&gt;</c> 或 <c>IDictionary&lt;string, object?&gt;</c>。
/// 生成代码在构造 <c>HttpRequestMessage</c> 时遍历字典，将每个键值对添加为 HTTP 请求头。
/// </para>
/// <para>
/// 与 <see cref="HeaderAttribute"/> 的职责区分：
/// <list type="bullet">
/// <item><description><see cref="HeaderAttribute"/> = 单个静态/参数化请求头</description></item>
/// <item><description><see cref="HeaderCollectionAttribute"/> = 批量字典请求头（运行时动态键值对）</description></item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Get("/api/data")]
/// Task&lt;string&gt; GetDataAsync([HeaderCollection] IDictionary&lt;string, string?&gt; headers);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class HeaderCollectionAttribute : Attribute
{
}
