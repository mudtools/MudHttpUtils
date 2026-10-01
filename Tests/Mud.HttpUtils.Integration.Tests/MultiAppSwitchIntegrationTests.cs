// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;
using System.Text.Json;

namespace Mud.HttpUtils.Integration.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// SW-01 / SW-14 端到端验收（S7-B）：
//   `IAppScopeSwitcher` 是"按 appKey 切换 + 自动归还上下文"的**抽象面**。
//   生成类（非 HttpClient 模式）已无条件发射 `UseAppScope` / `UseDefaultAppScope`，
//   但这两个方法此前不在任何接口上 —— 本组用例刻意以 `IAppScopeSwitcher` 作为**变量类型**调用，
//   这正是 SW-01 修复前**无法编译**的写法（此前只能通过具体生成类或 `IAppContextSwitcher` 上的旧入口）。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Default 模式 + 显式继承 <see cref="IAppScopeSwitcher"/> 的探测接口：
/// 生成类经接口传递即获得该契约（无需生成器追加），用于验证默认模式下作用域式切换的可用性。
/// </summary>
[HttpClientApi]
public interface IMultiAppScopeApi : IAppScopeSwitcher
{
    [Get("/api/cache-probe")]
    [Cache(60)]
    Task<string?> GetProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// SW-01 / SW-14：`IAppScopeSwitcher` 的端到端行为（作用域切换、自动归还、嵌套、跨应用隔离）。
/// </summary>
public class MultiAppSwitchIntegrationTests : IDisposable
{
    private readonly TestServer _server;
    private readonly HttpClient _httpClient;
    private readonly ServiceProvider _provider;

    public MultiAppSwitchIntegrationTests()
    {
        _server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ProbeCounter>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/api/cache-probe", async context =>
                    {
                        var counter = context.RequestServices.GetRequiredService<ProbeCounter>();
                        var n = counter.Increment();
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { seq = n }));
                    });
                });
            }));

        _httpClient = _server.CreateClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IEnhancedHttpClient>(
            new DirectEnhancedHttpClient(_httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }));
        services.TryAddSingleton<IHttpContentSerializer>(sp => HttpContentSerializerFactory.CreateDefault(null));
        services.TryAddSingleton<IHttpRequestExecutor, DefaultHttpRequestExecutor>();
        services.AddHttpResponseCache();
        // 单应用/受信测试场景：显式声明"放行"意图（未注册授权器时按 appKey 切换默认拒绝）。
        services.AddSingleton<IAppAccessAuthorizer, AllowAllAppAccessAuthorizer>();
        services.AddWebApiHttpClient();

        _provider = services.BuildServiceProvider();

        var appManager = _provider.GetRequiredService<IAppManager<IMudAppContext>>();
        appManager.RegisterApp("scope-app-a", new SwitcherAppContext("scope-app-a", _httpClient), isDefault: true);
        appManager.RegisterApp("scope-app-b", new SwitcherAppContext("scope-app-b", _httpClient));
    }

    /// <summary>
    /// SW-01 核心：`IAppScopeSwitcher` 可作为变量类型使用（赋值 + 调用），
    /// 且作用域内上下文切换为目标应用、释放后自动归还。
    /// </summary>
    [Fact]
    public async Task UseAppScope_ViaInterface_ScopesAndRestoresContext()
    {
        var client = _provider.GetRequiredService<IMultiAppScopeApi>();

        // ★ SW-01：这行赋值是本轮修复的**编译期**证明 —— 修复前生成类未实现任何声明
        //   UseAppScope 的接口，`IAppScopeSwitcher` 也不存在。
        IAppScopeSwitcher switcher = client;

        var holder = _provider.GetRequiredService<IAppContextHolder>();
        var appManager = _provider.GetRequiredService<IAppManager<IMudAppContext>>();
        holder.SwitchTo(appManager.GetApp("scope-app-a"));

        using (switcher.UseAppScope("scope-app-b"))
        {
            holder.Current!.AppKey.Should().Be("scope-app-b",
                "作用域内当前上下文必须是目标应用（UseAppScope 经 IAppManager 解析并切换）");
        }

        holder.Current!.AppKey.Should().Be("scope-app-a",
            "作用域释放后必须自动归还到进入作用域前的上下文（这是 UseAppScope 相对旧入口 UseApp 的核心价值）");
    }

    /// <summary>
    /// 嵌套作用域：内层释放回到外层应用，外层释放回到初始上下文；
    /// 同时以缓存序号证明"不同 appKey 的响应缓存互不命中"（服务端真实到达次数按 app 递增）。
    /// </summary>
    [Fact]
    public async Task NestedUseAppScope_RestoresEachLayer_AndIsolatesAppCache()
    {
        var client = _provider.GetRequiredService<IMultiAppScopeApi>();
        IAppScopeSwitcher switcher = client;
        var holder = _provider.GetRequiredService<IAppContextHolder>();
        var appManager = _provider.GetRequiredService<IAppManager<IMudAppContext>>();
        holder.SwitchTo(appManager.GetApp("scope-app-a"));

        int seqA1;
        using (switcher.UseAppScope("scope-app-a"))
        {
            seqA1 = Seq(await client.GetProbeAsync());

            int seqB1;
            using (switcher.UseAppScope("scope-app-b"))
            {
                holder.Current!.AppKey.Should().Be("scope-app-b", "内层作用域必须生效");
                seqB1 = Seq(await client.GetProbeAsync());

                seqB1.Should().Be(seqA1 + 1,
                    "不同 appKey 的响应缓存必须互不命中（F-01 层B：缓存键前置 AppKey）");
            }

            holder.Current!.AppKey.Should().Be("scope-app-a",
                "内层释放后必须回到外层应用，而不是初始上下文");

            // 切回 app-a 且参数相同 ⇒ 命中 app-a 自己的缓存，服务端不再到达。
            Seq(await client.GetProbeAsync()).Should().Be(seqA1,
                "回到外层应用后同参调用应命中该应用的缓存（证明作用域切换确实改变了缓存键前缀）");
        }

        holder.Current!.AppKey.Should().Be("scope-app-a", "外层释放后必须回到进入前的上下文");
    }

    private static int Seq(string? json)
        => JsonSerializer.Deserialize<JsonElement>(json!).GetProperty("seq").GetInt32();

    public void Dispose()
    {
        _provider.Dispose();
        _httpClient.Dispose();
        _server.Dispose();
    }

    /// <summary>缓存探测计数器（服务端单例）。</summary>
    public sealed class ProbeCounter
    {
        private int _count;
        public int Increment() => Interlocked.Increment(ref _count);
    }

    /// <summary>最小应用上下文：共享 HttpClient，无令牌服务。</summary>
    private sealed class SwitcherAppContext(string appKey, HttpClient httpClient) : IMudAppContext
    {
        public string AppKey => appKey;
        public IEnhancedHttpClient HttpClient =>
            new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });
        public ITokenManager GetTokenManager(string tokenType = "") => null!;
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();
        public T? GetService<T>() where T : class => null;
    }
}
