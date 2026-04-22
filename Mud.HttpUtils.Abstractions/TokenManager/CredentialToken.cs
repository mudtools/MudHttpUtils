namespace Mud.HttpUtils;

public class CredentialToken
{
    public string? Msg { get; set; }

    public int Code { get; set; }

    public
#if NET7_0_OR_GREATER
    required
#endif
    long Expire { get; set; }

    public
#if NET7_0_OR_GREATER
    required
#endif
    string? AccessToken { get; set; }

    public string? RefreshToken { get; set; }

    public long RefreshTokenExpire { get; set; }

    public string? Scope { get; set; }
}
