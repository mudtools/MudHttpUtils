// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷与责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// F-7（G-7/TK-1）<c>TokenManagerBase</c> 级联失效钩子专项测试：
/// ① <c>TokenInvalidated</c> 事件恒触发（不受级联开关影响）且订阅者异常隔离；
/// ② <c>EnableTokenDependencyCascade</c> 独立开关<b>默认关闭</b>——零传播；
/// ③ 开关打开后失效沿依赖图传播到依赖者管理器（<c>42001 → suite_access_token → corp 重取</c> 语义）；
/// ④ 互依环（A↔B）有限终止、不无限递归；解析不到管理器 fail-safe 跳过。
/// </summary>
public class TokenDependencyCascadeTests
{
    /// <summary>
    /// 级联测试桩：可配置令牌类型 / 依赖图 / 级联开关 / 管理器解析器，
    /// 并计数 <c>InvalidateTokenAsync</c> 调用（级联为 fire-and-forget，计数供断言）。
    /// </summary>
    private sealed class CascadeProbeTokenManager : TokenManagerBase
    {
        private readonly string? _tokenType;
        private readonly TokenDependencyGraph _graph;
        private readonly bool _cascade;
        private readonly Func<string, ITokenManager?>? _resolver;
        private readonly string _accessToken;

        private int _invalidateCount;
        private int _refreshCount;

        /// <summary>失效调用计数（含级联触发）。</summary>
        public int InvalidateCount => Volatile.Read(ref _invalidateCount);

        /// <summary>刷新调用计数（缓存命中不应增加）。</summary>
        public int RefreshCount => Volatile.Read(ref _refreshCount);

        /// <summary>级联 fire-and-forget 的完成信号（首次失效时置位）。</summary>
        public TaskCompletionSource<bool> Invalidated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>记录每次失效收到的 scopes（断言级联沿同一归属传播）。</summary>
        public List<string[]?> ReceivedScopes { get; } = new();

        public CascadeProbeTokenManager(
            string? tokenType = null,
            TokenDependencyGraph? graph = null,
            bool cascade = false,
            Func<string, ITokenManager?>? resolver = null,
            string? accessToken = null)
        {
            _tokenType = tokenType;
            _graph = graph ?? TokenDependencyGraph.Empty;
            _cascade = cascade;
            _resolver = resolver;
            _accessToken = accessToken ?? $"token-{Guid.NewGuid():N}";
        }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _refreshCount);
            return Task.FromResult(new CredentialToken
            {
                AccessToken = _accessToken,
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
        }

        protected override string? TokenType => _tokenType;

        public override TokenDependencyGraph DependencyGraph => _graph;

        protected override bool EnableTokenDependencyCascade => _cascade;

        protected override ITokenManager? ResolveTokenManagerForType(string tokenType)
            => _resolver?.Invoke(tokenType);

        public override async Task<TokenResult> InvalidateTokenAsync(
            string[]? scopes = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invalidateCount);
            lock (ReceivedScopes) ReceivedScopes.Add(scopes);
            var result = await base.InvalidateTokenAsync(scopes, cancellationToken).ConfigureAwait(false);
            Invalidated.TrySetResult(true);
            return result;
        }
    }

    private static async Task ShouldCompleteAsync(Task task, string because)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        completed.Should().BeSameAs(task, because);
        await task.ConfigureAwait(false);
    }

    // ── TokenInvalidated 事件（恒触发，不受开关影响） ─────────────────

    [Fact]
    public async Task TokenInvalidated_ShouldFireWithPinnedArgs()
    {
        using var manager = new CascadeProbeTokenManager(
            tokenType: "corp_access_token", accessToken: "cached-token");
        TokenInvalidatedEventArgs? captured = null;
        manager.TokenInvalidated += (_, args) => captured = args;

        var scopes = new[] { "app-a" };
        await manager.GetTokenAsync(scopes, CancellationToken.None);   // 同一归属预热缓存，让 Removed 携带被删令牌
        var result = await manager.InvalidateTokenAsync(scopes, CancellationToken.None);

        captured.Should().NotBeNull("InvalidateTokenAsync 收尾必须触发 TokenInvalidated");
        captured!.TokenType.Should().Be("corp_access_token");
        captured.Scopes.Should().BeSameAs(scopes, "事件参数应携带失效作用域（同一引用）");
        captured.Removed.AccessToken.Should().Be("cached-token", "Removed 应是失效前缓存中的令牌");
        result.AccessToken.Should().Be("cached-token");
    }

    [Fact]
    public async Task TokenInvalidated_WhenCacheEmpty_ShouldCarryEmptyRemoved()
    {
        using var manager = new CascadeProbeTokenManager(tokenType: "corp_access_token");
        TokenInvalidatedEventArgs? captured = null;
        manager.TokenInvalidated += (_, args) => captured = args;

        await manager.InvalidateTokenAsync(null, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Removed.AccessToken.Should().BeNull("缓存未命中时 Removed 为 TokenResult.Empty");
    }

    [Fact]
    public async Task TokenInvalidated_ShouldFireEvenWhenCascadeDisabled()
    {
        // 开关只控制级联传播，事件恒触发（默认关 ≠ 无通知）。
        using var manager = new CascadeProbeTokenManager(cascade: false);
        var fired = false;
        manager.TokenInvalidated += (_, _) => fired = true;

        await manager.InvalidateTokenAsync(null, CancellationToken.None);

        fired.Should().BeTrue("级联开关默认关闭不影响事件触发");
    }

    [Fact]
    public async Task TokenInvalidated_SubscriberException_ShouldBeIsolated()
    {
        using var manager = new CascadeProbeTokenManager();
        var secondSubscriberCalled = false;
        manager.TokenInvalidated += (_, _) => throw new InvalidOperationException("订阅者故障");
        manager.TokenInvalidated += (_, _) => secondSubscriberCalled = true;

        var act = () => manager.InvalidateTokenAsync(null, CancellationToken.None);

        await act.Should().NotThrowAsync("订阅者异常必须隔离，不得阻断失效主流程");
        secondSubscriberCalled.Should().BeTrue("先发订阅者抛异常不得阻断后续订阅者");
    }

    // ── 级联开关（默认关） ───────────────────────────────────────────

    [Fact]
    public async Task CascadeDisabledByDefault_ShouldNotPropagate()
    {
        var corp = new CascadeProbeTokenManager(
            tokenType: "corp_access_token", accessToken: "corp-token");
        using var corpDisposable = corp;
        var registry = new Dictionary<string, ITokenManager> { ["corp_access_token"] = corp };
        using var suite = new CascadeProbeTokenManager(
            tokenType: "suite_access_token",
            graph: new TokenDependencyGraph().Add("corp_access_token", "suite_access_token"),
            cascade: false,
            resolver: t => registry.TryGetValue(t, out var m) ? m : null);

        await corp.GetTokenAsync(CancellationToken.None);   // corp 预热缓存
        await suite.InvalidateTokenAsync(null, CancellationToken.None);
        await Task.Delay(200);          // 给 fire-and-forget 留窗口

        corp.InvalidateCount.Should().Be(0, "级联开关默认关闭：零传播");
        var corpToken = await corp.GetTokenAsync(CancellationToken.None);
        corpToken.Should().Be("corp-token", "corp 缓存应完好（未被连带失效）");
        corp.RefreshCount.Should().Be(1, "缓存命中不应触发刷新");
    }

    [Fact]
    public async Task CascadeDisabledByDefault_ShouldMatchProtectedHookDefault()
    {
        // 默认值钉死：基类默认 false（独立开关，默认不启用——方案红线）。
        using var manager = new CascadeProbeTokenManager();

        var exposed = typeof(TokenManagerBase)
            .GetProperty("EnableTokenDependencyCascade",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        exposed.Should().NotBeNull();
        exposed!.GetValue(manager).Should().Be(false, "EnableTokenDependencyCascade 默认必须为 false");
    }

    // ── 级联传播（开关打开） ─────────────────────────────────────────

    [Fact]
    public async Task CascadeEnabled_ShouldInvalidateDependentManagers()
    {
        var corp = new CascadeProbeTokenManager(
            tokenType: "corp_access_token", accessToken: "corp-token");
        using var corpDisposable = corp;
        var registry = new Dictionary<string, ITokenManager> { ["corp_access_token"] = corp };
        using var suite = new CascadeProbeTokenManager(
            tokenType: "suite_access_token",
            graph: new TokenDependencyGraph().Add("corp_access_token", "suite_access_token"),
            cascade: true,
            resolver: t => registry.TryGetValue(t, out var m) ? m : null);

        var scopes = new[] { "auth-corp-a" };
        await suite.InvalidateTokenAsync(scopes, CancellationToken.None);
        await ShouldCompleteAsync(corp.Invalidated.Task, "级联应触发 corp 失效（fire-and-forget 但可观测）");

        corp.InvalidateCount.Should().Be(1, "被依赖者 suite 失效 ⇒ 依赖者 corp 连带失效一次");
        corp.ReceivedScopes.Should().ContainSingle().Which.Should().BeSameAs(
            scopes, "级联应沿同一 scopes 归属传播（企业级链式场景 scope 即 authCorpId）");

        var corpToken = await corp.GetTokenAsync(CancellationToken.None);
        corpToken.Should().Be("corp-token", "连带失效后 corp 应重新取令牌（缓存已清）");
        corp.RefreshCount.Should().Be(1, "失效后重新取令牌走一次刷新");
    }

    [Fact]
    public async Task CascadeEnabled_ChainedGraph_ShouldReachTransitiveDependents()
    {
        // 42001 链：provider 失效 ⇒ suite ⇒ corp 全部连带失效。
        var corp = new CascadeProbeTokenManager(tokenType: "corp_access_token");
        var suite = new CascadeProbeTokenManager(tokenType: "suite_access_token");
        using var corpDisposable = corp;
        using var suiteDisposable = suite;
        var registry = new Dictionary<string, ITokenManager>
        {
            ["corp_access_token"] = corp,
            ["suite_access_token"] = suite
        };
        using var provider = new CascadeProbeTokenManager(
            tokenType: "provider_access_token",
            graph: new TokenDependencyGraph()
                .Add("corp_access_token", "suite_access_token")
                .Add("suite_access_token", "provider_access_token"),
            cascade: true,
            resolver: t => registry.TryGetValue(t, out var m) ? m : null);

        await provider.InvalidateTokenAsync(null, CancellationToken.None);
        await ShouldCompleteAsync(corp.Invalidated.Task, "传递闭包应触达 corp");
        await ShouldCompleteAsync(suite.Invalidated.Task, "直接依赖 suite 应被失效");

        suite.InvalidateCount.Should().Be(1, "直接依赖者被级联失效");
        corp.InvalidateCount.Should().Be(1, "传递依赖者被级联失效");
    }

    [Fact]
    public async Task CascadeEnabled_UnresolvableManager_ShouldFailSafeAndNotThrow()
    {
        using var suite = new CascadeProbeTokenManager(
            tokenType: "suite_access_token",
            graph: new TokenDependencyGraph().Add("corp_access_token", "suite_access_token"),
            cascade: true,
            resolver: _ => null);   // 未接线/未注册

        var act = () => suite.InvalidateTokenAsync(null, CancellationToken.None);

        await act.Should().NotThrowAsync("ResolveTokenManagerForType 返回 null 应 fail-safe 跳过，不抛");
    }

    // ── 互依环有限终止 ───────────────────────────────────────────────

    [Fact]
    public async Task CascadeEnabled_MutualDependency_ShouldTerminateWithoutRecursion()
    {
        // A ↔ B 互依：A 失效级联到 B，B 的级联被 AsyncLocal 重入抑制 ⇒ 各自只失效一次。
        var registry = new Dictionary<string, ITokenManager>();
        var graph = new TokenDependencyGraph().Add("A", "B").Add("B", "A");
        Func<string, ITokenManager?> resolver = t => registry.TryGetValue(t, out var m) ? m : null;

        var managerA = new CascadeProbeTokenManager(tokenType: "A", graph: graph, cascade: true, resolver: resolver);
        var managerB = new CascadeProbeTokenManager(tokenType: "B", graph: graph, cascade: true, resolver: resolver);
        using var disposableA = managerA;
        using var disposableB = managerB;
        registry["A"] = managerA;
        registry["B"] = managerB;

        await managerA.InvalidateTokenAsync(null, CancellationToken.None);
        await ShouldCompleteAsync(managerB.Invalidated.Task, "A 失效应级联到 B（互依环不得挂起）");
        await Task.Delay(300);   // 留窗口观察是否发生递归回传

        managerB.InvalidateCount.Should().Be(1, "B 被 A 级联失效一次");
        managerA.InvalidateCount.Should().Be(1, "B 的级联被重入抑制，不得递归回失效 A");
    }
}
