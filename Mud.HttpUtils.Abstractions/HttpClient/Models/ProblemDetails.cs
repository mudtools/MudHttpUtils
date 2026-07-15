// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;

namespace Mud.HttpUtils;

/// <summary>
/// RFC 7807 Problem Details 错误响应。
/// </summary>
/// <remarks>
/// 表示 HTTP API 返回的标准化错误响应（application/problem+json）。
/// 与 <see cref="ValidationApiException"/> 配合使用，提供结构化的错误信息。
/// </remarks>
public class ProblemDetails
{
    /// <summary>问题类型的 URI 引用（可选）。</summary>
    public string? Type { get; set; }

    /// <summary>问题的简短摘要。</summary>
    public string? Title { get; set; }

    /// <summary>HTTP 状态码。</summary>
    public int? Status { get; set; }

    /// <summary>问题的详细描述。</summary>
    public string? Detail { get; set; }

    /// <summary>问题实例的 URI 引用（可选）。</summary>
    public string? Instance { get; set; }

    /// <summary>验证错误详情（字段名 → 错误消息数组）。</summary>
    public Dictionary<string, string[]>? Errors { get; set; }

    /// <summary>扩展成员（非标准字段）。</summary>
    public Dictionary<string, object?>? Extensions { get; set; }
}

/// <summary>
/// 带有 <see cref="ProblemDetails"/> 的 API 异常。
/// </summary>
/// <remarks>
/// 当 API 返回 RFC 7807 Problem Details 格式的错误响应时抛出。
/// 继承自 <see cref="ApiException"/>，保持现有异常处理兼容性。
/// </remarks>
public class ValidationApiException : ApiException
{
    /// <summary>
    /// 获取 Problem Details 错误详情。
    /// </summary>
    public ProblemDetails? ProblemDetails { get; }

    /// <summary>
    /// 初始化 <see cref="ValidationApiException"/> 实例。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="problemDetails">Problem Details 错误详情。</param>
    /// <param name="content">原始响应内容。</param>
    /// <param name="requestUri">请求 URI。</param>
    public ValidationApiException(HttpStatusCode statusCode, ProblemDetails? problemDetails,
        string? content, string? requestUri) : base(statusCode, content, requestUri)
    {
        ProblemDetails = problemDetails;
    }
}
