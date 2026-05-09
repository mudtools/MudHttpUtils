// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// Mud HttpClient 应用程序配置选项
/// </summary>
/// <remarks>
/// <para>用于配置多个命名 HttpClient 实例的全局选项。</para>
/// <para>配置示例：</para>
/// <code>
/// {
///   "MudHttpClients": {
///     "DefaultClientName": "Default",
///     "AllowedDomains": [ "api.example.com", "cdn.example.com" ],
///     "Clients": {
///       "Default": {
///         "BaseAddress": "https://api.example.com",
///         "TimeoutSeconds": 30
///       },
///       "ExternalApi": {
///         "BaseAddress": "https://external.api.com",
///         "TimeoutSeconds": 60
///       }
///     }
///   }
/// }
/// </code>
/// </remarks>
public class MudHttpClientApplicationOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "MudHttpClients";

    /// <summary>
    /// 命名的 HttpClient 配置集合
    /// </summary>
    /// <remarks>
    /// 键为客户端名称，值为该客户端的配置选项。
    /// 客户端名称不区分大小写。
    /// </remarks>
    public Dictionary<string, MudHttpClientOptions> Clients { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 默认客户端名称
    /// </summary>
    /// <remarks>
    /// 当未指定特定客户端时使用的默认客户端名称。
    /// 如果未设置，将使用第一个配置的客户端。
    /// </remarks>
    public string? DefaultClientName { get; set; }

    /// <summary>
    /// 全局允许的域名白名单
    /// </summary>
    /// <remarks>
    /// <para>配置后会在应用启动时自动调用 <see cref="UrlValidator.ConfigureAllowedDomains"/> 设置白名单。</para>
    /// <para>所有 HttpClient 实例共享此白名单。白名单内的域名无需 <see cref="MudHttpClientOptions.AllowCustomBaseUrls"/> 即可直接访问。</para>
    /// <para>如需在运行时动态修改白名单，可使用 <see cref="UrlValidator.AddAllowedDomain"/> 和 <see cref="UrlValidator.RemoveAllowedDomain"/>。</para>
    /// </remarks>
    public List<string> AllowedDomains { get; set; } = [];
}
