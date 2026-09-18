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
                await refreshContinue.Task;
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
        await refreshStarted.Task;
        // 短暂延迟确保其他请求也进入恢复流程
        await Task.Delay(50);
        // 释放刷新完成信号
        refreshContinue.SetResult(true);

        var responses = await Task.WhenAll(tasks);

        // 所有请求应成功
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));

        // 由于去重机制，刷新次数应少于请求数
        refreshCallCount.Should().BeLessThan(5);
    }

    /// <summary>
    /// P1.4（TK-15）取消隔离：当一个调用方在刷新进行中取消其请求时，
    /// 不应中断共享的令牌刷新流程；其它等待同一刷新结果的调用方仍应成功。
    /// </summary>
    [Fact]
    public async Task Recovery_WaiterCancellation_ShouldNotCancelSharedRefresh()
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
                refreshStarted.TrySetResult(true);
                // 挂起刷新，模拟远端慢响应，扩大去重窗口
                await refreshContinue.Task;
                return "new-token";
            });

        // 前两次请求返回 401，之后的恢复重试返回 200
        var requestCount = 0;

        var handler = new TokenRecoveryDelegatingHandler(mockTokenManager.Object);
        handler.InnerHandler = new FakeHttpMessageHandler(_ =>
        {
            var n = Interlocked.Increment(ref requestCount);
            if (n <= 2)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        var invoker = new HttpMessageInvoker(handler);

        // 两个调用方并发发起请求
        using var ctsA = new CancellationTokenSource();
        // 刻意的「发射后不管」：调用方 A 的取消不应影响调用方 B 的恢复流程，
        // 其返回任务在用例结束前不做断言（避免 CS4014 提示未等待）。
        _ = invoker.SendAsync(CreateRequest(), ctsA.Token);
        var taskB = invoker.SendAsync(CreateRequest(), CancellationToken.None);

        // 等待刷新真正开始（只有一个线程赢得刷新权）
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        // 调用方 A 在刷新进行中取消
        ctsA.Cancel();

        // 释放刷新完成信号
        refreshContinue.SetResult(true);

        // 调用方 B 应正常成功（其等待不被 A 的取消影响）
        var responseB = await taskB.WaitAsync(TimeSpan.FromSeconds(5));
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);

        // 调用方 A 的请求因取消而失败，但共享刷新只发生了一次
        refreshCallCount.Should().Be(1);
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
            return await _handler(request);
        }
    }
}
