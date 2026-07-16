// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 请求执行器接口，统一处理响应反序列化、错误处理和执行模式编排。
/// 由源生成器生成的代码调用。
/// </summary>
/// <remarks>
/// 该接口是无状态的：<see cref="IBaseHttpClient"/> 实例通过方法参数逐次传递，
/// 而非保存在执行器内部。这使得执行器可安全注册为 Singleton，
/// 在多应用/多租户场景下不存在 TOCTOU 竞态风险。
/// </remarks>
public interface IHttpRequestExecutor
{
    /// <summary>
    /// 发送 HTTP 请求并反序列化响应。
    /// </summary>
    /// <typeparam name="TResult">反序列化目标类型。</typeparam>
    /// <param name="request">已构建完成的 HttpRequestMessage（含URL/Header/Body）。</param>
    /// <param name="httpClient">用于发送请求的 HTTP 客户端实例。</param>
    /// <param name="descriptor">响应处理描述符。</param>
    /// <param name="jsonSerializerOptions">JSON序列化选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化后的结果。</returns>
    Task<TResult?> SendAndDeserializeAsync<TResult>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送 HTTP 请求并返回 Response{T} 包装结果。
    /// </summary>
    Task<Response<TInner>> SendAsResponseAsync<TInner>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ResponseDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送 HTTP 请求（无返回值）。
    /// </summary>
    Task SendAsync(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ResponseDescriptor descriptor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载文件并返回字节数组。
    /// 支持基于 <paramref name="descriptor"/> 的错误处理（非 2xx 状态码抛出 <see cref="ApiException"/>）。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="httpClient">用于发送请求的 HTTP 客户端实例。</param>
    /// <param name="descriptor">响应处理描述符（用于 AllowAnyStatusCode 控制）。可为 null，null 时默认检查状态码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<byte[]?> DownloadAsync(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ResponseDescriptor? descriptor = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载大文件到指定路径。
    /// 支持基于 <paramref name="descriptor"/> 的错误处理（非 2xx 状态码抛出 <see cref="ApiException"/>）。
    /// 支持通过 <paramref name="progress"/> 报告下载进度（累计已接收字节数）。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="httpClient">用于发送请求的 HTTP 客户端实例。</param>
    /// <param name="filePath">文件保存路径。</param>
    /// <param name="overwrite">是否覆盖已存在的文件。默认为 true。</param>
    /// <param name="bufferSize">下载缓冲区大小（字节）。默认为 81920（80KB）。</param>
    /// <param name="descriptor">响应处理描述符（用于 AllowAnyStatusCode 控制）。可为 null，null 时默认检查状态码。</param>
    /// <param name="progress">下载进度回调（报告累计已写入字节数）。可为 null，表示不需要进度报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DownloadLargeAsync(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        string filePath,
        bool overwrite = true,
        int bufferSize = 81920,
        ResponseDescriptor? descriptor = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以异步枚举方式流式读取响应。
    /// </summary>
    /// <typeparam name="TElement">流式元素类型。</typeparam>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="httpClient">用于发送请求的 HTTP 客户端实例。</param>
    /// <param name="jsonSerializerOptions">JSON 序列化选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流式返回的异步枚举。</returns>
    /// <remarks>
    /// <b>Native AOT 注意</b>：此重载使用开放泛型 JSON 反序列化，AOT 场景下须确保 <typeparamref name="TElement"/> 已在
    /// <c>JsonSerializerContext</c> 中声明。推荐使用 AOT 安全重载。
    /// </remarks>
    IAsyncEnumerable<TElement> SendAsAsyncEnumerable<TElement>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

#if NET8_0_OR_GREATER
    /// <summary>
    /// 以异步枚举方式流式读取响应（Native AOT 安全重载）。
    /// </summary>
    /// <typeparam name="TElement">流式元素类型，必须在 <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> 中声明。</typeparam>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="httpClient">用于发送请求的 HTTP 客户端实例。</param>
    /// <param name="jsonTypeInfo">来自 <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> 的类型信息（AOT 安全）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流式返回的异步枚举。</returns>
    IAsyncEnumerable<TElement> SendAsAsyncEnumerable<TElement>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TElement> jsonTypeInfo,
        CancellationToken cancellationToken = default);
#endif

    /// <summary>
    /// 发送 HTTP 请求，支持缓存和弹性策略编排。
    /// </summary>
    /// <typeparam name="TResult">反序列化目标类型。</typeparam>
    /// <param name="request">已构建完成的 HttpRequestMessage。</param>
    /// <param name="httpClient">用于发送请求的 HTTP 客户端实例。</param>
    /// <param name="descriptor">执行模式描述符（含响应处理、缓存、弹性策略配置）。</param>
    /// <param name="jsonSerializerOptions">JSON序列化选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化后的结果。</returns>
    Task<TResult?> ExecuteAsync<TResult>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送 HTTP 请求并返回 Response{T}，支持缓存和弹性策略编排。
    /// </summary>
    Task<Response<TInner>?> ExecuteAsResponseAsync<TInner>(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ExecutionDescriptor descriptor,
        object? jsonSerializerOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送 HTTP 请求（无返回值），支持缓存和弹性策略编排。
    /// </summary>
    Task ExecuteAsync(
        HttpRequestMessage request,
        IBaseHttpClient httpClient,
        ExecutionDescriptor descriptor,
        CancellationToken cancellationToken = default);
}
