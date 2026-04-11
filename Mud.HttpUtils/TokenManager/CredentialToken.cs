// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 统一的凭证令牌数据模型
/// </summary>
/// <remarks>
/// 表示从Web API获取的访问令牌信息，包含令牌内容、过期时间和响应状态。
/// </remarks>
public class CredentialToken
{
    /// <summary>
    /// 响应消息
    /// </summary>
    /// <remarks>
    /// API返回的错误消息或成功消息，null表示无消息。
    /// </remarks>
    public string? Msg { get; set; }

    /// <summary>
    /// 响应状态码
    /// </summary>
    /// <remarks>
    /// 0表示成功，非0表示错误状态码。
    /// </remarks>
    public int Code { get; set; }

    /// <summary>
    /// 令牌过期时间戳（毫秒）
    /// </summary>
    /// <remarks>
    /// Unix时间戳格式的过期时间，使用UTC时间。
    /// </remarks>
    public
#if NET7_0_OR_GREATER
    required
#endif
  long Expire
    { get; set; }

    /// <summary>
    /// 访问令牌
    /// </summary>
    /// <remarks>
    /// 用于API认证的访问令牌字符串，null表示未获取到令牌。
    /// </remarks>
    public
#if NET7_0_OR_GREATER
    required
#endif
  string? AccessToken
    { get; set; }

    /// <summary>
    /// 刷新令牌
    /// </summary>
    /// <remarks>
    /// 用于刷新访问令牌的刷新令牌，仅在用户授予 offline_access 权限时返回。
    /// </remarks>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// 刷新令牌过期时间戳（毫秒）
    /// </summary>
    /// <remarks>
    /// Unix时间戳格式的刷新令牌过期时间，使用UTC时间。
    /// </remarks>
    public long RefreshTokenExpire { get; set; }

    /// <summary>
    /// 权限范围
    /// </summary>
    /// <remarks>
    /// 本次请求所获得的 access_token 所具备的权限列表，以空格分隔。
    /// </remarks>
    public string? Scope { get; set; }


}
