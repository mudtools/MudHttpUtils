// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// G9：<c>AddAppScopedTokenManagerRegistry&lt;TAppContext&gt;()</c> —— 从
/// <see cref="IAppManager{TAppContext}"/> 派生 per-app 令牌管理器注册表。
/// </summary>
public class AppScopedTokenManagerRegistryTests
{
    private sealed class ProbeAppContext(string appKey, ITokenManager tokenManager) : IMudAppContext
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => tokenManager;

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;
    }

    private sealed class ProbeHolder : IAppContextHolder
    {
        public IMudAppContext? Current { get; init; }

        public void SwitchTo(IMudAppContext? context) => throw new NotSupportedException();

        public IDisposable BeginScope(IMudAppContext context) => throw new NotSupportedException();
    }

    private static Mock<ITokenManager> NewTokenManager(string name)
    {
        var mock = new Mock<ITokenManager>();
        mock.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(name);
        return mock;
    }

    [Fact]
    public void AppScopedRegistry_ShouldResolveByCurrentApp()
    {
        var appTokenManager = NewTokenManager("app-a-token").Object;
        var appContext = new ProbeAppContext("app-A", appTokenManager);

        var services = new ServiceCollection();
        services.AddSingleton<IAppContextHolder>(new ProbeHolder { Current = appContext });
        services.AddSingleton<IAppManager<IMudAppContext>>(sp =>
        {
            var manager = new DefaultAppManager<IMudAppContext>();
            manager.RegisterApp("app-A", appContext);
            return manager;
        });
        services.AddAppScopedTokenManagerRegistry<IMudAppContext>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenManagerRegistry>().Resolve("any-key")
            .Should().BeSameAs(appTokenManager, "按当前 AppKey 解析到该应用的令牌管理器");
    }

    [Fact]
    public void AppScopedRegistry_WithoutCurrentContext_ShouldReturnNull()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppContextHolder>(new ProbeHolder { Current = null });
        services.AddSingleton<IAppManager<IMudAppContext>>(new DefaultAppManager<IMudAppContext>());
        services.AddAppScopedTokenManagerRegistry<IMudAppContext>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenManagerRegistry>().Resolve("k").Should().BeNull(
            "无当前应用上下文时返回 null（由调用方回落），不得抛异常");
    }

    [Fact]
    public void AppScopedRegistry_UnknownAppKey_ShouldReturnNull()
    {
        var appContext = new ProbeAppContext("app-A", NewTokenManager("t").Object);
        var services = new ServiceCollection();
        services.AddSingleton<IAppContextHolder>(new ProbeHolder { Current = appContext });
        // 应用未注册到 AppManager
        services.AddSingleton<IAppManager<IMudAppContext>>(new DefaultAppManager<IMudAppContext>());
        services.AddAppScopedTokenManagerRegistry<IMudAppContext>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenManagerRegistry>().Resolve("k").Should().BeNull(
            "应用未注册时不得抛异常（TryGetApp 而非 GetApp）");
    }

    [Fact]
    public void AppScopedRegistry_ShouldNotOverrideHostRegistry()
    {
        var hostRegistry = new Mock<ITokenManagerRegistry>();
        hostRegistry.Setup(r => r.Resolve(It.IsAny<string>())).Returns(NewTokenManager("host").Object);

        var services = new ServiceCollection();
        services.AddSingleton(hostRegistry.Object);
        services.AddAppScopedTokenManagerRegistry<IMudAppContext>();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITokenManagerRegistry>().Should().BeSameAs(hostRegistry.Object,
            "TryAdd：宿主已注册注册表时以其为准");
    }
}
