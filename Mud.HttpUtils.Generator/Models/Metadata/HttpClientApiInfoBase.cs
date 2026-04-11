// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Models.Metadata;

/// <summary>
/// HttpClient API 信息的基类
/// </summary>
/// <remarks>
/// 包含所有 HttpClient API 相关的通用属性，提供统一的基础结构
/// </remarks>
internal abstract class HttpClientApiInfoBase
{
    /// <summary>
    /// 初始化 HttpClient API 信息基类
    /// </summary>
    /// <param name="namespaceName">命名空间名称</param>
    /// <param name="baseUrl">API 基础地址（可为空，运行时通过Options配置）</param>
    /// <param name="timeout">超时时间（秒）</param>
    /// <param name="registryGroupName">注册组名称</param>
    protected HttpClientApiInfoBase(string namespaceName, string? baseUrl, int timeout, string? registryGroupName = null)
    {
        Namespace = namespaceName ?? throw new ArgumentNullException(nameof(namespaceName));
        BaseUrl = baseUrl;
        Timeout = timeout;
        RegistryGroupName = registryGroupName;
    }

    /// <summary>
    /// 命名空间名称
    /// </summary>
    public string Namespace { get; }

    /// <summary>
    /// API 基础地址
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int Timeout { get; }

    /// <summary>
    /// 注册组名称
    /// </summary>
    /// <remarks>
    /// 用于按组生成服务注册函数，如果为空则使用默认注册函数
    /// </remarks>
    public string? RegistryGroupName { get; }
}
