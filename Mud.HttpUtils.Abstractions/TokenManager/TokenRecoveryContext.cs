// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌恢复上下文，携带令牌注入信息以支持 <see cref="System.Net.Http.DelegatingHandler"/> 在 401 恢复时正确重新注入令牌。
/// <para>
/// 生成代码在构建 HTTP 请求时将此上下文附加到 <see cref="System.Net.Http.HttpRequestMessage.Properties"/> 中，
/// 键为 <see cref="PropertyKey"/>。恢复处理器读取此上下文以确定令牌的注入模式和位置。
/// </para>
/// </summary>
public sealed class TokenRecoveryContext
{
    /// <summary>
    /// 附加到 <see cref="System.Net.Http.HttpRequestMessage.Properties"/> 时使用的属性键。
    /// </summary>
    public const string PropertyKey = "__Mud_HttpUtils_TokenRecoveryContext";

    /// <summary>
    /// 获取或设置令牌注入模式。
    /// </summary>
    public TokenInjectionMode InjectionMode { get; set; } = TokenInjectionMode.Header;

    /// <summary>
    /// 获取或设置令牌注入的 Header 名称。
    /// <para>Header 模式默认为 "Authorization"；ApiKey 模式为自定义 Header 名称。</para>
    /// </summary>
    public string HeaderName { get; set; } = "Authorization";

    /// <summary>
    /// 获取或设置令牌的认证方案（如 "Bearer"），仅 Header 模式使用。
    /// </summary>
    public string TokenScheme { get; set; } = "Bearer";

    /// <summary>
    /// 获取或设置 Cookie 名称，仅 Cookie 模式使用。
    /// </summary>
    public string? CookieName { get; set; }

    /// <summary>
    /// 获取或设置查询参数名称，仅 Query 模式使用。
    /// </summary>
    public string? QueryParameterName { get; set; }

    /// <summary>
    /// 获取或设置用户 ID，用于用户级令牌恢复。
    /// <para>当令牌需要用户上下文时，生成代码将当前用户 ID 附加到此属性。</para>
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 获取或设置令牌管理器查找键，用于在恢复时定位正确的令牌管理器。
    /// </summary>
    public string? TokenManagerKey { get; set; }
}
