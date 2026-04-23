namespace Mud.HttpUtils;

/// <summary>
/// JSON HTTP 客户端接口，提供基于 JSON 数据格式的 HTTP 请求方法。
/// </summary>
public interface IJsonHttpClient : IBaseHttpClient
{
    /// <summary>
    /// 异步发送 GET 请求并返回 JSON 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> GetAsync<TResult>(string requestUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 POST 请求，将请求数据序列化为 JSON 并返回 JSON 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="requestData">要发送的请求数据。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> PostAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 PUT 请求，将请求数据序列化为 JSON 并返回 JSON 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="requestData">要发送的请求数据。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> PutAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 DELETE 请求并返回 JSON 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> DeleteAsJsonAsync<TResult>(string requestUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送带请求体的 DELETE 请求，将请求数据序列化为 JSON 并返回 JSON 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="requestData">要发送的请求数据。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> DeleteAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 PATCH 请求，将请求数据序列化为 JSON 并返回 JSON 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="requestData">要发送的请求数据。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> PatchAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default);
}
