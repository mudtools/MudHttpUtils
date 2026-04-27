// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌注入模式枚举，指定令牌在 HTTP 请求中的注入位置。
/// </summary>
public enum TokenInjectionMode
{
    /// <summary>
    /// 将令牌注入到 HTTP 请求头中。
    /// </summary>
    Header = 0,

    /// <summary>
    /// 将令牌注入到 HTTP 请求的查询参数中。
    /// </summary>
    Query = 1,

    /// <summary>
    /// 将令牌注入到 HTTP 请求的路径中。
    /// </summary>
    Path = 2,

    /// <summary>
    /// API Key 认证（注入到请求头）。
    /// </summary>
    ApiKey = 3,

    /// <summary>
    /// HMAC 签名认证（计算请求签名并注入到请求头）。
    /// </summary>
    HmacSignature = 4,

    /// <summary>
    /// HTTP Basic 认证（将凭据编码为 Base64 注入到 Authorization 头）。
    /// </summary>
    BasicAuth = 5,

    /// <summary>
    /// 将令牌注入到 HTTP 请求的 Cookie 中。
    /// </summary>
    Cookie = 6,
}
