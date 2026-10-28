using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// AM2-16 / AM2-06 回归：多应用管理健康检查的数据口径。
/// </summary>
/// <remarks>
/// 两条不变量：① 不得输出 <c>defaultAppKey</c> 原文（Data 会被探针/监控/工单外发，默认应用键往往是租户标识）；
/// ② "是否存在可用应用"必须与启动校验同源（<c>AppManagerProbe</c>），不得各自写一份。
/// </remarks>
public class AppManagementHealthCheckTests
{
    private sealed class FakeAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;
    }

    private static ServiceProvider BuildProvider(IAppManager<IMudAppContext>? manager)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppContextHolder, AsyncLocalAppContextSwitcher>();
        // 注册授权器以隔离本文件关注的"可用应用"判定（缺授权器本身也会让状态 Degraded）。
        services.AddSingleton<IAppAccessAuthorizer, AllowAllAppAccessAuthorizer>();
        if (manager is not null)
            services.AddSingleton(manager);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task HealthCheck_DoesNotExposeAppKey()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("tenant-secret-42", new FakeAppContext("tenant-secret-42"), isDefault: true);

        using var sp = BuildProvider(manager);
        var result = await new AppManagementHealthCheck(sp)
            .CheckHealthAsync(new HealthCheckContext())
            .ConfigureAwait(false);

        result.Data.Should().NotContainKey("defaultAppKey",
            "AM2-16：不得输出默认应用键原文（租户标识）");
        result.Data.Should().ContainKey("hasDefaultApp");
        result.Data["hasDefaultApp"].Should().Be(true);

        var leaked = result.Data.Values
            .OfType<string>()
            .Any(s => s.Contains("tenant-secret-42", StringComparison.Ordinal));
        leaked.Should().BeFalse("任何 Data 值都不得回显应用键");
    }

    [Fact]
    public async Task HealthCheck_LazyOnlyWithDefaultKey_ReportsHasUsableApp()
    {
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterLazy("lazy-app", () => new FakeAppContext("lazy-app"), isDefault: true);

        using var sp = BuildProvider(manager);
        var result = await new AppManagementHealthCheck(sp)
            .CheckHealthAsync(new HealthCheckContext())
            .ConfigureAwait(false);

        result.Data["hasUsableApp"].Should().Be(true,
            "AM2-06/16：仅声明但已设默认应用的懒加载项算可用（与启动校验同源口径）");
        result.Status.Should().NotBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task HealthCheck_EmptyManager_ReportsNoUsableApp()
    {
        using var sp = BuildProvider(new DefaultAppManager<IMudAppContext>());
        var result = await new AppManagementHealthCheck(sp)
            .CheckHealthAsync(new HealthCheckContext())
            .ConfigureAwait(false);

        result.Data["hasUsableApp"].Should().Be(false, "空管理器与「未注册」在可用性上等价");
        result.Status.Should().Be(HealthStatus.Degraded);
    }
}
