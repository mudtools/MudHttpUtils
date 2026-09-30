// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P0-04（架构不变式 I1 / 缺陷 B11）：恢复去重键必须单射 ——
/// <c>managerKey</c> / <c>userId</c> 为中间层可灌入的不可信输入，内嵌分隔符不得造成跨管理器键碰撞。
/// </summary>
/// <remarks>
/// 原实现按键 <c>appKey + U+001F + managerKey + [U+001F + userId] + U+001F + scopeKey</c> 拼接，于是：
/// <list type="bullet">
/// <item><description>A：<c>managerKey = "mgrB\u001Fa"</c> + <c>scopes = ["b"]</c> ⇒ <c>_no_tenant␟mgrB␟a␟b</c></description></item>
/// <item><description>B：<c>managerKey = "mgrB"</c> + <c>scopes = ["a","b"]</c> ⇒ <c>_no_tenant␟mgrB␟a␟b</c>（<b>碰撞</b>）</description></item>
/// </list>
/// 碰撞后 B 会复用 A 的在途刷新结果 —— 直接拿到他人令牌字符串。修复后两者必须各自刷新一次。
/// </remarks>
public class RecoveryDedupKeyInjectionTests
{
    private const string InjectedManagerKey = "mgrB\u001Fa";

    private static HttpRequestMessage CreateRequest(string managerKey, string[] scopes)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/resource");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale-token");
        request.Options.Set(
            new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey),
            new TokenRecoveryContext { TokenManagerKey = managerKey, Scopes = scopes });
        return request;
    }

    [Fact]
    public async Task ManagerKeyInjection_ShouldNotAliasAnotherManagerScopes()
    {
        var refreshCallCount = 0;
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .Returns(() => RefreshAsync());
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(() => RefreshAsync());

        async Task<string> RefreshAsync()
        {
            Interlocked.Increment(ref refreshCallCount);
            refreshStarted.TrySetResult(true);
            // 挂起首次刷新，扩大去重窗口 —— 若键碰撞，第二个请求会复用同一在途任务。
            await refreshGate.Task.ConfigureAwait(false);
            return "access-token";
        }

        var handler = new TokenRecoveryDelegatingHandler(mockTokenManager.Object)
        {
            InnerHandler = new AlwaysUnauthorizedHandler(),
        };
        var invoker = new HttpMessageInvoker(handler);

        // A：managerKey 内嵌分隔符 + 单 scope
        var taskA = invoker.SendAsync(CreateRequest(InjectedManagerKey, new[] { "b" }), CancellationToken.None);

        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        // B：另一管理器的多 scope 组合，其"裸拼接键"与 A 相同
        var taskB = invoker.SendAsync(CreateRequest("mgrB", new[] { "a", "b" }), CancellationToken.None);

        await Task.Delay(200);

        refreshCallCount.Should().Be(2,
            "两个语义不同的管理器 / 作用域组合必须各自刷新（修复前因键碰撞只会刷新一次并互相返回对方令牌）");

        refreshGate.SetResult(true);

        (await taskA.WaitAsync(TimeSpan.FromSeconds(5))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await taskB.WaitAsync(TimeSpan.FromSeconds(5))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SameManagerAndScopes_ShouldStillDedupToSingleRefresh()
    {
        // 回归保护：改为长度前缀编码后，语义相同的键必须仍然去重（否则会引入刷新风暴）。
        var refreshCallCount = 0;
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref refreshCallCount);
                refreshStarted.TrySetResult(true);
                await refreshGate.Task.ConfigureAwait(false);
                return "access-token";
            });

        var handler = new TokenRecoveryDelegatingHandler(mockTokenManager.Object)
        {
            InnerHandler = new AlwaysUnauthorizedHandler(),
        };
        var invoker = new HttpMessageInvoker(handler);

        var taskA = invoker.SendAsync(CreateRequest("mgr", new[] { "b" }), CancellationToken.None);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        var taskB = invoker.SendAsync(CreateRequest("mgr", new[] { "b" }), CancellationToken.None);
        await Task.Delay(200);

        refreshCallCount.Should().Be(1, "同管理器 + 同作用域的并发 401 必须继续去重");

        refreshGate.SetResult(true);
        await taskA.WaitAsync(TimeSpan.FromSeconds(5));
        await taskB.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class AlwaysUnauthorizedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }
}
