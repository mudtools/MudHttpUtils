// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generators.Context;

/// <summary>
/// 生成配置
/// </summary>
internal class GenerationConfiguration
{
    public string HttpClientOptionsName { get; set; } = "HttpClientOptions";

    public string DefaultContentType { get; set; } = "application/json";

    public int Timeout { get; set; } = 100;

    public int TimeoutFromAttribute { get; set; } = 100;

    public string? BaseAddress { get; set; }

    public string? BaseAddressFromAttribute { get; set; }

    public bool IsAbstract { get; set; }

    public string? InheritedFrom { get; set; }

    public string? TokenManager { get; set; }

    public string? TokenManagerType { get; set; }

    /// <summary>
    /// HttpClient接口类型（与TokenManager互斥，优先使用）
    /// </summary>
    public string? HttpClient { get; set; }

    public string? TokenType { get; set; }

    /// <summary>
    /// 是否为用户访问令牌 (UserAccessToken)
    /// </summary>
    public bool IsUserAccessToken { get; set; }
}
