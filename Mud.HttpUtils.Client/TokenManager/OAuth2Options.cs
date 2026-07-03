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
/// <remarks>
/// <para>安全警告：字符串在 .NET 中不可变，无法被主动清除，可能被内存转储攻击获取。</para>
/// <para>生产环境强烈建议使用 <see cref="ClientSecretProviderName"/> 配合 <see cref="ISecretProvider"/> 
/// 从安全存储（如 Azure Key Vault、HashiCorp Vault）获取密钥，而非使用明文配置。</para>
/// <para>长期计划：在下一个大版本中将引入 <c>SecureClientSecret</c> 属性（byte[] 类型），
/// 支持使用后通过 <c>CryptographicOperations.ZeroMemory</c> 清除。</para>
/// </remarks>
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
