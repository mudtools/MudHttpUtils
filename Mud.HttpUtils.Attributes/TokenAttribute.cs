using Mud.HttpUtils;

namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TokenAttribute : Attribute
{
    public TokenAttribute(string tokenType = "TenantAccessToken")
    {
        TokenType = tokenType;
    }

    public string TokenType { get; set; } = "TenantAccessToken";

    public TokenInjectionMode InjectionMode { get; set; } = TokenInjectionMode.Header;

    public string? Name { get; set; }

    public bool Replace { get; set; } = true;

    /// <summary>
    /// 获取或设置令牌作用域（Scopes），多个作用域用逗号分隔。
    /// </summary>
    /// <example>
    /// [Token(TokenType = "UserAccessToken", Scopes = "user:read,user:write")]
    /// </example>
    public string? Scopes { get; set; }
}
