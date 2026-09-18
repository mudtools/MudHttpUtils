// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Client.Tests;   // CollectingLoggerProvider / UpdatableMemoryProvider

namespace Mud.HttpUtils.Tests;

/// <summary>
/// L-6：「只注册不解析」类接线缺陷的回归用例（热更新订阅 + AppManager 诊断出口）。
/// </summary>
/// <remarks>
/// <para>
/// 通用反模式：<c>services.TryAddSingleton&lt;T&gt;(...)</c> 注册了一个**靠构造函数副作用完成接线**的类型，
/// 但容器是惰性的 —— 无人解析 ⇒ 副作用永不发生。此时"已注册"给读者造成"已生效"的错觉。
/// </para>
/// <list type="number">
///   <item><description><c>EnhancedHttpClientFactoryChangeNotifier</c>：未解析 ⇒ 配置热更新订阅未建立 ⇒
///     keyed 命名客户端永不失效。</description></item>
///   <item><description><c>AppManagerDiagnosticsWiring</c>：未解析 ⇒ <c>SubscriberFailed</c> 恒为 null ⇒
///     <c>ConfigurationChanged</c> 订阅者异常被静默吞掉。</description></item>
/// </list>
/// <para>
/// 修复手段是从「创建客户端」这一必经路径强制解析（<c>CreateEnhancedClient</c>），本组用例钉死该行为。
/// </para>
/// </remarks>
public class AppManagementWiringTests
{
    private sealed class FakeAppContext(string appKey) : IMudAppContext
    {
        public string AppKey { get; } = appKey;

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException();

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;
    }

    /// <summary>构造「多客户端配置入口」所需的最小配置（注意 Clients 多一层，与选项模型同构）。</summary>
    private static (UpdatableMemoryProvider Provider, IConfigurationRoot Configuration) BuildConfig(
        string clientName, params (string Key, string Value)[] extra)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"MudHttpClients:Clients:{clientName}:BaseAddress"] = "https://api.example.com",
        };
        foreach (var (key, value) in extra)
            data[$"MudHttpClients:Clients:{clientName}:{key}"] = value;

        var provider = new UpdatableMemoryProvider(data);
        return (provider, new ConfigurationBuilder().Add(provider).Build());
    }

    private static ServiceProvider BuildConfiguredContainer(
        IConfigurationRoot configuration, ILoggerProvider? loggerProvider = null)
    {
        var services = new ServiceCollection();
        if (loggerProvider != null)
            services.AddLogging(b => b.AddProvider(loggerProvider));
        else
            services.AddLogging();

        services.AddMudHttpClientsFromConfiguration(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ChangeNotifier_ShouldBeResolvedOnFirstClientCreation()
    {
        var (_, configuration) = BuildConfig("wired");
        using var sp = BuildConfiguredContainer(configuration);

        _ = sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("wired");

        sp.GetService<EnhancedHttpClientFactoryChangeNotifier>().Should().NotBeNull(
            "MT-03/L-6：配置热更新订阅必须由客户端创建路径强制解析建立 —— 只注册不解析等于永不生效");
    }

    [Fact]
    public void ConfigurationReload_ShouldRebuildNamedClient_AndKeepKeyedPathInSync()
    {
        var (provider, configuration) = BuildConfig("wired", ("AllowCustomBaseUrls", "false"));
        using var sp = BuildConfiguredContainer(configuration);

        var factory = sp.GetRequiredService<IEnhancedHttpClientFactory>();
        var first = factory.CreateClient("wired");

        sp.GetRequiredKeyedService<IEnhancedHttpClient>("wired").Should().BeSameAs(first,
            "L-6：keyed 路径与工厂路径必须同源同实例（单缓存语义，避免生命周期语义分裂）");

        // 触发配置热更新
        provider.Set("MudHttpClients:Clients:wired:AllowCustomBaseUrls", "true");
        configuration.Reload();

        var rebuilt = factory.CreateClient("wired");

        rebuilt.Should().NotBeSameAs(first,
            "MT-03/L-6：配置变更后命名客户端必须真正重建。" +
            "修复前 keyed 注册为 AddKeyedSingleton，容器永久缓存实例，InvalidateAll() 清缓存后仍拿到同一实例，热更新形同虚设");

        sp.GetRequiredKeyedService<IEnhancedHttpClient>("wired").Should().BeSameAs(rebuilt,
            "重建后 keyed 路径必须与工厂路径同步指向新实例");
    }

    [Fact]
    public void ConfigurationReload_ShouldNotRebuildWhenNotInvalidated()
    {
        // 对照组：未发生配置变更时，同一名称必须稳定返回同一实例（避免每次解析重建导致连接/配置抖动）。
        var (_, configuration) = BuildConfig("stable");
        using var sp = BuildConfiguredContainer(configuration);

        var factory = sp.GetRequiredService<IEnhancedHttpClientFactory>();

        factory.CreateClient("stable").Should().BeSameAs(factory.CreateClient("stable"),
            "无配置变更时命名客户端必须保持进程内单例语义");
    }

    [Fact]
    public void AppManagerDiagnosticsWiring_ShouldBeResolved_AndRouteSubscriberFailureToLogger()
    {
        var logProvider = new CollectingLoggerProvider();
        var (_, configuration) = BuildConfig("wired");
        using var sp = BuildConfiguredContainer(configuration, logProvider);

        // 必经路径：创建客户端即完成 AppManagerDiagnostics 接线
        _ = sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("wired");

        sp.GetService<HttpClientServiceCollectionExtensions.AppManagerDiagnosticsWiring>().Should().NotBeNull(
            "L-6：诊断接线类型必须被真正解析，否则 SubscriberFailed 恒为 null、订阅者异常被静默吞掉");

        var subscriberFailed = AppManagerDiagnostics.SubscriberFailed;

        subscriberFailed.Should().NotBeNull(
            "L-6：解析后 SubscriberFailed 必须已挂接（Abstractions 层无日志依赖，由此委托输出诊断）");

        // 直接驱动诊断出口，验证其确实写入 ILogger（不依赖静态被并发测试覆盖的时序）。
        subscriberFailed!(new InvalidOperationException("订阅者故障模拟"), "app-1", AppConfigurationChangeType.Updated);

        logProvider.GetLogRecords(LogLevel.Warning)
            .Should().Contain(
                r => r.Message.Contains("ConfigurationChanged", StringComparison.Ordinal)
                     && r.Message.Contains("app-1", StringComparison.Ordinal),
                "L-6：订阅者异常必须经 ILogger 输出 Warning，且包含 AppKey 与变更类型");
    }

    [Fact]
    public void SubscriberFailure_ShouldNotBreakRegistryStateMachine()
    {
        // 行为契约：订阅者抛异常不得影响注册表状态机（DefaultAppManager 吞异常后走诊断出口）。
        var manager = new DefaultAppManager<IMudAppContext>();
        manager.RegisterApp("app-1", new FakeAppContext("app-1"), isDefault: true);

        manager.ConfigurationChanged += (_, _) => throw new InvalidOperationException("订阅者故障");

        var act = () => manager.RegisterApp("app-1", new FakeAppContext("app-1"));

        act.Should().NotThrow("订阅者故障必须被隔离，不得影响 IAppManager 的状态机");
        manager.HasApp("app-1").Should().BeTrue();
    }
}
