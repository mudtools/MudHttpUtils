using System.Text.Json.Serialization;

namespace HttpClientApiTest.WebApi;

/// <summary>
/// 应用凭证
/// </summary>
public class AppCredentials
{
    /// <summary>
    /// <para> 应用唯一标识，创建应用后获得。有关app_id 的详细介绍。请参考通用参数介绍</para>
    /// <para>示例值： "cli_slkdjalasdkjasd"</para>
    /// </summary>
    [JsonPropertyName("app_id")]
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// <para>应用秘钥，创建应用后获得。有关 app_secret 的详细介绍，请参考通用参数介绍</para>
    /// <para>示例值： "dskLLdkasdjlasdKK"</para>
    /// </summary>
    [JsonPropertyName("app_secret")]
    public string AppSecret { get; set; } = string.Empty;
}

/// <summary>
/// API响应结果模型
/// </summary>
public class FeishuApiResult
{
    /// <summary>
    /// 错误码，0表示成功，非 0 取值表示失败
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// 错误描述
    /// </summary>
    [JsonPropertyName("msg")]
    public string? Msg { get; set; }
}

/// <summary>
/// 自建应用认证响应结果
/// </summary>
public class AppCredentialResult : TenantAppCredentialResult
{
    /// <summary>
    /// 应用访问凭证
    /// </summary>
    [JsonPropertyName("app_access_token")]
    public string? AppAccessToken { get; set; }
}

/// <summary>
/// 自建应用租户认证响应结果
/// </summary>
public class TenantAppCredentialResult : FeishuApiResult
{
    /// <summary>
    /// token 的过期时间，单位为秒
    /// </summary>
    [JsonPropertyName("expire")]
    public int Expire { get; set; } = 0;

    /// <summary>
    /// 租户访问凭证
    /// </summary>
    [JsonPropertyName("tenant_access_token")]
    public string? TenantAccessToken { get; set; }
}
