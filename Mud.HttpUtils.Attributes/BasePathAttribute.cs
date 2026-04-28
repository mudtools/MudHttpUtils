// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记接口的基础路径前缀，应用于所有方法的 URL 构建。
/// </summary>
/// <remarks>
/// <para>
/// 应用于接口上，指定该接口所有方法的统一路径前缀。
/// Base Path 可以包含占位符（如 {tenantId}），通过接口级 Path 属性或方法参数提供值。
/// </para>
/// <para>
/// 完整的请求 URL 构建规则：
/// <list type="bullet">
/// <item>正常情况：[Base Address] + [Base Path] + [Method Path]</item>
/// <item>Method Path 以 / 开头：[Base Address] + [Method Path]（忽略 Base Path）</item>
/// <item>Method Path 是绝对 URL：[Method Path]（忽略 Base Address 和 Base Path）</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
/// [BasePath("api/v1")]
/// public interface IUserApi
/// {
///     [Get("users/{id}")]  // 实际路径: /api/v1/users/{id}
///     Task&lt;User&gt; GetUserAsync([Path] int id);
///     
///     [Get("/admin/users")]  // 以 / 开头，忽略 BasePath，实际路径: /admin/users
///     Task&lt;List&lt;User&gt;&gt; GetAllUsersAsync();
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class BasePathAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="BasePathAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="path">基础路径前缀，可以包含占位符（如 {tenantId}）。</param>
    public BasePathAttribute(string path)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>
    /// 获取基础路径前缀。
    /// </summary>
    public string Path { get; }
}
