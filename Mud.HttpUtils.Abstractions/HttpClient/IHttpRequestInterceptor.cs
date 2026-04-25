namespace Mud.HttpUtils;

/// <summary>
/// HTTP 请求拦截器接口，用于在请求发送前执行自定义逻辑。
/// </summary>
public interface IHttpRequestInterceptor
{
    /// <summary>
    /// 拦截器执行顺序，数值越小越先执行。
    /// </summary>
    int Order { get; }

    /// <summary>
    /// 在请求发送前执行。
    /// </summary>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task OnRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
