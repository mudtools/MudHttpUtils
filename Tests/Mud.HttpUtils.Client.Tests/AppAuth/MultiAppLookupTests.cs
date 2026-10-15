// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// U-5：<see cref="IMultiAppLookupProvider"/> 多命中反查契约与
/// <see cref="DefaultAppManager{TAppContext}.TryResolveAllAppKeys"/> 消费面 ——
/// 多命中枚举 + HasApp 过滤（含懒加载未实例化条目、不物化）/ 去重 / 单结果回退 / 内置索引 fail-closed 口径不变。
/// </summary>
public class MultiAppLookupTests
{
    private class TestAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;
        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();
        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();
        public T? GetService<T>() where T : class => null;
    }

    /// <summary>携带外部标识的测试上下文（内置反查索引消费）。</summary>
    private sealed class ExternalIdAppContext : TestAppContext, IAppExternalIds
    {
        public ExternalIdAppContext(string appKey, IReadOnlyDictionary<string, string> externalIds)
            : base(appKey) => ExternalIds = externalIds;

        public IReadOnlyDictionary<string, string> ExternalIds { get; }
    }

    /// <summary>多命中提供者（真实接入形态：同时实现单结果面与多命中面）。</summary>
    private sealed class MultiProvider(Func<IReadOnlyDictionary<string, string>, IEnumerable<string>> impl)
        : IAppLookupProvider, IMultiAppLookupProvider
    {
        public int Calls { get; private set; }

        public bool TryResolveAppKey(IReadOnlyDictionary<string, string> externalIds, out string? appKey)
        {
            appKey = null;
            return false;
        }

        public IEnumerable<string> ResolveAllAppKeys(IReadOnlyDictionary<string, string> externalIds)
        {
            Calls++;
            return impl(externalIds);
        }
    }

    private sealed class SingleProvider(string? resolved) : IAppLookupProvider
    {
        public bool TryResolveAppKey(IReadOnlyDictionary<string, string> externalIds, out string? appKey)
        {
            appKey = resolved;
            return resolved != null;
        }
    }

    // ── 多命中提供者路径 ───────────────────────────────────────────────

    [Fact]
    public void TryResolveAllAppKeys_MultiProvider_Should_ReturnAllHits_Deduped_AndFiltered()
    {
        var provider = new MultiProvider(_ => new[] { "app-a", "app-b", "app-a", "ghost", "" });
        var manager = new DefaultAppManager<IMudAppContext>(provider);
        manager.RegisterApp("app-a", new TestAppContext("app-a"));
        manager.RegisterApp("app-b", new TestAppContext("app-b"));
        // "ghost" 未注册、"app-c" 注册但提供者未返回

        var result = manager.TryResolveAllAppKeys(new Dictionary<string, string> { ["SuiteId"] = "ww1" });

        result.Should().BeEquivalentTo(new[] { "app-a", "app-b" },
            "多命中全量返回：去重 + 未注册项过滤（HasApp）+ 空白项过滤");
        provider.Calls.Should().Be(1);
    }

    [Fact]
    public void TryResolveAllAppKeys_MultiProvider_Should_IncludeLazyUninstantiated_WithoutMaterializing()
    {
        var provider = new MultiProvider(_ => new[] { "lazy-app" });
        var manager = new DefaultAppManager<IMudAppContext>(provider);
        var calls = 0;
        manager.RegisterLazy("lazy-app", () =>
        {
            Interlocked.Increment(ref calls);
            return new TestAppContext("lazy-app");
        });

        var result = manager.TryResolveAllAppKeys(new Dictionary<string, string> { ["SuiteId"] = "ww1" });

        result.Should().BeEquivalentTo(new[] { "lazy-app" }, "懒加载「已声明即存在」（C-6 同口径）");
        calls.Should().Be(0, "纯反查不得触发实例化副作用");
    }

    [Fact]
    public void TryResolveAllAppKeys_MultiProvider_Should_RecordAmbiguity_AsMultipleHits()
    {
        // 微信场景：同一 suiteId 注册在多个应用 → 全部返回（歧义显式表达，由调用方记录并全部处理）
        var provider = new MultiProvider(_ => new[] { "app-1", "app-2" });
        var manager = new DefaultAppManager<IMudAppContext>(provider);
        manager.RegisterApp("app-1", new TestAppContext("app-1"));
        manager.RegisterApp("app-2", new TestAppContext("app-2"));

        var result = manager.TryResolveAllAppKeys(new Dictionary<string, string> { ["SuiteId"] = "ww1" }).ToList();

        result.Should().HaveCount(2);
    }

    // ── 单结果回退路径 ─────────────────────────────────────────────────

    [Fact]
    public void TryResolveAllAppKeys_SingleResultProvider_Should_DegradeToF4Behavior()
    {
        var manager = new DefaultAppManager<IMudAppContext>(new SingleProvider("app-a"));
        manager.RegisterApp("app-a", new TestAppContext("app-a"));

        var result = manager.TryResolveAllAppKeys(new Dictionary<string, string> { ["SuiteId"] = "ww1" });

        result.Should().BeEquivalentTo(new[] { "app-a" }, "仅实现 IAppLookupProvider → 退化为单结果");
    }

    [Fact]
    public void TryResolveAllAppKeys_SingleResultProvider_Unregistered_Should_BeFiltered()
    {
        var manager = new DefaultAppManager<IMudAppContext>(new SingleProvider("nope"));

        var result = manager.TryResolveAllAppKeys(new Dictionary<string, string> { ["SuiteId"] = "ww1" });

        result.Should().BeEmpty("回退结果同样须经 HasApp 校验（防误路由到不存在应用）");
    }

    // ── 内置索引路径（无外部提供者）────────────────────────────────────

    [Fact]
    public void TryResolveAllAppKeys_BuiltinIndex_Hit_Should_ReturnSingleViaFallback()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new ExternalIdAppContext("app-a",
            new Dictionary<string, string> { ["SuiteId"] = "ww1" }));

        var result = manager.TryResolveAllAppKeys(new Dictionary<string, string> { ["SuiteId"] = "ww1" });

        result.Should().BeEquivalentTo(new[] { "app-a" }, "内置索引命中经单结果回退返回");
    }

    [Fact]
    public void TryResolveAllAppKeys_BuiltinIndex_AmbiguousKey_Should_ReturnEmpty_FailClosed()
    {
        // 同一（键+值）被两个应用声明 → 内置索引标记歧义；多命中语义由 IMultiAppLookupProvider 承接，
        // 内置路径维持 F-4 fail-closed 口径不变（TryResolveAllAppKeys 返回空集）。
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-1", new ExternalIdAppContext("app-1",
            new Dictionary<string, string> { ["SuiteId"] = "ww-shared" }));
        manager.RegisterApp("app-2", new ExternalIdAppContext("app-2",
            new Dictionary<string, string> { ["SuiteId"] = "ww-shared" }));

        manager.TryResolveAppKey(new Dictionary<string, string> { ["SuiteId"] = "ww-shared" }, out _)
            .Should().BeFalse("F-4 单结果语义不变：歧义 fail-closed");
        manager.TryResolveAllAppKeys(new Dictionary<string, string> { ["SuiteId"] = "ww-shared" })
            .Should().BeEmpty("内置路径多命中退化为 fail-closed 空集（歧义须由多命中提供者承接）");
    }

    // ── 输入守卫 ───────────────────────────────────────────────────────

    [Fact]
    public void TryResolveAllAppKeys_NullOrEmptyInput_Should_ReturnEmpty()
    {
        var provider = new MultiProvider(_ => throw new InvalidOperationException("不应被调用"));
        var manager = new DefaultAppManager<IMudAppContext>(provider);

        manager.TryResolveAllAppKeys(null!).Should().BeEmpty();
        manager.TryResolveAllAppKeys(new Dictionary<string, string>()).Should().BeEmpty();
        provider.Calls.Should().Be(0, "输入非法时不得触达提供者");
    }

    [Fact]
    public void TryResolveAllAppKeys_AllEmptyValueEntries_Should_ReturnEmpty()
    {
        var manager = new DefaultAppManager<IMudAppContext>(new SingleProvider(null));

        manager.TryResolveAllAppKeys(new Dictionary<string, string> { ["SuiteId"] = "", ["CorpId"] = null! })
            .Should().BeEmpty();
    }
}
