// -----------------------------------------------------------------------
//  作者：Mud Studio 版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 多应用管理器语义回归（2026-10-28 方案 P0/P1：AM2-03 / AM2-04 / AM2-07 / AM2-09 / AM2-10 / AM2-11 / AM2-17）。
/// </summary>
/// <remarks>
/// 每个用例对应方案中的<b>锚点断言</b>：未应用修复时必须失败。评审时可注释掉修复代码验证。
/// </remarks>
public class AppManagerSemanticsTests
{
    private sealed class TestAppContext(string appKey) : IMudAppContext, IDisposable
    {
        public string AppKey { get; } = appKey;

        public bool Disposed { get; private set; }

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;

        public void Dispose() => Disposed = true;
    }

    /// <summary>可覆写延迟释放窗口的测试管理器（AM2-10）。</summary>
    private sealed class FastRetireManager(TimeSpan delay) : DefaultAppManager<IMudAppContext>
    {
        protected override TimeSpan RetireDelay => delay;
    }

    private sealed class InitializableAppContext(string appKey) : IMudAppContext, IAsyncInitializable, IDisposable
    {
        public string AppKey { get; } = appKey;

        public bool Disposed { get; private set; }

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() => Disposed = true;
    }

    // ───────────────────────── AM2-03 ─────────────────────────

    [Fact]
    public void UpdateApp_LazyDeclaredKey_Succeeds()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterLazy("lazy", () => new TestAppContext("lazy-v1"));

        var updated = new TestAppContext("lazy-v2");
        manager.UpdateApp("lazy", updated);

        manager.GetApp("lazy").Should().BeSameAs(updated,
            "AM2-03：声明态 ∪ 物化态 统一存在性口径，UpdateApp 必须能把\"仅声明\"升级为物化");
    }

    [Fact]
    public void UpdateApp_LazyDeclaredKey_ConcurrentWithMaterialize_NoLostUpdate()
    {
        for (var round = 0; round < 20; round++)
        {
            var manager = new DefaultAppManager<IMudAppContext>();
            manager.RegisterLazy("k", () => new TestAppContext("from-factory"));

            var winner = new TestAppContext("from-update");

            // 并发：一边首次命中触发物化，一边 UpdateApp。
            var tasks = new[]
            {
                Task.Run(() => { try { _ = manager.GetApp("k"); } catch (InvalidOperationException) { } }),
                Task.Run(() => manager.UpdateApp("k", winner)),
            };
            Task.WaitAll(tasks);

            manager.GetApp("k").Should().BeSameAs(winner,
                "AM2-03（X9）：并发物化窗口下 UpdateApp 不得静默丢失（裸 Add 会失败并保留工厂值）");
        }
    }

    [Fact]
    public void UpdateApp_UnknownKey_Throws()
    {
        var manager = new DefaultAppManager<IMudAppContext>();

        var act = () => manager.UpdateApp("nope", new TestAppContext("nope"));

        act.Should().Throw<InvalidOperationException>("未知键仍须 fail-closed");
    }

    // ───────────────────────── AM2-04 ─────────────────────────

    [Fact]
    public void RemoveDefault_WhenRemainingAreLazy_FallsBackToLazyKey()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("default-app", new TestAppContext("default-app"), isDefault: true);
        manager.RegisterLazy("lazy-app", () => new TestAppContext("lazy-app"));

        manager.RemoveApp("default-app").Should().BeTrue();

        manager.DefaultAppKey.Should().Be("lazy-app",
            "AM2-04：回退候选必须纳入\"仅声明\"的懒加载键（此前只枚举 _registry ⇒ 键被置空）");
        manager.GetDefaultApp().AppKey.Should().Be("lazy-app", "选中声明态键后应正常物化");
    }

    [Fact]
    public void RemoveDefault_NoRemainingApps_KeyBecomesNull()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("only", new TestAppContext("only"), isDefault: true);

        manager.RemoveApp("only");

        manager.DefaultAppKey.Should().BeNull("确实无任何剩余应用时置空（不伪造候选）");
        var act = () => manager.GetDefaultApp();
        act.Should().Throw<InvalidOperationException>();
    }

    // ───────────────────────── AM2-07 ─────────────────────────

    [Fact]
    public async Task ConcurrentFirstGet_PublishesAppInstantiatedExactlyOnce()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var factoryCalls = 0;
        var instantiatedEvents = 0;
        manager.AppInstantiated += (_, _) => Interlocked.Increment(ref instantiatedEvents);
        manager.RegisterLazy("hot", () =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new TestAppContext("hot");
        });

        const int concurrency = 32;
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            await start.Task.ConfigureAwait(false);
            return manager.GetApp("hot");
        })).ToArray();

        start.SetResult(true);
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        factoryCalls.Should().Be(1, "Lazy<T> 保证工厂只执行一次");
        instantiatedEvents.Should().Be(1,
            "AM2-07：以注册表 Add 返回值作一次性闸门，AppInstantiated 只能发布一次（此前 N 次）");
        results.Should().OnlyContain(c => ReferenceEquals(c, results[0]), "并发首访必须拿到同一实例");
    }

    [Fact]
    public void MaterializeThenRegisterLazyAgain_KeepsMaterializedInstance()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var first = new TestAppContext("first");
        manager.RegisterLazy("k", () => first);
        var materialized = manager.GetApp("k");

        manager.RegisterLazy("k", () => new TestAppContext("second"));

        manager.GetApp("k").Should().BeSameAs(materialized,
            "已物化项重新 RegisterLazy 只替换工厂，保留已实例化值（既有文档语义不变）");
    }

    // ───────────────────────── AM2-09 ─────────────────────────

    [Fact]
    public void RegisterApp_OverLazyKey_EmitsUpdated()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var changeTypes = new List<AppConfigurationChangeType>();
        manager.RegisterLazy("k", () => new TestAppContext("lazy"));
        manager.ConfigurationChanged += (_, e) => changeTypes.Add(e.ChangeType);

        manager.RegisterApp("k", new TestAppContext("eager"));

        changeTypes.Should().ContainSingle().Which.Should().Be(AppConfigurationChangeType.Updated,
            "AM2-09：覆盖\"仅声明\"的键属于 Updated（此前误报 Added）");
    }

    [Fact]
    public void RegisterLazy_ThenRegisterApp_NoDuplicateAdded()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterLazy("k", () => new TestAppContext("lazy"));

        var changeTypes = new List<AppConfigurationChangeType>();
        manager.ConfigurationChanged += (_, e) => changeTypes.Add(e.ChangeType);

        manager.RegisterApp("k", new TestAppContext("eager"));

        changeTypes.Should().NotContain(AppConfigurationChangeType.Added,
            "同一键的 Added 事件只能由首次登记发出");
    }

    [Fact]
    public void RegisterLazy_OnExistingKey_EmitsUpdated()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterLazy("k", () => new TestAppContext("v1"));

        var changeTypes = new List<AppConfigurationChangeType>();
        manager.ConfigurationChanged += (_, e) => changeTypes.Add(e.ChangeType);

        manager.RegisterLazy("k", () => new TestAppContext("v2"));

        changeTypes.Should().ContainSingle().Which.Should().Be(AppConfigurationChangeType.Updated,
            "重复 RegisterLazy 同一键属于 Updated（B9：判定改为\"已声明 ∪ 已物化\"）");
    }

    // ───────────────────────── AM2-11 ─────────────────────────

    [Fact]
    public void SetDefaultApp_RaisesConfigurationChanged()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("a", new TestAppContext("a"));
        manager.RegisterApp("b", new TestAppContext("b"));

        var events = new List<AppConfigurationChangedEventArgs>();
        manager.ConfigurationChanged += (_, e) => events.Add(e);

        manager.SetDefaultApp("b");

        events.Should().ContainSingle()
            .Which.IsDefaultAppChanged.Should().BeTrue("AM2-11：默认应用切换此前完全静默");
        events[0].AppKey.Should().Be("b");
    }

    [Fact]
    public void SetDefaultApp_SameKey_DoesNotRaise()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("a", new TestAppContext("a"), isDefault: true);

        var count = 0;
        manager.ConfigurationChanged += (_, _) => count++;

        manager.SetDefaultApp("a");

        count.Should().Be(0, "键未变化时不应产生事件噪声（幂等写入）");
    }

    [Fact]
    public void RemoveDefault_Fallback_RaisesConfigurationChanged()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("a", new TestAppContext("a"), isDefault: true);
        manager.RegisterApp("b", new TestAppContext("b"));

        var defaultChanges = new List<string>();
        manager.ConfigurationChanged += (_, e) =>
        {
            if (e.IsDefaultAppChanged) defaultChanges.Add(e.AppKey);
        };

        manager.RemoveApp("a");

        defaultChanges.Should().ContainSingle("移除默认应用后的回退同样必须可观测");
    }

    // ───────────────────────── AM2-10 ─────────────────────────

    [Fact]
    public async Task UpdateApp_RetiresOldContext_AfterDelay()
    {
        var manager = new FastRetireManager(TimeSpan.FromMilliseconds(50));
        var old = new TestAppContext("old");
        manager.RegisterApp("k", old);

        manager.UpdateApp("k", new TestAppContext("new"));

        old.Disposed.Should().BeFalse("AM2-10：延迟窗口内不得立即释放（在途请求仍可能持有它）");

        await Task.Delay(500).ConfigureAwait(false);
        old.Disposed.Should().BeTrue("延迟窗口结束后旧实例必须被释放（IServiceScope/HttpClient/Timer 泄漏）");
    }

    [Fact]
    public async Task RemoveApp_RetiresContext()
    {
        var manager = new FastRetireManager(TimeSpan.FromMilliseconds(50));
        var ctx = new TestAppContext("k");
        manager.RegisterApp("k", ctx);

        manager.RemoveApp("k");

        await Task.Delay(500).ConfigureAwait(false);
        ctx.Disposed.Should().BeTrue("被移除的上下文同样需要回收");
    }

    [Fact]
    public void RetireDelayZero_DisposesImmediately()
    {
        var manager = new FastRetireManager(TimeSpan.Zero);
        var old = new TestAppContext("old");
        manager.RegisterApp("k", old);

        manager.UpdateApp("k", new TestAppContext("new"));

        old.Disposed.Should().BeTrue("RetireDelay ≤ 0 表示立即释放（逃生闸门）");
    }

    [Fact]
    public void DisposeFailure_DoesNotBreakConfigurationChange()
    {
        var manager = new FastRetireManager(TimeSpan.Zero);
        var throwsOnDispose = new ThrowingDisposableContext("boom");
        manager.RegisterApp("k", throwsOnDispose);

        var act = () => manager.UpdateApp("k", new TestAppContext("new"));

        act.Should().NotThrow("释放失败是\"尽力而为\"，不得把已提交的配置变更表现为失败");
        manager.GetApp("k").AppKey.Should().Be("new");
    }

    private sealed class ThrowingDisposableContext(string appKey) : IMudAppContext, IDisposable
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;

        public void Dispose() => throw new InvalidOperationException("dispose boom");
    }

    // ───────────────────────── AM2-17 ─────────────────────────

    [Fact]
    public async Task RegisterAppAsync_CancelledAfterInit_DoesNotRegister_AndDisposes()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var ctx = new InitializableAppContext("k");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await manager.RegisterAppAsync("k", ctx, cancellationToken: cts.Token)
            .ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        manager.HasApp("k").Should().BeFalse("AM2-17：已取消 = 不注册");
        ctx.Disposed.Should().BeTrue("已取消的注册必须释放新实例，避免泄漏");
    }

    [Fact]
    public async Task UpdateAppAsync_CancelledAfterInit_DoesNotUpdate_AndDisposes()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var original = new TestAppContext("original");
        manager.RegisterApp("k", original);

        var replacement = new InitializableAppContext("replacement");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await manager.UpdateAppAsync("k", replacement, cts.Token).ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        manager.GetApp("k").Should().BeSameAs(original, "取消后不得替换");
        replacement.Disposed.Should().BeTrue();
    }
}
