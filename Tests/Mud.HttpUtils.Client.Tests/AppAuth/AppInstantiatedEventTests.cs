// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// U-8：<see cref="DefaultAppManager{TAppContext}.AppInstantiated"/> 生命周期事件 ——
/// 首滚恰好一次 / 处理器内可见已注册上下文 / 异常隔离 / 注册面（RegisterApp）不发布（事件面收敛，
/// Reconfigured/Removed 由 ConfigurationChanged 承载）。
/// </summary>
public class AppInstantiatedEventTests
{
    private sealed class TestAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;
        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();
        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();
        public T? GetService<T>() where T : class => null;
    }

    [Fact]
    public void RegisterApp_Should_Not_Publish_AppInstantiated()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var count = 0;
        manager.AppInstantiated += (_, _) => count++;

        manager.RegisterApp("eager", new TestAppContext("eager"));
        _ = manager.GetApp("eager");

        count.Should().Be(0, "事件面收敛：仅懒加载首滚发布，注册面仍由 ConfigurationChanged 承载");
    }

    [Fact]
    public async Task LazyFirstMaterialization_Should_Publish_ExactlyOnce()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var events = new List<AppInstantiatedEventArgs>();
        manager.AppInstantiated += (_, e) => events.Add(e);
        manager.RegisterLazy("lazy", () => new TestAppContext("lazy"));

        _ = manager.GetApp("lazy");
        _ = manager.TryGetApp("lazy", out _);       // 二次命中走注册表，不再实例化
        await Task.Delay(10);

        events.Should().ContainSingle("同一次实例化恰好发布一次");
        events[0].AppKey.Should().Be("lazy");
        events[0].Context.AppKey.Should().Be("lazy", "处理器内可见刚实例化的上下文");
    }

    [Fact]
    public void AppInstantiated_Should_BeVisible_InHandler_AfterRegistryAdd()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var registeredAtEvent = false;
        manager.AppInstantiated += (_, e) => registeredAtEvent = manager.HasApp(e.AppKey);
        manager.RegisterLazy("lazy", () => new TestAppContext("lazy"));

        _ = manager.GetApp("lazy");

        registeredAtEvent.Should().BeTrue("发布时机严格后于 _registry.Add，处理器内可见已注册上下文");
    }

    [Fact]
    public void SubscriberException_Should_Not_Break_Materialization()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.AppInstantiated += (_, _) => throw new InvalidOperationException("subscriber boom");
        manager.RegisterLazy("lazy", () => new TestAppContext("lazy"));

        var act = () => manager.GetApp("lazy");

        act.Should().NotThrow("订阅者异常隔离（吞异常 + 诊断），不影响实例化结果");
        manager.GetApp("lazy").AppKey.Should().Be("lazy");
    }

    [Fact]
    public void ReregisteredKey_NewLazy_Should_Publish_Again()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        var count = 0;
        manager.AppInstantiated += (_, _) => count++;
        manager.RegisterLazy("k", () => new TestAppContext("k-v1"));
        _ = manager.GetApp("k");

        manager.RemoveApp("k");
        manager.RegisterLazy("k", () => new TestAppContext("k-v2"));
        _ = manager.GetApp("k");

        count.Should().Be(2, "同名键重注册的新 Lazy 首滚同样发布（每次实例化各发一次）");
        manager.GetApp("k").AppKey.Should().Be("k-v2");
    }
}
