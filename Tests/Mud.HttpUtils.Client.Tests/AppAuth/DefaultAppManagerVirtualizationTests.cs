// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// G3 + B8：<see cref="DefaultAppManager{TAppContext}"/> 的<b>注册/默认键入口虚化</b>与
/// <b>懒加载注册</b>（消除"查询入口 virtual、写入入口非 virtual"导致的影子注册表）。
/// </summary>
public class DefaultAppManagerVirtualizationTests
{
    private sealed class TestAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;
    }

    /// <summary>暴露 protected 写入口以便断言（派生类场景的真实用法）。</summary>
    private sealed class ProbeAppManager : DefaultAppManager<IMudAppContext>
    {
        public bool SetDefaultKey(string appKey, bool requireRegistered)
            => TrySetDefaultAppKeyCore(appKey, requireRegistered);
    }

    // ── ① 入口虚化（消除影子注册表的前提） ──────────────────────────────

    [Theory]
    [InlineData("RegisterApp")]
    [InlineData("RegisterAppAsync")]
    [InlineData("UpdateAppAsync")]
    [InlineData("TrySetDefaultApp")]
    [InlineData("RegisterLazy")]
    public void RegistrationEntryPoints_ShouldBeVirtual(string methodName)
    {
        typeof(DefaultAppManager<IMudAppContext>)
            .GetMethod(methodName)
            .Should().NotBeNull($"{methodName} 必须存在");
        typeof(DefaultAppManager<IMudAppContext>)
            .GetMethod(methodName)!.IsVirtual
            .Should().BeTrue($"G3：{methodName} 必须可覆写（否则派生类只能造影子注册表）");
    }

    [Fact]
    public void DefaultAppKey_ShouldBeVirtual()
    {
        typeof(DefaultAppManager<IMudAppContext>)
            .GetProperty(nameof(DefaultAppManager<IMudAppContext>.DefaultAppKey))!
            .GetMethod!
            .IsVirtual
            .Should().BeTrue("G3：默认键读取必须可覆写");
    }

    [Fact]
    public void TrySetDefaultAppKeyCore_ShouldBeProtectedVirtual()
    {
        var method = typeof(DefaultAppManager<IMudAppContext>).GetMethod(
            "TrySetDefaultAppKeyCore",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        method.Should().NotBeNull();
        method!.IsFamily.Should().BeTrue("protected");
        method.IsVirtual.Should().BeTrue();
    }

    // ── ② 懒加载注册 ───────────────────────────────────────────────────

    [Fact]
    public void RegisterLazy_FirstGetApp_ShouldInvokeFactoryOnce()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var calls = 0;
        manager.RegisterLazy("lazy-app", () =>
        {
            Interlocked.Increment(ref calls);
            return new TestAppContext("lazy-app");
        });

        calls.Should().Be(0, "注册阶段不得实例化");

        var first = manager.GetApp("lazy-app");
        var second = manager.GetApp("lazy-app");

        calls.Should().Be(1, "工厂只调用一次（Lazy<T> ExecutionAndPublication）");
        first.Should().BeSameAs(second);
        manager.HasApp("lazy-app").Should().BeTrue();
    }

    [Fact]
    public void RegisterLazy_WithIsDefault_ShouldAllowUnregisteredDefaultKey()
    {
        var manager = new DefaultAppManager<IMudAppContext>();

        // B8 真缺口：先声明默认键，应用稍后才实例化（此前 TrySetDefaultApp 因"未注册"直接失败）
        manager.RegisterLazy("lazy-default", () => new TestAppContext("lazy-default"), isDefault: true);

        manager.DefaultAppKey.Should().Be("lazy-default");
        manager.GetDefaultApp().AppKey.Should().Be("lazy-default", "默认应用可为懒加载项");
    }

    [Fact]
    public void TrySetDefaultAppKeyCore_RequireRegistered_ShouldGateOnRegistration()
    {
        var manager = new ProbeAppManager();

        manager.SetDefaultKey("unknown", requireRegistered: true).Should().BeFalse("未注册 ⇒ 拒绝（既有语义）");

        manager.RegisterApp("known", new TestAppContext("known"));
        manager.SetDefaultKey("known", requireRegistered: true).Should().BeTrue();

        manager.SetDefaultKey("declared-later", requireRegistered: false).Should().BeTrue(
            "懒加载/稍后注册场景必须能先声明默认键");
        manager.DefaultAppKey.Should().Be("declared-later");
    }

    [Fact]
    public void GetAllApps_ShouldNotMaterializeLazyEntries()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var calls = 0;
        manager.RegisterApp("eager", new TestAppContext("eager"));
        manager.RegisterLazy("lazy", () =>
        {
            Interlocked.Increment(ref calls);
            return new TestAppContext("lazy");
        });

        var all = manager.GetAllApps().ToList();

        all.Should().HaveCount(1, "已定义语义：枚举不触发实例化（避免把'声明'变成副作用）");
        all[0].AppKey.Should().Be("eager");
        calls.Should().Be(0);
    }

    [Fact]
    public void RemoveApp_ShouldAlsoRemoveLazyDeclaration()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterLazy("lazy", () => new TestAppContext("lazy"));

        manager.HasApp("lazy").Should().BeTrue();
        manager.RemoveApp("lazy").Should().BeTrue();

        manager.HasApp("lazy").Should().BeFalse("移除后不得因懒加载声明而'复活'");
        var act = () => manager.GetApp("lazy");
        act.Should().Throw<InvalidOperationException>();
    }

    // ── ③ 既有行为回归 ─────────────────────────────────────────────────

    [Fact]
    public void TrySetDefaultApp_Unregistered_ShouldStillReturnFalse()
    {
        var manager = new DefaultAppManager<IMudAppContext>();

        manager.TrySetDefaultApp("nope").Should().BeFalse("默认实现语义不变（必须已注册）");
    }

    [Fact]
    public void RegisterApp_WithIsDefault_ShouldSetDefaultKey()
    {
        var manager = new DefaultAppManager<IMudAppContext>();

        manager.RegisterApp("app-A", new TestAppContext("app-A"), isDefault: true);

        manager.DefaultAppKey.Should().Be("app-A");
        manager.GetDefaultApp().AppKey.Should().Be("app-A");
    }

    [Fact]
    public async Task RegisterAppAsync_ShouldStillInitializeAndRegister()
    {
        var manager = new DefaultAppManager<IMudAppContext>();

        await manager.RegisterAppAsync("app-A", new TestAppContext("app-A"), isDefault: true);

        manager.HasApp("app-A").Should().BeTrue();
        manager.GetApp("app-A").AppKey.Should().Be("app-A");
    }
}
