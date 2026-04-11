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
    public HttpClientApiInfo(string interfaceName, string implementationName, string namespaceName, string baseUrl, int timeout, string? registryGroupName = null)
        : base(namespaceName, baseUrl, timeout, registryGroupName)
    {
        InterfaceName = interfaceName ?? throw new ArgumentNullException(nameof(interfaceName));
        ImplementationName = implementationName ?? throw new ArgumentNullException(nameof(implementationName));
    }

    /// <summary>
    /// 接口名称
    /// </summary>
    public string InterfaceName { get; }

    /// <summary>
    /// 实现类名称
    /// </summary>
    public string ImplementationName { get; }
}
