// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌类型常量定义，提供标准化的令牌类型标识符。
/// </summary>
public static class TokenTypes
{
    /// <summary>
    /// 应用级别的访问令牌（如飞书的 TenantAccessToken）。
    /// </summary>
    public const string TenantAccessToken = "TenantAccessToken";

    /// <summary>
    /// 用户级别的访问令牌（如飞书的 UserAccessToken）。
    /// </summary>
    public const string UserAccessToken = "UserAccessToken";

    /// <summary>
    /// Bearer 认证方案前缀。
    /// </summary>
    public const string Bearer = "Bearer";

    /// <summary>
    /// Basic 认证方案前缀。
    /// </summary>
    public const string Basic = "Basic";
}
