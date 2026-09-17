// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http;

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 状态码异常工厂钩子（第一级）。
/// </summary>
/// <remarks>
/// <para>
/// 返回 <c>null</c> 表示不抛异常（类似 <see cref="AllowAnyStatusCodeAttribute"/> 的效果）。
/// </para>
/// <para>
/// 默认实现抛出 <see cref="ApiException"/>（含状态码、内容、URI），确保向后兼容。
/// 自定义实现可返回不同的异常类型或 <c>null</c> 以抑制异常。
/// </para>
/// </remarks>
public interface IHttpExceptionFactory
{
    /// <summary>
    /// 根据 HTTP 响应创建异常（或返回 null 表示不抛异常）。
    /// </summary>
    /// <param name="response">HTTP 响应消息。</param>
    /// <param name="content">响应内容（已读取为字符串，可能为 null）。</param>
    /// <param name="requestUri">请求 URI。</param>
    /// <returns>要抛出的异常，或 <c>null</c> 表示不抛异常。</returns>
    Exception? CreateApiException(HttpResponseMessage response, string? content, string? requestUri);
}

/// <summary>
/// 反序列化失败异常工厂钩子（第二级）。
/// </summary>
/// <remarks>
/// <para>
/// 默认实现抛出 <see cref="ApiException"/>（含内容、反序列化异常信息）。
/// 自定义实现可返回不同的异常类型或添加重试逻辑。
/// </para>
/// </remarks>
public interface IDeserializationExceptionFactory
{
    /// <summary>
    /// 根据反序列化失败上下文创建异常。
    /// </summary>
    /// <param name="content">响应内容（已读取为字符串）。</param>
    /// <param name="targetType">反序列化目标类型。</param>
    /// <param name="deserializationException">原始反序列化异常。</param>
    /// <param name="requestUri">请求 URI。</param>
    /// <returns>要抛出的异常。</returns>
    Exception CreateDeserializationException(string? content, Type targetType, Exception deserializationException, string? requestUri);
}

/// <summary>
/// 运输层异常工厂钩子（第三级）。
/// </summary>
/// <remarks>
/// <para>
/// 默认实现抛出 <see cref="ApiRequestException"/>（含 <see cref="ApiRequestException.IsTimeout"/>/
/// <see cref="ApiRequestException.IsCancellation"/>/<see cref="ApiRequestException.TransportException"/> 扩展属性），确保向后兼容。
/// 自定义实现可返回不同的异常类型。
/// </para>
/// </remarks>
public interface ITransportExceptionFactory
{
    /// <summary>
    /// 根据运输层失败上下文创建异常。
    /// </summary>
    /// <param name="transportException">原始运输层异常（<see cref="HttpRequestException"/> / <see cref="OperationCanceledException"/> 等）。</param>
    /// <param name="requestUri">请求 URI。</param>
    /// <param name="isTimeout">是否为超时导致。</param>
    /// <param name="isCancellation">是否为取消导致。</param>
    /// <returns>要抛出的异常。</returns>
    Exception CreateTransportException(Exception transportException, string? requestUri, bool isTimeout, bool isCancellation);
}

/// <summary>
/// 异常擦除钩子：在异常传播前清除敏感数据（Authorization 头、请求体、Set-Cookie 等）。
/// </summary>
/// <remarks>
/// <para>
/// 使用时通过 <c>options.ExceptionRedactor</c> 或 DI 注入。默认无注入时不执行擦除。
/// 典型用法：<c>ex.Content = null; ex.RequestContent = null;</c>
/// </para>
/// </remarks>
public interface IExceptionRedactor
{
    /// <summary>
    /// 擦除异常中的敏感数据。
    /// </summary>
    /// <param name="exception">要擦除的异常。</param>
    void Redact(ApiException exception);
}

/// <summary>
/// 三级异常工厂的默认实现集合。复刻现有行为，确保向后兼容。
/// </summary>
/// <remarks>
/// 通过 DI 注入自定义工厂可覆盖任一级别；默认无注入时行为与改造前完全一致。
/// </remarks>
public sealed class DefaultExceptionFactory : IHttpExceptionFactory, IDeserializationExceptionFactory, ITransportExceptionFactory
{
    /// <inheritdoc />
    public Exception? CreateApiException(HttpResponseMessage response, string? content, string? requestUri)
    {
        return new ApiException(response.StatusCode, content, requestUri);
    }

    /// <inheritdoc />
    public Exception CreateDeserializationException(string? content, Type targetType, Exception deserializationException, string? requestUri)
    {
        return new ApiException(HttpStatusCode.OK, content, requestUri, deserializationException);
    }

    /// <inheritdoc />
    public Exception CreateTransportException(Exception transportException, string? requestUri, bool isTimeout, bool isCancellation)
    {
        return new ApiRequestException(
            transportException.Message,
            transportException,
            isTimeout,
            isCancellation,
            requestUri);
    }
}
