// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Models.Metadata;

/// <summary>
/// 表示 HttpClient API 的元数据信息
/// </summary>
/// <remarks>
/// 继承自 HttpClientApiInfoBase，包含 HttpClient API 接口特定的信息
/// </remarks>
internal sealed class HttpClientApiInfo : HttpClientApiInfoBase
{
    /// <summary>
    /// 初始化 HttpClient API 信息
    /// </summary>
    /// <param name="interfaceName">接口名称</param>
    /// <param name="implementationName">实现类名称</param>
    /// <param name="namespaceName">命名空间名称</param>
    /// <param name="baseUrl">API 基础地址</param>
    /// <param name="timeout">超时时间（秒）</param>
    /// <param name="registryGroupName">注册组名称</param>
    /// <param name="httpClientType">HttpClient 接口类型名称（与 tokenManager 互斥，优先使用）</param>
    /// <param name="tokenManagerType">Token 管理器类型名称</param>
    public HttpClientApiInfo(string interfaceName, string implementationName, string namespaceName, string baseUrl, int timeout, string? registryGroupName = null, string? httpClientType = null, string? tokenManagerType = null)
        : base(namespaceName, baseUrl, timeout, registryGroupName)
    {
        InterfaceName = interfaceName ?? throw new ArgumentNullException(nameof(interfaceName));
        ImplementationName = implementationName ?? throw new ArgumentNullException(nameof(implementationName));
        HttpClientType = httpClientType;
        TokenManagerType = tokenManagerType;
    }

    /// <summary>
    /// 接口名称
    /// </summary>
    public string InterfaceName { get; }

    /// <summary>
    /// 实现类名称
    /// </summary>
    public string ImplementationName { get; }

    /// <summary>
    /// HttpClient 接口类型名称（与 TokenManager 互斥，优先使用）
    /// </summary>
    /// <remarks>
    /// 当 [HttpClientApi(HttpClient = "IBaseHttpClient")] 设置了 HttpClient 属性时，构造函数依赖此类型。
    /// 需要通过 AddMudHttpClient 等方法在 DI 容器中注册此类型。
    /// </remarks>
    public string? HttpClientType { get; }

    /// <summary>
    /// Token 管理器类型名称
    /// </summary>
    /// <remarks>
    /// 当 [HttpClientApi(TokenManage = "IFeishuAppManager")] 设置了 TokenManage 属性时，构造函数依赖此类型。
    /// </remarks>
    public string? TokenManagerType { get; }
}
