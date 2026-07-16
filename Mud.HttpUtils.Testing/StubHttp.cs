// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Mud.HttpUtils.Testing;

/// <summary>
/// Mock HTTP 服务器，基于 <see cref="HttpMessageHandler"/> 实现请求拦截与响应配置。
/// </summary>
/// <remarks>
/// 用于单元测试中模拟 HTTP 服务器响应，无需真实网络请求。
/// </remarks>
public sealed class StubHttp : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, List<StubResponse>> _routes = new();
    private readonly List<StubResponse> _catchAll = new();

    /// <summary>
    /// 配置指定路由的响应。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">路由路径（如 <c>/api/users/{id}</c>）。</param>
    /// <param name="statusCode">响应状态码。</param>
    /// <param name="content">响应内容。</param>
    /// <param name="contentType">Content-Type。</param>
    /// <returns>配置的 <see cref="StubResponse"/> 实例，可链式配置头等。</returns>
    public StubResponse Respond(HttpMethod method, string path,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? content = null,
        string contentType = "application/json")
    {
        var response = new StubResponse(method, path, statusCode, content, contentType);
        var key = RouteMatcher.BuildKey(method, path);
        _routes.AddOrUpdate(key, [response], (_, list) => { list.Add(response); return list; });
        return response;
    }

    /// <summary>
    /// 配置捕获所有未匹配请求的默认响应。
    /// </summary>
    public StubResponse RespondToAnyRequest(HttpStatusCode statusCode = HttpStatusCode.OK,
        string? content = null,
        string contentType = "application/json")
    {
        var response = new StubResponse(null, "*", statusCode, content, contentType);
        _catchAll.Add(response);
        return response;
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 查找匹配的路由
        var key = RouteMatcher.BuildKey(request.Method, request.RequestUri?.AbsolutePath ?? "/");
        StubResponse? matched = null;

        if (_routes.TryGetValue(key, out var responses))
        {
            // 取第一个未过期或未达到调用次数限制的响应
            matched = responses.FirstOrDefault(r => r.IsValid);
            if (matched != null)
            {
                matched.IncrementCallCount();
            }
        }

        // Fallback 到 catch-all
        matched ??= _catchAll.FirstOrDefault(r => r.IsValid);

        if (matched == null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No stub response configured for {request.Method} {request.RequestUri?.AbsolutePath}")
            };
        }

        // 模拟网络延迟（如已配置）
        if (matched.DelayMs > 0)
        {
            await Task.Delay(matched.DelayMs, cancellationToken).ConfigureAwait(false);
        }

        var responseMessage = new HttpResponseMessage(matched.StatusCode);

        if (!string.IsNullOrEmpty(matched.Content))
        {
            responseMessage.Content = new StringContent(matched.Content, System.Text.Encoding.UTF8, matched.ContentType);
        }

        // 添加配置的头
        foreach (var header in matched.Headers)
        {
            responseMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return responseMessage;
    }

    /// <summary>
    /// 清除所有已配置的路由。
    /// </summary>
    public void Clear()
    {
        _routes.Clear();
        _catchAll.Clear();
    }
}
