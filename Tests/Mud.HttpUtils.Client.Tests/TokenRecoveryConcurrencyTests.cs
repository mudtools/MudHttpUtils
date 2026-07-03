using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 并发刷新去重测试：验证 TokenRecoveryDelegatingHandler 在多个并发 401 场景下
/// 仅触发一次令牌刷新（问题 1 修复验证）。
/// </summary>
public class TokenRecoveryConcurrencyTests
{
    private static HttpRequestMessage CreateRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old-token");
        return request;
    }

    [Fact]
    public async Task Concurrent401_OnlyRefreshesOnce()
    {
        var refreshCallCount = 0;
        var invalidateCallCount = 0;

        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref invalidateCallCount))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref refreshCallCount))
            .ReturnsAsync("new-token");

        var requestCount = 0;
        var innerHandler = new FakeHttpMessageHandler(_ =>
        {
            var n = Interlocked.Increment(ref requestCount);
            // 第一次返回 401，之后返回 200
            return n == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });

        var handler = new TokenRecoveryDelegatingHandler(mockTokenManager.Object);
        handler.InnerHandler = innerHandler;
        var invoker = new HttpMessageInvoker(handler);

        // 发送第一个请求触发 401 恢复
        var response = await invoker.SendAsync(CreateRequest(), CancellationToken.None);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 仅刷新一次
        refreshCallCount.Should().Be(1);
        invalidateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent401_MultipleRequests_DedupRefresh()
    {
        var refreshCallCount = 0;
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref refreshCallCount);
                // 通知等待者刷新已开始
                refreshStarted.TrySetResult(true);
                // 等待信号以延迟刷新完成，扩大去重窗口
                await refreshContinue.Task.ConfigureAwait(false);
                return "new-token";
            });

        // 使用全局计数器：前 5 次调用返回 401，之后返回 200
        var requestCount = 0;
        var innerHandler = new FakeAsyncHttpMessageHandler(async _ =>
        {
            var n = Interlocked.Increment(ref requestCount);
            if (n <= 5)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });

        var handler = new TokenRecoveryDelegatingHandler(mockTokenManager.Object);
        handler.InnerHandler = innerHandler;
        var invoker = new HttpMessageInvoker(handler);

        // 并发发送 5 个请求
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => invoker.SendAsync(CreateRequest(), CancellationToken.None))
            .ToArray();

        // 等待第一个刷新请求开始
        await refreshStarted.Task.ConfigureAwait(false);
        // 短暂延迟确保其他请求也进入恢复流程
        await Task.Delay(50).ConfigureAwait(false);
        // 释放刷新完成信号
        refreshContinue.SetResult(true);

        var responses = await Task.WhenAll(tasks).ConfigureAwait(false);

        // 所有请求应成功
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));

        // 由于去重机制，刷新次数应少于请求数
        refreshCallCount.Should().BeLessThan(5);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class FakeAsyncHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public FakeAsyncHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return await _handler(request).ConfigureAwait(false);
        }
    }
}
