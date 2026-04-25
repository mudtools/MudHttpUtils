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
    /// 客户端密钥。
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

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
}
