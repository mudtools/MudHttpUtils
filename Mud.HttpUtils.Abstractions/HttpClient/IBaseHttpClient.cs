namespace Mud.HttpUtils;

/// <summary>
/// 基础 HTTP 客户端接口，定义了 HTTP 请求的基本操作方法。
/// </summary>
public interface IBaseHttpClient
{
    /// <summary>
    /// 异步发送 HTTP 请求并返回反序列化后的结果。
    /// </summary>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="jsonSerializerOptions">JSON 序列化选项，用于控制反序列化行为。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> SendAsync<TResult>(HttpRequestMessage request, object? jsonSerializerOptions = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 HTTP 请求并返回原始的 <see cref="HttpResponseMessage"/>。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>原始 HTTP 响应消息任务。</returns>
    Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 HTTP 请求并返回响应流。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含响应流的任务。</returns>
    Task<Stream> SendStreamAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步下载数据并返回字节数组。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含下载数据的字节数组任务。</returns>
    Task<byte[]?> DownloadAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步下载大型文件到指定路径。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="filePath">文件保存的目标路径。</param>
    /// <param name="overwrite">如果目标文件已存在，是否覆盖。默认为 true。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>下载的文件信息任务。</returns>
    Task<FileInfo> DownloadLargeAsync(HttpRequestMessage request, string filePath, bool overwrite = true, CancellationToken cancellationToken = default);
}
