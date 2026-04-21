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
}
