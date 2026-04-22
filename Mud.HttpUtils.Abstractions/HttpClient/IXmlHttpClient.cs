namespace Mud.HttpUtils;

/// <summary>
/// XML HTTP 客户端接口，提供基于 XML 数据格式的 HTTP 请求方法。
/// </summary>
public interface IXmlHttpClient : IBaseHttpClient
{
    /// <summary>
    /// 异步发送 HTTP 请求并返回 XML 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="encoding">XML 内容的编码方式，默认为 null。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> SendXmlAsync<TResult>(HttpRequestMessage request, Encoding? encoding = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 POST 请求，将请求数据序列化为 XML 并返回 XML 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="requestData">要发送的请求数据。</param>
    /// <param name="encoding">XML 内容的编码方式，默认为 null。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> PostAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 PUT 请求，将请求数据序列化为 XML 并返回 XML 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="requestData">要发送的请求数据。</param>
    /// <param name="encoding">XML 内容的编码方式，默认为 null。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> PutAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 GET 请求并返回 XML 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="encoding">XML 内容的编码方式，默认为 null。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> GetXmlAsync<TResult>(string requestUri, Encoding? encoding = null, CancellationToken cancellationToken = default);
}
