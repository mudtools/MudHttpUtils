// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P0-01（架构不变式 I2）：令牌端点客户端也挂载恢复处理器时，刷新链路不得重入并永久挂起。
/// </summary>
/// <remarks>
/// 缺陷路径（B1）：刷新经由的 HTTP 请求若被恢复处理器拦截，令牌端点的 401 会重新进入
/// <see cref="TokenRecoveryExecutor"/>，命中同一去重键的"在途刷新"条目并 <c>await</c> 自身 → 永久挂起。
/// 本用例以"业务请求与令牌端点请求共享同一恢复执行器"精确复现该循环，并断言：
/// ① 3 秒内返回（不死锁）；② 刷新只发生一次（未产生二次刷新风暴）；③ 返回服务端真实 401。
/// </remarks>
public class TokenRecoveryRefreshReentrancyTests
{
    private const string TokenEndpointHost = "idp.example.com";

    private static HttpRequestMessage CreateBusinessRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/resource");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale-token");
        return request;
    }

    [Fact]
    public async Task RecoveryDuringTokenRefresh_ShouldNotDeadlock_WhenHandlerAlsoOnTokenEndpoint()
    {
        var refreshCallCount = 0;
        var tokenEndpointCallCount = 0;
        var businessCallCount = 0;

        HttpMessageInvoker? sharedInvoker = null;

        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref refreshCallCount);

                // 关键：刷新经由"同样挂载了恢复处理器"的令牌端点客户端发出。
                // 修复前该请求会重新进入恢复流程、命中同一去重键的在途条目并 await 自身 → 永久挂起。
                using var tokenRequest = new HttpRequestMessage(
                    HttpMethod.Post, $"https://{TokenEndpointHost}/oauth2/token");
                using var tokenResponse = await sharedInvoker!.SendAsync(tokenRequest, CancellationToken.None);
                tokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

                return "fresh-token";
            });

        // 业务客户端与令牌端点客户端共享同一处理器实例 ⇒ 共享同一 TokenRecoveryExecutor 与同一去重表。
        sharedInvoker = new HttpMessageInvoker(new TokenRecoveryDelegatingHandler(mockTokenManager.Object)
        {
            InnerHandler = new CountingUnauthorizedHandler(request =>
            {
                if (string.Equals(request.RequestUri?.Host, TokenEndpointHost, StringComparison.OrdinalIgnoreCase))
                    Interlocked.Increment(ref tokenEndpointCallCount);
                else
                    Interlocked.Increment(ref businessCallCount);
            }),
        });

        var sendTask = sharedInvoker.SendAsync(CreateBusinessRequest(), CancellationToken.None);

        var completed = await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(3)));
        completed.Should().BeSameAs(sendTask,
            "令牌端点与业务端点共享恢复执行器时，I2 熔断必须使恢复流程在 3 秒内返回（修复前永久挂起）");

        var response = await sendTask;
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "恢复耗尽后必须返回服务端真实响应，不得伪造成功");

        refreshCallCount.Should().Be(1, "熔断只跳过被拦截的请求，不得引发二次刷新");
        tokenEndpointCallCount.Should().Be(1, "令牌端点请求应被实际发出一次（而非被熔断吞掉）");
        businessCallCount.Should().Be(2, "业务请求应为：首次发送 + 1 次恢复重试");
    }

    private sealed class CountingUnauthorizedHandler : HttpMessageHandler
    {
        private readonly Action<HttpRequestMessage> _onSend;

        public CountingUnauthorizedHandler(Action<HttpRequestMessage> onSend) => _onSend = onSend;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _onSend(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }
}
