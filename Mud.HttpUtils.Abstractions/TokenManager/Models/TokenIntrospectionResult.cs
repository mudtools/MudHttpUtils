// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌内省结果。
/// </summary>
public class TokenIntrospectionResult
{
    /// <summary>
    /// 令牌是否有效。
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// 令牌的权限范围。
    /// </summary>
    public string[]? Scopes { get; set; }

    /// <summary>
    /// 客户端 ID。
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// 用户名。
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 令牌类型。
    /// </summary>
    public string? TokenType { get; set; }

    /// <summary>
    /// 过期时间（Unix 时间戳，秒）。
    /// </summary>
    public long? Exp { get; set; }

    /// <summary>
    /// 签发时间（Unix 时间戳，秒）。
    /// </summary>
    public long? Iat { get; set; }

    /// <summary>
    /// 生效时间（Unix 时间戳，秒）。
    /// </summary>
    public long? Nbf { get; set; }

    /// <summary>
    /// 主题（通常是用户 ID）。
    /// </summary>
    public string? Sub { get; set; }

    /// <summary>
    /// 受众。
    /// </summary>
    public string? Aud { get; set; }

    /// <summary>
    /// 签发者。
    /// </summary>
    public string? Iss { get; set; }

    /// <summary>
    /// JWT ID。
    /// </summary>
    public string? Jti { get; set; }
}
