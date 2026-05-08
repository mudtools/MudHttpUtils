namespace Mud.HttpUtils;

/// <summary>
/// OAuth2 配置选项。
/// </summary>
public class OAuth2Options
{
    /// <summary>
    /// 配置节的名称。
    /// </summary>
    public const string SectionName = "MudHttpOAuth2";

    /// <summary>
    /// 客户端 ID。
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 客户端密钥。建议优先使用 <see cref="ClientSecretProviderName"/> 从安全存储获取。
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// 客户端密钥的安全提供程序名称。
    /// 当设置此值时，将通过 <see cref="ISecretProvider"/> 从安全存储中获取 ClientSecret，
    /// 而非使用明文配置的 <see cref="ClientSecret"/>。
    /// </summary>
    public string? ClientSecretProviderName { get; set; }

    /// <summary>
    /// 令牌端点。
    /// </summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// 撤销端点。
    /// </summary>
    public string RevocationEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// 内省端点。
    /// </summary>
    public string IntrospectionEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// 是否强制使用 HTTPS 端点。当设置为 true 时，如果端点不是 HTTPS 将抛出异常。
    /// 默认为 true。
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// 令牌过期安全边际（秒），默认 60 秒。
    /// 计算令牌过期时间时会从服务器返回的 expires_in 中减去此值，
    /// 以确保在令牌实际过期前提前刷新，避免因网络延迟导致使用已过期令牌。
    /// </summary>
    public int ExpirySafetyMarginSeconds { get; set; } = 60;
}
