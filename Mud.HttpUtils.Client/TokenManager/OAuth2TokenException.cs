// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// SR-M2（P2.3，D8）OAuth2 令牌请求失败的类型化异常：调用方可编程区分故障类别。
/// </summary>
/// <remarks>
/// <para>
/// 继承自 <see cref="InvalidOperationException"/>——既有 <c>catch (InvalidOperationException)</c> 兼容；
/// 需要精确处理时按 <see cref="ErrorCode"/> 区分：
/// <c>invalid_grant</c>（refresh_token 被消费/过期/撤销，应清除回退）与
/// <c>invalid_client</c>（配置错误，清除无意义）等。
/// </para>
/// </remarks>
public sealed class OAuth2TokenException : InvalidOperationException
{
    /// <summary>OAuth2 错误码（RFC 6749 §5.2），如 "invalid_grant" / "invalid_client"。</summary>
    public string? ErrorCode { get; }

    /// <summary>服务端返回的错误描述（不含令牌本体）。</summary>
    public string? ErrorDescription { get; }

    /// <summary>HTTP 状态码（响应可解析时）。</summary>
    public int? HttpStatusCode { get; }

    internal OAuth2TokenException(string? code, string? description, int? statusCode)
        : base($"OAuth2 令牌请求失败: {code}" + (string.IsNullOrEmpty(description) ? "" : $" - {description}"))
    {
        ErrorCode = code;
        ErrorDescription = description;
        HttpStatusCode = statusCode;
    }
}
