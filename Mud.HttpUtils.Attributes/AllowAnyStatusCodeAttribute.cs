// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记接口或方法，指示 HTTP 请求不会在错误状态码时抛出异常。
/// </summary>
/// <remarks>
/// <para>
/// 默认情况下，当 HTTP 响应的状态码表示错误（4xx 或 5xx）时，生成的代码会抛出异常。
/// 应用此特性后，错误状态码不再抛出异常，而是返回响应内容。
/// </para>
/// <para>
/// 建议与 <see cref="HttpClient.Response{T}"/> 返回类型配合使用，
/// 以便同时获取状态码和响应内容。
/// </para>
/// <para>
/// 应用于接口级别时，该接口中所有方法均允许任何状态码。
/// 应用于方法级别时，仅该方法允许任何状态码，方法级别的设置覆盖接口级别。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 接口级别：所有方法均允许任何状态码
/// [HttpClientApi]
/// [AllowAnyStatusCode]
/// public interface IUserApi
/// {
///     [Get("/api/users/{id}")]
///     Task&lt;Response&lt;User&gt;&gt; GetUserAsync(int id);
/// }
/// 
/// // 方法级别：仅该方法允许任何状态码
/// [HttpClientApi]
/// public interface IUserApi
/// {
///     [AllowAnyStatusCode]
///     [Get("/api/users/{id}")]
///     Task&lt;Response&lt;User&gt;&gt; GetUserAsync(int id);
/// 
///     [Post("/api/users")]
///     Task&lt;User&gt; CreateUserAsync([Body] User user);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowAnyStatusCodeAttribute : Attribute
{
}
