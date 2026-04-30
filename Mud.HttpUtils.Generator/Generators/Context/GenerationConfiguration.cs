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

    public bool IsAbstract { get; set; }

    public string? InheritedFrom { get; set; }

    public string? TokenManager { get; set; }

    /// <summary>
    /// 从特性中提取的原始 TokenManager 值（未经过互斥处理），
    /// 用于在 ValidateConfiguration 中正确检测 HttpClient 与 TokenManager 的互斥冲突。
    /// </summary>
    public string? RawTokenManager { get; set; }

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

    /// <summary>
    /// 接口级 TokenManagerKey（从 [Token(TokenManagerKey = "...")] 特性获取）。
    /// 当指定时，使用此键而非 TokenType 从 IMudAppContext 中查找令牌管理器。
    /// </summary>
    public string? TokenManagerKey { get; set; }

    /// <summary>
    /// 接口级是否需要 UserId（从 [Token(RequiresUserId = true)] 特性获取）。
    /// 当未显式指定时，根据 IsUserAccessToken 自动推断。
    /// </summary>
    public bool? RequiresUserId { get; set; }

    /// <summary>
    /// 是否有任何方法需要 UserId（包括接口级和方法级）。
    /// 用于决定是否在构造函数中注入 ICurrentUserContext。
    /// </summary>
    public bool AnyMethodRequiresUserId { get; set; }

    /// <summary>
    /// 接口的基础路径前缀（从 [BasePath] 特性获取）
    /// </summary>
    public string? BasePath { get; set; }
}
