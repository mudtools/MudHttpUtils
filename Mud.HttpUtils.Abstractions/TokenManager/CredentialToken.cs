// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 凭证令牌类，用于存储从认证服务器获取的令牌信息。
/// </summary>
public class CredentialToken
{
    /// <summary>
    /// 获取或设置消息描述。
    /// </summary>
    public string? Msg { get; set; }

    /// <summary>
    /// 获取或设置响应状态码。
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// 获取或设置令牌的过期时间（Unix 时间戳，毫秒）。
    /// </summary>
    public
#if NET7_0_OR_GREATER
    required
#endif
    long Expire
    { get; set; }

    /// <summary>
    /// 获取或设置访问令牌。
    /// </summary>
    public
#if NET7_0_OR_GREATER
    required
#endif
    string? AccessToken
    { get; set; }

    /// <summary>
    /// 获取或设置刷新令牌，用于获取新的访问令牌。
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 获取或设置刷新令牌的过期时间（Unix 时间戳，毫秒）。
    /// </summary>
    public long RefreshTokenExpire { get; set; }

    /// <summary>
    /// 获取或设置令牌的作用域。
    /// </summary>
    public string? Scope { get; set; }
}
