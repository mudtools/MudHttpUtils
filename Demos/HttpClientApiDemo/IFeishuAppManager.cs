namespace HttpClientApiTest;



public class FeishuAppContext : IMudAppContext
{
    /// <summary>
    /// HTTP客户端
    /// </summary>
    /// <remarks>
    /// 用于发送HTTP请求到远程API的客户端实例。
    /// 每个应用拥有独立的HTTP客户端实例。
    /// </remarks>
    public IEnhancedHttpClient HttpClient { get; }

    /// <summary>
    /// 根据令牌类型获取对应的令牌管理器
    /// </summary>
    /// <param name="tokenType">令牌类型</param>
    /// <returns></returns>
    public ITokenManager GetTokenManager(string tokenType) => throw new NotImplementedException();

    public void Dispose()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 租户令牌管理器
    /// </summary>
    /// <remarks>
    /// 用于获取和管理租户访问令牌（Tenant Access Token）。
    /// 租户令牌用于租户级别的权限验证。
    /// </remarks>
    public ITenantTokenManager TenantTokenManager { get; }

    /// <summary>
    /// 应用令牌管理器
    /// </summary>
    /// <remarks>
    /// 用于获取和管理应用身份访问令牌（App Access Token）。
    /// 应用令牌用于应用级别的权限验证。
    /// </remarks>
    public IAppTokenManager AppTokenManager { get; }

    /// <summary>
    /// 用户令牌管理器
    /// </summary>
    /// <remarks>
    /// 用于获取和管理用户访问令牌（User Access Token）。
    /// 用户令牌通过OAuth授权流程获取，需要用户授权。
    /// </remarks>
    public IUserTokenManager UserTokenManager { get; }

}


/// <summary>
/// 应用Key访问器接口
/// </summary>
public interface IAppKeyAccessor
{
    /// <summary>
    /// 获取应用Key
    /// </summary>
    /// <returns></returns>
    string GetAppKey();
}