namespace Mud.HttpUtils;

/// <summary>
/// HTTP 响应拦截器接口，用于在响应接收后执行自定义逻辑。
/// </summary>
public interface IHttpResponseInterceptor
{
    /// <summary>
    /// 拦截器执行顺序，数值越小越先执行。
    /// </summary>
    int Order { get; }

    /// <summary>
    /// 在响应接收后执行。
    /// </summary>
    /// <param name="response">HTTP 响应消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task OnResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default);
}
