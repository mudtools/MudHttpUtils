// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Threading;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// L-1：401 恢复链路的租户绑定守卫。
/// </summary>
/// <remarks>
/// <para>
/// 修复前 <c>BindTenantGuard</c> 只在 <c>DefaultTokenProvider.GetTokenAsync</c>（令牌获取）中调用，
/// 恢复链路（<c>TokenRecoveryExecutor.ResolveManager</c> / <c>ResolveUserManager</c>）拿到的管理器
/// <b>不受守卫约束</b>。
/// </para>
/// <para>
/// 危害场景：宿主使用<b>扁平</b>的 <see cref="ITokenManagerRegistry"/> 且两个应用注册了同名 key 时，
/// 应用 A 的 401 可能解析到应用 B 的令牌管理器并对其执行失效 + 刷新 —— 凭据错配 / 跨租户越权。
/// 守卫在此 fail-closed（抛异常 → 恢复按失败处理 → 返回真实 401）。
/// </para>
/// </remarks>
public class TokenRecoveryTenantGuardTests
{
    private sealed class FakeAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;
    }

    private sealed class FakeAppContextHolder : IAppContextHolder
    {
        public FakeAppContextHolder(string? appKey)
            => Current = appKey == null ? null : new FakeAppContext(appKey);

        public IMudAppContext? Current { get; init; }

        public void SwitchTo(IMudAppContext? context) => throw new NotSupportedException();

        public IDisposable BeginScope(IMudAppContext context) => throw new NotSupportedException();
    }

    /// <summary>可观测「被哪个租户绑定过」的测试用租户令牌管理器。</summary>
    private sealed class TenantBoundManager : TokenManagerBase
    {
        public int RefreshCount { get; private set; }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.FromResult(new CredentialToken
            {
                AccessToken = "tenant-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });
        }
    }

    private static TokenRecoveryContext ContextWithKey(string key)
        => new() { TokenManagerKey = key };

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> UnauthorizedSender()
        => (req, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, req.RequestUri!),
        });

    private static HttpRequestMessage RequestWithRecoveryContext(string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/x");
        request.Options.Set(
            new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey),
            ContextWithKey(key));
        return request;
    }

    [Fact]
    public async Task Recovery_SameTenant_ShouldNotBeBlocked()
    {
        var manager = new TenantBoundManager();
        var registry = new DelegateTokenManagerRegistry(_ => manager);
        var executor = new TokenRecoveryExecutor(
            manager,
            new TokenRecoveryOptions { RecoveryMaxRetries = 1 },
            NullLogger.Instance,
            new FakeAppContextHolder("app-a"));

        // 同租户（守卫首次绑定即成功），不应因守卫而中断恢复流程。
        var response = await executor.ExecuteAsync(
            RequestWithRecoveryContext("k"), UnauthorizedSender(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        manager.RefreshCount.Should().BeGreaterThan(0, "同租户下恢复流程应正常执行刷新");
    }

    [Fact]
    public async Task Recovery_CrossTenantManager_ShouldBeRejectedAndNotRefresh()
    {
        // 管理器已被 app-a 绑定
        var manager = new TenantBoundManager();
        manager.BindTenantGuard("app-a");

        var registry = new DelegateTokenManagerRegistry(_ => manager);
        var executor = new TokenRecoveryExecutor(
            manager,
            new TokenRecoveryOptions { RecoveryMaxRetries = 1 },
            NullLogger.Instance,
            new FakeAppContextHolder("app-b"));   // 当前请求属于 app-b

        var response = await executor.ExecuteAsync(
            RequestWithRecoveryContext("k"), UnauthorizedSender(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "L-1：跨租户管理器必须 fail-closed —— 恢复流程按失败处理并返回真实 401");
        manager.RefreshCount.Should().Be(0,
            "L-1：不得对已绑定到其它租户的管理器执行任何刷新（修复前恢复路径完全绕过守卫）");
    }

    [Fact]
    public async Task Recovery_WithoutAppContextHolder_ShouldSkipGuard()
    {
        // 未注入 IAppContextHolder（无 DI / 第三方宿主自建执行器）：守卫跳过，行为与既有版本一致。
        var manager = new TenantBoundManager();
        var registry = new DelegateTokenManagerRegistry(_ => manager);
        var executor = new TokenRecoveryExecutor(
            manager,
            new TokenRecoveryOptions { RecoveryMaxRetries = 1 },
            NullLogger.Instance);
        // 即使管理器已被其它租户绑定，无上下文时也不应因守卫而额外失败
        manager.BindTenantGuard("app-a");

        var response = await executor.ExecuteAsync(
            RequestWithRecoveryContext("k"), UnauthorizedSender(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        manager.RefreshCount.Should().BeGreaterThan(0, "无应用上下文时守卫不介入（不引入新的误报）");
    }

    [Fact]
    public async Task Recovery_WithoutAppContext_ShouldSkipGuard()
    {
        var manager = new TenantBoundManager();
        var registry = new DelegateTokenManagerRegistry(_ => manager);
        var executor = new TokenRecoveryExecutor(
            manager,
            new TokenRecoveryOptions { RecoveryMaxRetries = 1 },
            NullLogger.Instance,
            new FakeAppContextHolder(null));   // 已注入但当前无上下文
        manager.BindTenantGuard("app-a");

        var response = await executor.ExecuteAsync(
            RequestWithRecoveryContext("k"), UnauthorizedSender(), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        manager.RefreshCount.Should().BeGreaterThan(0, "当前无应用上下文时守卫跳过");
    }

    [Fact]
    public async Task Recovery_CrossTenantManager_ShouldNotThrowToCaller()
    {
        // L-1：拒绝路径必须与其余恢复失败分支一致 —— 返回真实 401，而不是把内部守卫异常抛给调用方。
        var manager = new TenantBoundManager();
        manager.BindTenantGuard("app-a");

        var executor = new TokenRecoveryExecutor(
            manager,
            new TokenRecoveryOptions { RecoveryMaxRetries = 1 },
            NullLogger.Instance,
            new FakeAppContextHolder("app-b"));

        var act = async () => await executor.ExecuteAsync(
            RequestWithRecoveryContext("k"), UnauthorizedSender(), CancellationToken.None);

        var response = await act.Should().NotThrowAsync("D3：恢复失败一律返回真实 401，不向上抛内部异常");
        response.Subject.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
