namespace Mud.HttpUtils;

/// <summary>
/// 令牌类型常量定义，提供标准化的令牌类型标识符。
/// </summary>
public static class TokenTypes
{
    /// <summary>
    /// 应用级别的访问令牌（如飞书的 TenantAccessToken）。
    /// </summary>
    public const string TenantAccessToken = "TenantAccessToken";

    /// <summary>
    /// 用户级别的访问令牌（如飞书的 UserAccessToken）。
    /// </summary>
    public const string UserAccessToken = "UserAccessToken";

    /// <summary>
    /// Bearer 认证方案前缀。
    /// </summary>
    public const string Bearer = "Bearer";

    /// <summary>
    /// Basic 认证方案前缀。
    /// </summary>
    public const string Basic = "Basic";
}
