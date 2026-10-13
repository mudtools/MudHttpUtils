// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils.Client.Tests.AppAuth;

/// <summary>
/// F-4：外部标识反查应用（<see cref="IAppLookupProvider"/>）的映射守卫契约。
/// </summary>
/// <remarks>
/// 钉死语义：多对一映射合法；一对多（映射歧义）整键 fail-closed；未命中/非法输入 fail-closed；
/// 反查<b>不做</b>授权判定（与 <see cref="AppKeyAllowListAuthorizer"/> 组合时授权仍在其后）。
/// </remarks>
public class AppLookupProviderTests
{
    /// <summary>测试用上下文：同时声明外部标识（F-4 可选契约）。</summary>
    private sealed class StubAppContext : IMudAppContext, IAppExternalIds
    {
        public StubAppContext(string appKey, IReadOnlyDictionary<string, string>? externalIds = null)
        {
            AppKey = appKey;
            ExternalIds = externalIds ?? new Dictionary<string, string>();
        }

        public string AppKey { get; }
        public IReadOnlyDictionary<string, string> ExternalIds { get; }
        IEnhancedHttpClient IMudAppContext.HttpClient => throw new NotSupportedException();
        ITokenManager IMudAppContext.GetTokenManager(string tokenType) => throw new NotSupportedException();
        T IMudAppContext.GetTokenManager<T>() where T : class => throw new NotSupportedException();
        T? IMudAppContext.GetService<T>() where T : class => null;
    }

    private static IReadOnlyDictionary<string, string> Ids(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
            dict[key] = value;
        return dict;
    }

    [Fact]
    public void Manager_Implements_IAppLookupProvider()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.Should().BeAssignableTo<IAppLookupProvider>();
    }

    // ── 命中与多对一 ──────────────────────────────────────

    [Fact]
    public void TryResolveAppKey_SingleId_ResolvesAppKey()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out var appKey)
            .Should().BeTrue("SuiteId=ww100 已登记为 app-a 的外部标识");
        appKey.Should().Be("app-a");
    }

    [Fact]
    public void TryResolveAppKey_ManyToOne_MultipleIdsSameApp_Resolves()
    {
        // 多对一：同一应用声明多个外部标识（如 SuiteId + CorpId），任一均可反查。
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a",
            Ids(("SuiteId", "ww100"), ("CorpId", "corp-1"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out var k1).Should().BeTrue();
        k1.Should().Be("app-a");

        manager.TryResolveAppKey(Ids(("CorpId", "corp-1")), out var k2).Should().BeTrue();
        k2.Should().Be("app-a");
    }

    [Fact]
    public void TryResolveAppKey_MultipleKeysCrossVerify_SameApp_Resolves()
    {
        // 多键互证：两键都指向同一应用 → 解析成功。
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a",
            Ids(("SuiteId", "ww100"), ("CorpId", "corp-1"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100"), ("CorpId", "corp-1")), out var appKey)
            .Should().BeTrue();
        appKey.Should().Be("app-a");
    }

    [Fact]
    public void TryResolveAppKey_MultipleKeys_PointingToDifferentApps_FailClosed()
    {
        // 多键互证失败：两键分别指向不同应用 → 无法互证，fail-closed。
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));
        manager.RegisterApp("app-b", new StubAppContext("app-b", Ids(("CorpId", "corp-1"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100"), ("CorpId", "corp-1")), out var appKey)
            .Should().BeFalse("两键指向不同应用时不得猜测其一");
        appKey.Should().BeNull();
    }

    // ── 一对多（映射歧义）守卫 ──────────────────────────────

    [Fact]
    public void TryResolveAppKey_OneToMany_Ambiguous_FailClosedWholeKey()
    {
        // 一对多：同一「键 + 值」被两个不同应用声明 → 该键整体 fail-closed。
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));
        manager.RegisterApp("app-b", new StubAppContext("app-b", Ids(("SuiteId", "ww100"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out var appKey)
            .Should().BeFalse("映射歧义时宁可不可查，不可查错");
        appKey.Should().BeNull();
    }

    [Fact]
    public void TryResolveAppKey_AmbiguousKey_FailClosed_EvenForNonConflictingValue()
    {
        // 歧义是「键」级：键内任一值冲突后，该键的其它值也不可查（防止绕道）。
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));
        manager.RegisterApp("app-b", new StubAppContext("app-b", Ids(("SuiteId", "ww100"), ("SuiteId2", "x"))));
        // app-b 与 app-a 在 SuiteId=ww100 冲突；SuiteId=ww200 唯一登记在 app-b 上。
        manager.RegisterApp("app-c", new StubAppContext("app-c", Ids(("SuiteId", "ww200"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww200")), out _)
            .Should().BeFalse("SuiteId 键已歧义，整键 fail-closed（含未冲突的值）");
    }

    [Fact]
    public void TryResolveAppKey_AmbiguityEliminated_AfterConflictPartyRemoved()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));
        manager.RegisterApp("app-b", new StubAppContext("app-b", Ids(("SuiteId", "ww100"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out _).Should().BeFalse();

        // 冲突方移除后歧义自动消除（整体重建语义）。
        manager.RemoveApp("app-b").Should().BeTrue();
        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out var appKey).Should().BeTrue();
        appKey.Should().Be("app-a");
    }

    [Fact]
    public void TryResolveAppKey_ConflictReportsOncePerAmbiguousKey()
    {
        var reports = new ConcurrentBag<string>();
        var previous = AppManagerDiagnostics.ExternalIdConflict;
        try
        {
            AppManagerDiagnostics.ExternalIdConflict = (key, _, _, _) => reports.Add(key);

            var manager = new DefaultAppManager<IMudAppContext>();
            manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));
            manager.RegisterApp("app-b", new StubAppContext("app-b", Ids(("SuiteId", "ww100"))));

            var firstCount = reports.Count;
            firstCount.Should().Be(1, "歧义键只报一次新增诊断");

            // 后续无关注册触发重建，不得对同一歧义键重复刷诊断。
            manager.RegisterApp("app-c", new StubAppContext("app-c", Ids(("CorpId", "corp-9"))));
            reports.Count.Should().Be(firstCount, "同一歧义键不重复报告");
        }
        finally
        {
            AppManagerDiagnostics.ExternalIdConflict = previous;
        }
    }

    // ── 未命中 / 非法输入 fail-closed ──────────────────────────────

    [Fact]
    public void TryResolveAppKey_UnregisteredId_FailClosed()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww999")), out var appKey).Should().BeFalse();
        appKey.Should().BeNull();

        manager.TryResolveAppKey(Ids(("UnknownKey", "ww100")), out _).Should().BeFalse("键名同样参与匹配");
    }

    [Theory]
    [InlineData(true)]   // null 字典
    [InlineData(false)]  // 空字典
    public void TryResolveAppKey_NullOrEmptyInput_FailClosed(bool useNull)
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));

        var input = useNull
            ? null!
            : new Dictionary<string, string>();

        manager.TryResolveAppKey(input, out var appKey).Should().BeFalse("非法输入 fail-closed");
        appKey.Should().BeNull();
    }

    [Fact]
    public void TryResolveAppKey_AllEmptyValueEntries_FailClosed()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));

        manager.TryResolveAppKey(Ids(("", "v"), ("k", "")), out var appKey)
            .Should().BeFalse("全部为空键/空值项时无有效标识");
        appKey.Should().BeNull();
    }

    [Fact]
    public void TryResolveAppKey_IsCaseSensitive_Ordinal()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));

        // 与 appKey 语义一致：键与值均序数比较，大小写不同即未命中。
        manager.TryResolveAppKey(Ids(("suiteid", "ww100")), out _).Should().BeFalse();
        manager.TryResolveAppKey(Ids(("SuiteId", "WW100")), out _).Should().BeFalse();
    }

    // ── 生命周期联动 ──────────────────────────────────────

    [Fact]
    public void TryResolveAppKey_LazyApp_JoinsIndexAfterMaterialization()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterLazy("app-lazy", () => new StubAppContext("app-lazy", Ids(("SuiteId", "ww700"))));

        // 未实例化前不参与反查（实例化即副作用，枚举/反查不触发）。
        manager.TryResolveAppKey(Ids(("SuiteId", "ww700")), out _).Should().BeFalse(
            "懒加载项未实例化前外部标识不可见（避免反查触实例化副作用）");

        manager.GetApp("app-lazy");     // 触发实例化

        manager.TryResolveAppKey(Ids(("SuiteId", "ww700")), out var appKey).Should().BeTrue();
        appKey.Should().Be("app-lazy");
    }

    [Fact]
    public void TryResolveAppKey_AfterRemoveApp_FailClosed()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out _).Should().BeTrue();

        manager.RemoveApp("app-a").Should().BeTrue();

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out _)
            .Should().BeFalse("RemoveApp 后外部标识不得再反查到该应用");
    }

    [Fact]
    public void TryResolveAppKey_AfterUpdateApp_UsesNewExternalIds()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));

        manager.UpdateApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww200"))));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out _)
            .Should().BeFalse("旧外部标识随更新失效");
        manager.TryResolveAppKey(Ids(("SuiteId", "ww200")), out var appKey).Should().BeTrue();
        appKey.Should().Be("app-a");
    }

    // ── 外部提供者兜底 ──────────────────────────────────────

    [Fact]
    public void TryResolveAppKey_ExternalProvider_Fallback_Resolves()
    {
        var manager = new DefaultAppManager<IMudAppContext>(
            new StubLookupProvider(ids => ids.TryGetValue("SuiteId", out var v) && v == "ww800" ? "app-a" : null));
        manager.RegisterApp("app-a", new StubAppContext("app-a"));   // 无内置外部标识

        manager.TryResolveAppKey(Ids(("SuiteId", "ww800")), out var appKey)
            .Should().BeTrue("内置未命中时委托外部提供者兜底");
        appKey.Should().Be("app-a");
    }

    [Fact]
    public void TryResolveAppKey_ExternalProvider_TargetNotRegistered_FailClosed()
    {
        // 外部提供者解析出的 appKey 必须落在已注册应用上（防误路由）。
        var manager = new DefaultAppManager<IMudAppContext>(
            new StubLookupProvider(ids => ids.TryGetValue("SuiteId", out var v) && v == "ww800" ? "ghost-app" : null));
        manager.RegisterApp("app-a", new StubAppContext("app-a"));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww800")), out var appKey)
            .Should().BeFalse("外部提供者解析出的 appKey 未注册时 fail-closed");
        appKey.Should().BeNull();
    }

    [Fact]
    public void TryResolveAppKey_InternalIndexTakesPrecedence_OverExternalProvider()
    {
        // 内置索引与注册视图同源一致，优先于外部提供者。
        var manager = new DefaultAppManager<IMudAppContext>(
            new StubLookupProvider(ids => ids.TryGetValue("SuiteId", out var v) && v == "ww100" ? "app-b" : null));
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));
        manager.RegisterApp("app-b", new StubAppContext("app-b"));

        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out var appKey).Should().BeTrue();
        appKey.Should().Be("app-a", "内置索引完整命中时不委托外部提供者");
    }

    // ── 与授权链路组合（反查 ≠ 授权） ──────────────────────────────

    [Fact]
    public void TryResolveAppKey_CombinedWithAllowListAuthorizer_StillRequiresAuthorization()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-a", new StubAppContext("app-a", Ids(("SuiteId", "ww100"))));
        manager.RegisterApp("app-b", new StubAppContext("app-b", Ids(("SuiteId", "ww200"))));

        var authorizer = new AppKeyAllowListAuthorizer(new[] { "app-b" });

        // 回调场景链路：外部标识 → appKey → 授权守卫 → 才允许切换。
        manager.TryResolveAppKey(Ids(("SuiteId", "ww100")), out var resolvedA).Should().BeTrue();
        authorizer.CanSwitchTo(resolvedA!).Should().BeFalse(
            "反查只做映射不做授权：白名单外的 appKey 仍须被拒绝");

        manager.TryResolveAppKey(Ids(("SuiteId", "ww200")), out var resolvedB).Should().BeTrue();
        authorizer.CanSwitchTo(resolvedB!).Should().BeTrue();
    }

    /// <summary>测试用外部提供者：固定映射表反查。</summary>
    private sealed class StubLookupProvider : IAppLookupProvider
    {
        private readonly Func<IReadOnlyDictionary<string, string>, string?> _resolver;

        public StubLookupProvider(Func<IReadOnlyDictionary<string, string>, string?> resolver)
            => _resolver = resolver;

        public bool TryResolveAppKey(IReadOnlyDictionary<string, string> externalIds, out string? appKey)
        {
            appKey = _resolver(externalIds);
            return appKey is not null;
        }
    }
}
