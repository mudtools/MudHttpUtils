// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;

namespace Mud.HttpUtils;

/// <summary>
/// 运输层异常。表示请求未成功发出或运输层失败（网络 / DNS / 超时 / 取消）。
/// 与 <see cref="ApiException"/>（HTTP 错误响应）区分。
/// </summary>
/// <remarks>
/// v1.5 修正：<see cref="ApiRequestException"/> 继承 <see cref="ApiException"/>（而非独立的
/// <c>ApiExceptionBase</c>），因此现有 <c>catch (ApiException)</c> 调用方**仍能捕获**运输层异常，
/// 无需大规模迁移；同时保留 <see cref="IsTimeout"/> / <see cref="IsCancellation"/> / <see cref="TransportException"/>
/// 扩展信息以区分超时 / 取消 / 网络失败。运输层失败无 HTTP 状态码，故基类 <see cref="ApiException.StatusCode"/>
/// 取默认值 0（与 <see cref="HttpRequestException"/> 行为一致）。
/// </remarks>
public class ApiRequestException : ApiException
{
    /// <summary>
    /// 原始运输层异常（<see cref="HttpRequestException"/> / <see cref="OperationCanceledException"/> 等）。
    /// </summary>
    public Exception? TransportException { get; }

    /// <summary>
    /// 是否为超时导致。
    /// </summary>
    public bool IsTimeout { get; }

    /// <summary>
    /// 是否为取消导致。
    /// </summary>
    public bool IsCancellation { get; }

    /// <summary>
    /// 初始化 <see cref="ApiRequestException"/> 类的新实例。
    /// </summary>
    /// <param name="message">异常消息。</param>
    /// <param name="transportException">原始运输层异常。</param>
    /// <param name="isTimeout">是否为超时。</param>
    /// <param name="isCancellation">是否为取消。</param>
    /// <param name="requestUri">请求 URI。</param>
    public ApiRequestException(
        string message,
        Exception? transportException = null,
        bool isTimeout = false,
        bool isCancellation = false,
        string? requestUri = null)
        : base(message, requestUri, transportException)
    {
        TransportException = transportException;
        IsTimeout = isTimeout;
        IsCancellation = isCancellation;
    }
}
