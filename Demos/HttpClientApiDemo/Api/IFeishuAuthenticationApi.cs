namespace HttpClientApiTest.Api;

using System.Text.Json.Serialization;

/// <summary>
/// 飞书认证授权API测试接口
/// 测试飞书认证相关的API功能，包括获取tenant_access_token和app_access_token
/// </summary>
[HttpClientApi("https://api.dingtalk.com", HttpClient = nameof(IEnhancedHttpClient), Timeout = 60, RegistryGroupName = "Feishu")]
public interface IFeishuAuthenticationApi
{
    /// <summary>
    /// 测试：获取自建应用的tenant_access_token
    /// 接口：POST https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal
    /// 特点：使用完整URL，应用凭证通过Body传递
    /// </summary>
    /// <param name="credentials">应用唯一标识及应用秘钥信息</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/>取消操作令牌对象。</param>
    /// <remarks>
    /// <para>tenant_access_token 的最大有效期是 2 小时。</para>
    /// <para>剩余有效期小于 30 分钟时，调用本接口会返回一个新的 tenant_access_token，这会同时存在两个有效的 tenant_access_token。</para>
    /// <para>剩余有效期大于等于 30 分钟时，调用本接口会返回原有的 tenant_access_token。</para>
    /// </remarks>
    /// <returns></returns>
    [Post("https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal")]
    Task<TenantAppCredentialResult> GetTenantAccessTokenAsync(
        [Body] AppCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取自建应用的app_access_token
    /// 接口：POST https://open.feishu.cn/open-apis/auth/v3/app_access_token/internal
    /// 特点：使用完整URL，应用凭证通过Body传递
    /// </summary>
    /// <param name="credentials">应用唯一标识及应用秘钥信息</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/>取消操作令牌对象。</param>
    /// <remarks>
    /// <para>app_access_token 的最大有效期是 2 小时。</para>
    /// <para>剩余有效期小于 30 分钟时，调用本接口会返回一个新的 app_access_token，这会同时存在两个有效的 app_access_token。</para>
    /// <para>剩余有效期大于等于 30 分钟时，调用本接口会返回原有的 app_access_token。</para>
    /// </remarks>
    /// <returns></returns>
    [Post("https://open.feishu.cn/open-apis/auth/v3/app_access_token/internal")]
    Task<AppCredentialResult> GetAppAccessTokenAsync(
        [Body] AppCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取tenant_access_token（边界测试 - 空应用凭证）
    /// 接口：POST https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal
    /// 特点：使用完整URL，空应用凭证
    /// </summary>
    [Post("https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal")]
    Task<TenantAppCredentialResult> GetTenantAccessTokenWithEmptyCredentialsAsync(
        [Body] AppCredentials? credentials = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取app_access_token（边界测试 - 无效应用凭证格式）
    /// 接口：POST https://open.feishu.cn/open-apis/auth/v3/app_access_token/internal
    /// 特点：使用完整URL，无效应用凭证格式
    /// </summary>
    [Post("https://open.feishu.cn/open-apis/auth/v3/app_access_token/internal")]
    Task<AppCredentialResult> GetAppAccessTokenWithInvalidFormatAsync(
        [Body] InvalidAppCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取tenant_access_token（异常测试 - 无效app_id）
    /// 接口：POST https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal
    /// 特点：使用完整URL，无效app_id
    /// </summary>
    [Post("https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal")]
    Task<TenantAppCredentialResult> GetTenantAccessTokenWithInvalidAppIdAsync(
        [Body] AppCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取app_access_token（异常测试 - 无效app_secret）
    /// 接口：POST https://open.feishu.cn/open-apis/auth/v3/app_access_token/internal
    /// 特点：使用完整URL，无效app_secret
    /// </summary>
    [Post("https://open.feishu.cn/open-apis/auth/v3/app_access_token/internal")]
    Task<AppCredentialResult> GetAppAccessTokenWithInvalidAppSecretAsync(
        [Body] AppCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：获取tenant_access_token（默认值测试 - 使用默认参数）
    /// 接口：POST https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal
    /// 特点：使用完整URL，使用默认参数
    /// </summary>
    [Post("https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal")]
    Task<TenantAppCredentialResult> GetTenantAccessTokenWithDefaultsAsync(
        [Body] AppCredentials credentials = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：刷新tenant_access_token（特殊场景测试）
    /// 接口：POST https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/refresh
    /// 特点：使用完整URL，刷新token
    /// </summary>
    [Post("https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/refresh")]
    Task<TenantAppCredentialResult> RefreshTenantAccessTokenAsync(
        [Body] RefreshTokenRequest refreshRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 测试：刷新app_access_token（特殊场景测试）
    /// 接口：POST https://open.feishu.cn/open-apis/auth/v3/app_access_token/refresh
    /// 特点：使用完整URL，刷新token
    /// </summary>
    [Post("https://open.feishu.cn/open-apis/auth/v3/app_access_token/refresh")]
    Task<AppCredentialResult> RefreshAppAccessTokenAsync(
        [Body] RefreshTokenRequest refreshRequest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 无效应用凭证类
    /// 用于测试无效应用凭证格式的场景
    /// </summary>
    public class InvalidAppCredentials
    {
        /// <summary>
        /// 无效的应用凭证格式
        /// </summary>
        [JsonPropertyName("invalid_field")]
        public string InvalidField { get; set; }
    }

    /// <summary>
    /// 刷新Token请求类
    /// 用于测试刷新Token的场景
    /// </summary>
    public class RefreshTokenRequest
    {
        /// <summary>
        /// 应用唯一标识
        /// </summary>
        [JsonPropertyName("app_id")]
        public string AppId { get; set; }

        /// <summary>
        /// 应用秘钥
        /// </summary>
        [JsonPropertyName("app_secret")]
        public string AppSecret { get; set; }

        /// <summary>
        /// 待刷新的Token
        /// </summary>
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }
    }
}