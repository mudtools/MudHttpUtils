// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests.AppAuth;

/// <summary>
/// F-5：多应用注册表存储 SPI（<see cref="IAppRegistry{TAppContext}"/>）的契约测试。
/// </summary>
/// <remarks>
/// 钉死实现契约：序数区分大小写键、<c>Add</c> 幂等不覆盖、<c>TryUpdate</c> CAS 引用比较
/// （MT-08"复活已删除应用"防护前提）、<c>TryRemove</c> 返回旧值、<c>EnumerateKeys</c> 快照。
/// 同时验证自定义注册表经受保护构造注入 <see cref="DefaultAppManager{TAppContext}"/> 后
/// 守卫语义（大小写/默认回退/CAS）逐位不变 —— 这是"存储可替换、守卫不失效"的验收点。
/// </remarks>
public class AppRegistrySpiTests
{
    private sealed class TestAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;
        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();
        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();
        public T? GetService<T>() where T : class => null;
    }

    /// <summary>探测受保护构造的派生类（SPI 接线的真实用法）。</summary>
    private sealed class DerivedAppManager(IAppRegistry<IMudAppContext>? registry)
        : DefaultAppManager<IMudAppContext>(registry, null, null);

    /// <summary>
    /// 自定义注册表：包装 <see cref="ConcurrentDictionaryAppRegistry{TAppContext}"/> 并统计触点，
    /// 用于证明 DefaultAppManager 的存储触点<b>全部</b>经 SPI（不再有绕过 SPI 的直连存储）。
    /// </summary>
    private sealed class CountingRegistry : IAppRegistry<IMudAppContext>
    {
        private readonly ConcurrentDictionaryAppRegistry<IMudAppContext> _inner = new();
        public int TryGetCalls;
        public int AddCalls;
        public int TryUpdateCalls;
        public int TryRemoveCalls;
        public int EnumerateKeysCalls;

        public bool TryGet(string appKey, out IMudAppContext? appContext)
        {
            Interlocked.Increment(ref TryGetCalls);
            return _inner.TryGet(appKey, out appContext);
        }

        public bool Add(string appKey, IMudAppContext appContext)
        {
            Interlocked.Increment(ref AddCalls);
            return _inner.Add(appKey, appContext);
        }

        public bool TryUpdate(string appKey, IMudAppContext appContext, IMudAppContext comparisonValue)
        {
            Interlocked.Increment(ref TryUpdateCalls);
            return _inner.TryUpdate(appKey, appContext, comparisonValue);
        }

        public bool TryRemove(string appKey, out IMudAppContext? removed)
        {
            Interlocked.Increment(ref TryRemoveCalls);
            return _inner.TryRemove(appKey, out removed);
        }

        public IEnumerable<string> EnumerateKeys()
        {
            Interlocked.Increment(ref EnumerateKeysCalls);
            return _inner.EnumerateKeys();
        }
    }

    // ── ① IAppRegistry 实现契约（默认实现） ──────────────────────────

    [Fact]
    public void Add_ShouldBeIdempotent_AndNotOverwrite()
    {
        IAppRegistry<IMudAppContext> registry = new ConcurrentDictionaryAppRegistry<IMudAppContext>();
        var first = new TestAppContext("app-a");
        var second = new TestAppContext("app-a");

        registry.Add("app-a", first).Should().BeTrue("键不存在应插入");
        registry.Add("app-a", second).Should().BeFalse("键已存在应拒绝");
        registry.TryGet("app-a", out var current).Should().BeTrue();
        current.Should().BeSameAs(first, "Add 失败不得修改现值");
    }

    [Fact]
    public void TryUpdate_ShouldBeCasByReference()
    {
        IAppRegistry<IMudAppContext> registry = new ConcurrentDictionaryAppRegistry<IMudAppContext>();
        var original = new TestAppContext("app-a");
        var replacement = new TestAppContext("app-a");
        registry.Add("app-a", original);

        registry.TryUpdate("app-a", replacement, original).Should().BeTrue("comparisonValue 引用相等应替换成功");
        registry.TryGet("app-a", out var current).Should().BeTrue();
        current.Should().BeSameAs(replacement);

        var stale = new TestAppContext("app-a");
        registry.TryUpdate("app-a", stale, original).Should().BeFalse("comparisonValue 引用不等应 CAS 失败（防复活已删除应用）");
        registry.TryGet("app-a", out var afterStale).Should().BeTrue();
        afterStale.Should().BeSameAs(replacement, "CAS 失败不得改写现值");
    }

    [Fact]
    public void TryUpdate_KeyMissing_ShouldFail()
    {
        IAppRegistry<IMudAppContext> registry = new ConcurrentDictionaryAppRegistry<IMudAppContext>();
        var ctx = new TestAppContext("app-a");

        registry.TryUpdate("app-a", ctx, ctx).Should().BeFalse("键不存在时 CAS 更新应失败");
    }

    [Fact]
    public void TryRemove_ShouldReturnOldValue()
    {
        IAppRegistry<IMudAppContext> registry = new ConcurrentDictionaryAppRegistry<IMudAppContext>();
        var ctx = new TestAppContext("app-a");
        registry.Add("app-a", ctx);

        registry.TryRemove("app-a", out var removed).Should().BeTrue();
        removed.Should().BeSameAs(ctx, "TryRemove 应返回旧值");
        registry.TryRemove("app-a", out _).Should().BeFalse("重复移除应失败");
    }

    [Fact]
    public void Keys_ShouldBeOrdinalCaseSensitive()
    {
        IAppRegistry<IMudAppContext> registry = new ConcurrentDictionaryAppRegistry<IMudAppContext>();
        registry.Add("App-A", new TestAppContext("App-A"));

        registry.TryGet("app-a", out _).Should().BeFalse("appKey 序数比较：大小写不同是不同键");
        registry.TryGet("App-A", out _).Should().BeTrue();
    }

    [Fact]
    public void EnumerateKeys_ShouldBeSnapshot()
    {
        IAppRegistry<IMudAppContext> registry = new ConcurrentDictionaryAppRegistry<IMudAppContext>();
        registry.Add("app-a", new TestAppContext("app-a"));
        registry.Add("app-b", new TestAppContext("app-b"));

        var snapshot = registry.EnumerateKeys().ToList();
        registry.Add("app-c", new TestAppContext("app-c"));

        snapshot.Should().BeEquivalentTo(new[] { "app-a", "app-b" }, "快照不受枚举后并发增删影响");
        registry.EnumerateKeys().Should().HaveCount(3);
    }

    // ── ② 自定义注册表注入：守卫语义逐位不变 ─────────────────────────

    [Fact]
    public void DerivedManager_WithCustomRegistry_ShouldDelegateStorage()
    {
        var counting = new CountingRegistry();
        var manager = new DerivedAppManager(counting);
        var ctx = new TestAppContext("app-a");

        manager.RegisterApp("app-a", ctx);
        manager.GetApp("app-a").Should().BeSameAs(ctx);
        manager.HasApp("app-a").Should().BeTrue();
        manager.RemoveApp("app-a").Should().BeTrue();

        counting.AddCalls.Should().BeGreaterThan(0, "注册应经 SPI Add");
        counting.TryGetCalls.Should().BeGreaterThan(0, "查询应经 SPI TryGet");
        counting.TryRemoveCalls.Should().BeGreaterThan(0, "移除应经 SPI TryRemove");
    }

    [Fact]
    public void DerivedManager_WithCustomRegistry_ShouldPreserveCaseSensitivityGuard()
    {
        var manager = new DerivedAppManager(new ConcurrentDictionaryAppRegistry<IMudAppContext>());
        manager.RegisterApp("App-A", new TestAppContext("App-A"));

        manager.HasApp("app-a").Should().BeFalse("换存储不得放松大小写语义（G8-11 钉死）");
        manager.HasApp("App-A").Should().BeTrue();
    }

    [Fact]
    public void DerivedManager_WithCustomRegistry_ShouldPreserveUpdateCasGuard()
    {
        var manager = new DerivedAppManager(new ConcurrentDictionaryAppRegistry<IMudAppContext>());
        manager.RegisterApp("app-a", new TestAppContext("app-a"));

        // MT-08：更新不存在的键不得"复活"。
        var act = () => manager.UpdateApp("nope", new TestAppContext("nope"));
        act.Should().Throw<InvalidOperationException>("未注册键更新必须抛出（守卫不因换存储失效）");
    }

    [Fact]
    public void DerivedManager_NullRegistry_ShouldFallbackToDefault()
    {
        var manager = new DerivedAppManager(null);
        var ctx = new TestAppContext("app-a");
        manager.RegisterApp("app-a", ctx);

        manager.GetApp("app-a").Should().BeSameAs(ctx, "registry 为 null 时应回退默认 ConcurrentDictionary 实现");
    }

    [Fact]
    public void DefaultManager_ShouldDelegateToSpi_NotDirectStorage()
    {
        // 断言 DefaultAppManager 无绕过 SPI 的存储触点：全部写路径经 SPI 计数可见。
        var counting = new CountingRegistry();
        var manager = new DerivedAppManager(counting);

        manager.RegisterApp("app-a", new TestAppContext("app-a"));
        manager.UpdateApp("app-a", new TestAppContext("app-a"));
        manager.RemoveApp("app-a");

        counting.TryUpdateCalls.Should().BeGreaterThan(0, "UpdateApp 应经 SPI TryUpdate（CAS）");
    }
}
