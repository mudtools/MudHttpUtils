// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// Token 请求参数，封装获取令牌所需的全部信息。
/// </summary>
/// <remarks>
/// 此类仅包含 Token 获取所需的信息，不包含注入模式等 HTTP 请求层面的概念。
/// 注入模式（InjectionMode）由生成代码在 HTTP 请求构建阶段处理，
/// 与 Token 获取逻辑无关。
/// </remarks>
public class TokenRequest
{
    /// <summary>
    /// TokenManager 的查找键，用于从 IMudAppContext 获取对应的令牌管理器。
    /// </summary>
    public string TokenManagerKey { get; set; } = string.Empty;

    /// <summary>
    /// 用户 ID。非空时使用 IUserTokenManager 获取用户令牌。
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 令牌作用域数组。
    /// </summary>
    public string[]? Scopes { get; set; }
}
