namespace Mud.HttpUtils;

/// <summary>
/// 令牌内省结果。
/// </summary>
public class TokenIntrospectionResult
{
    /// <summary>
    /// 令牌是否有效。
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// 令牌的权限范围。
    /// </summary>
    public string[]? Scopes { get; set; }

    /// <summary>
    /// 客户端 ID。
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 用户名。
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 令牌类型。
    /// </summary>
    public string? TokenType { get; set; }

    /// <summary>
    /// 过期时间（Unix 时间戳，秒）。
    /// </summary>
    public long? Exp { get; set; }

    /// <summary>
    /// 签发时间（Unix 时间戳，秒）。
    /// </summary>
    public long? Iat { get; set; }

    /// <summary>
    /// 生效时间（Unix 时间戳，秒）。
    /// </summary>
    public long? Nbf { get; set; }

    /// <summary>
    /// 主题（通常是用户 ID）。
    /// </summary>
    public string? Sub { get; set; }

    /// <summary>
    /// 受众。
    /// </summary>
    public string? Aud { get; set; }

    /// <summary>
    /// 签发者。
    /// </summary>
    public string? Iss { get; set; }

    /// <summary>
    /// JWT ID。
    /// </summary>
    public string? Jti { get; set; }
}
