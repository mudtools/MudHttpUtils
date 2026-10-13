// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio  2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Client.Tests.AppAuth;

/// <summary>
/// F-10：per-app 惰性工厂（<c>RegisterLazy(appKey, Func&lt;IServiceProvider, TAppContext&gt;)</c>）
/// 与 per-app scope / 命名 HttpClient 组合辅助的语义契约。
/// </summary>
public class AppLazyFactoryServiceProviderTests
{
    private sealed class StubAppContext : IMudAppContext
    {
        public StubAppContext(string appKey, object? extra = null)
        {
            AppKey = appKey;
            Extra = extra;
        }

        public string AppKey { get; }
        public object? Extra { get; }
        IEnhancedHttpClient IMudAppContext.HttpClient => throw new NotSupportedException();
        ITokenManager IMudAppContext.GetTokenManager(string tokenType) => throw new NotSupportedException();
        T IMudAppContext.GetTokenManager<T>() where T : class => throw new NotSupportedException();
        T? IMudAppContext.GetService<T>() where T : class => null;
    }

    private sealed class DisposableScopeMarker : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void RegisterLazy_WithServiceProvider_FactoryReceivesProviderAtMaterialization()
    {
        var services = new ServiceCollection();
        services.AddSingleton("di-marker");
        var sp = services.BuildServiceProvider();

        var manager = new DefaultAppManager<IMudAppContext>(sp, externalLookupProvider: null);

        IServiceProvider? received = null;
        manager.RegisterLazy("app-a", provider =>
        {
            received = provider;
            return new StubAppContext("app-a", provider.GetRequiredService<string>());
        });

        // 注册期不得实例化（懒加载语义），因此也不得传入服务提供者。
        received.Should().BeNull("工厂必须在首次命中时才执行");

        var context = manager.GetApp("app-a");

        received.Should().BeSameAs(sp, "工厂在实例化时收到构造管理器注入的服务提供者");
        context.Should().BeOfType<StubAppContext>()
            .Which.Extra.Should().Be("di-marker", "工厂内可经服务提供者解析依赖（DI scope / 命名 HttpClient 组合的基础）");
    }

    [Fact]
    public void RegisterLazy_WithServiceProvider_IsLazy_SingleMaterialization()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var manager = new DefaultAppManager<IMudAppContext>(sp, null);

        var invocations = 0;
        manager.RegisterLazy("app-a", _ =>
        {
            Interlocked.Increment(ref invocations);
            return new StubAppContext("app-a");
        });

        invocations.Should().Be(0);

        manager.GetApp("app-a");
        manager.GetApp("app-a");
        manager.TryGetApp("app-a", out _);

        invocations.Should().Be(1, "懒加载语义：工厂仅调用一次（与无参工厂重载逐位一致）");
    }

    [Fact]
    public void RegisterLazy_WithoutServiceProvider_FailsAtRegistration_FailClosed()
    {
        var manager = new DefaultAppManager<IMudAppContext>();   // 未注入服务提供者

        var act = () => manager.RegisterLazy("app-a", sp => new StubAppContext("app-a"));

        act.Should().Throw<InvalidOperationException>(
            "服务提供者缺失须注册期即报（fail-closed），不得让工厂收到 null 炸在深处");
    }

    [Fact]
    public void CreateAppScope_CreatesResolvableScope_WithLifetimeControl()
    {
        var services = new ServiceCollection();
        services.AddScoped<DisposableScopeMarker>();   // scoped 服务随 scope 释放
        var sp = services.BuildServiceProvider();

        var manager = new DefaultAppManager<IMudAppContext>(sp, null);
        DisposableScopeMarker? marker = null;

        manager.RegisterLazy("app-a", provider =>
        {
            var scope = provider.CreateAppScope();          // F-10 辅助：per-app DI scope
            var resolved = scope.ServiceProvider.GetRequiredService<DisposableScopeMarker>();
            marker = resolved;
            return new StubAppContext("app-a", scope);      // 上下文持有 scope，负责释放
        });

        manager.GetApp("app-a");

        marker.Should().NotBeNull("per-app scope 内可解析 scoped/singleton 服务");

        // 生命周期责任在上下文（持有 scope 的一方），不在管理器。
        var scope = ((StubAppContext)manager.GetApp("app-a")).Extra as IServiceScope;
        scope.Should().NotBeNull();
        scope!.Dispose();

        marker!.Disposed.Should().BeTrue("scope 释放即释放其中的 disposable 服务（上下文生命周期结束时调用）");
    }

    [Fact]
    public void CreateAppScope_NullProvider_Throws()
    {
        var act = () => ((IServiceProvider)null!).CreateAppScope();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetRequiredNamedClient_UsesIHttpClientResolver()
    {
        var sp = new ServiceCollection().BuildServiceProvider();

        // IHttpClientResolver 未注册 → fail-closed 抛 InvalidOperationException。
        var act = () => sp.GetRequiredNamedClient("userApi");
        act.Should().Throw<InvalidOperationException>("未注册 IHttpClientResolver 时命名客户端解析 fail-closed");
    }

    [Fact]
    public void GetRequiredNamedClient_NullArguments_Throw()
    {
        var sp = new ServiceCollection().BuildServiceProvider();

        var actNullProvider = () => ((IServiceProvider)null!).GetRequiredNamedClient("n");
        actNullProvider.Should().Throw<ArgumentNullException>();

        var actNullName = () => sp.GetRequiredNamedClient(null!);
        actNullName.Should().Throw<ArgumentNullException>();
    }
}
