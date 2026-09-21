using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;
using System.Text;
using System.Text.Json;

namespace Mud.HttpUtils.Integration.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// F-01 / F-02 集成回归（generator-review-fix-plan-2026-09-21）。
// F-01 验收 1：跨接口——同路径同名方法同参，默认键首段含接口全名，互不命中。
// F-01 验收 2：跨应用——AsyncLocal 切换应用，键前置 AppKey 前缀，互不命中。
// F-02 验收 3：运行时——服务端按实际收到的 Body 重算 HMAC 签名并比对（签的是就绪请求而非空请求）。
// ─────────────────────────────────────────────────────────────────────────────

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IFooCacheApi
{
    [Get("/api/cache-probe")]
    [Cache(60)]
    Task<string?> GetProbeAsync(CancellationToken cancellationToken = default);
}

[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IBarCacheApi
{
    [Get("/api/cache-probe")]
    [Cache(60)]
    Task<string?> GetProbeAsync(CancellationToken cancellationToken = default);
}

[HttpClientApi]
public interface IAppScopedCacheApi
{
    [Get("/api/cache-probe")]
    [Cache(60)]
    Task<string?> GetProbeAsync(CancellationToken cancellationToken = default);
}

public interface ITestHmacTokenManager
{
    IMudAppContext GetDefaultApp();
    IMudAppContext GetApp(string appKey);
}

[HttpClientApi(TokenManage = "ITestHmacTokenManager")]
public interface IHmacProbeApi
{
    [Post("/api/hmac-probe")]
    [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.HmacSignature)]
    Task<string?> PostProbeAsync([Body] TestModels.CreateUserRequest request, CancellationToken cancellationToken = default);
}

public class CacheIsolationIntegrationTests : IDisposable
{
    private const string HmacSecretKey = "it-test-hmac-secret";

    private readonly TestServer _server;
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _services;

    public CacheIsolationIntegrationTests()
    {
        _server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                // ProbeCounter 由端点从服务端容器解析，必须注册在 WebHostBuilder 侧。
                services.AddSingleton<ProbeCounter>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    // 计数端点：每次真实到达服务端都递增并回传序号，缓存命中则序号不变。
                    endpoints.MapGet("/api/cache-probe", async context =>
                    {
                        var counter = context.RequestServices.GetRequiredService<ProbeCounter>();
                        var n = counter.Increment();
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { seq = n }));
                    });

                    // HMAC 验签端点：以收到的 Method/Path/Query/Body 重建请求并重算签名，
                    // Body 参与签名（修复后语义）→ 匹配；签的是空请求（修复前语义）→ 不匹配。
                    endpoints.MapPost("/api/hmac-probe", async context =>
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();

                        var signature = context.Request.Headers["X-Hmac-Signature"].FirstOrDefault();
                        var reconstructed = new HttpRequestMessage(HttpMethod.Post, context.Request.Path + context.Request.QueryString)
                        {
                            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
                        };
                        var provider = new DefaultHmacSignatureProvider();
                        var isValid = await provider.VerifySignatureAsync(reconstructed, signature ?? string.Empty, HmacSecretKey);

                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync(isValid ? "signature-valid" : "signature-invalid");
                    });
                });
            }));

        _httpClient = _server.CreateClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<ProbeCounter>();
        // App 模式（无 HttpClient/TokenManager）实现类构造必需 IMudAppContext；SwitchTo 由测试用 IAppContextHolder 完成。
        services.AddSingleton<IMudAppContext>(new TestAppContext("cache-app-default", _httpClient));
        services.AddSingleton<IEnhancedHttpClient>(new DirectEnhancedHttpClient(_httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }));
        services.TryAddSingleton<IHttpContentSerializer>(sp => HttpContentSerializerFactory.CreateDefault(sp.GetService<IOptions<JsonSerializerOptions>>()?.Value));
        services.TryAddSingleton<IHttpRequestExecutor, DefaultHttpRequestExecutor>();
        services.AddHttpResponseCache();
        services.AddWebApiHttpClient();
        _services = services.BuildServiceProvider();
        // F-04 工厂化注册后，默认模式接口激活需经 appManager.GetDefaultApp()（DI 注入的默认应用语义），
        // 主容器必须注册默认应用（方法执行上下文仍由各测试通过 IAppContextHolder.SwitchTo 显式设置）。
        _services.GetRequiredService<IAppManager<IMudAppContext>>()
            .RegisterApp("cache-app-default", new TestAppContext("cache-app-default", _httpClient), isDefault: true);
    }

    /// <summary>
    /// F-01 验收 1（跨接口）：IFooApi/IBarApi 同路径同名方法同参，两次调用都必须真实到达服务端。
    /// 修复前默认键为 "{MethodName}|{参数}"，两接口互串（第二次调用命中 foo 的缓存，seq 不再递增）。
    /// </summary>
    [Fact]
    public async Task SameMethodAcrossInterfaces_ShouldNotShareCache()
    {
        var foo = _services.GetRequiredService<IFooCacheApi>();
        var bar = _services.GetRequiredService<IBarCacheApi>();

        var r1 = await foo.GetProbeAsync();
        var r2 = await bar.GetProbeAsync();
        var r3 = await foo.GetProbeAsync(); // 同接口同参 → 应命中缓存（seq 不变）

        var s1 = Seq(r1);
        var s2 = Seq(r2);
        var s3 = Seq(r3);

        s2.Should().Be(s1 + 1, "跨接口调用不得命中 IFoo 的缓存（F-01 层A：键首段含接口全名）");
        s3.Should().Be(s1, "同接口同参第二次调用应命中缓存（缓存本身仍工作）");
    }

    /// <summary>
    /// F-01 验收 2（跨应用）：同一接口，AsyncLocal 切换两个应用调用同参请求，互不命中；
    /// 同应用再次调用命中缓存。
    /// </summary>
    [Fact]
    public async Task SameMethodAcrossApps_ShouldNotShareCache()
    {
        var api = _services.GetRequiredService<IAppScopedCacheApi>();
        // 生成类的 SwitchTo 是类成员而非接口成员；直接操作 DI 中同一 IAppContextHolder 单例
        //（生成实现与 DefaultHttpRequestExecutor 读取的是同一实例）。
        var holder = _services.GetRequiredService<IAppContextHolder>();
        var appA = new TestAppContext("cache-app-a", _httpClient);
        var appB = new TestAppContext("cache-app-b", _httpClient);

        holder.SwitchTo(appA);
        var sA1 = Seq(await api.GetProbeAsync());

        holder.SwitchTo(appB);
        var sB1 = Seq(await api.GetProbeAsync());

        holder.SwitchTo(appA);
        var sA2 = Seq(await api.GetProbeAsync());

        holder.SwitchTo(appB);
        var sB2 = Seq(await api.GetProbeAsync());

        sB1.Should().Be(sA1 + 1, "跨应用调用不得命中 app-a 的缓存（F-01 层B：键前置 AppKey）");
        sA2.Should().Be(sA1, "切回 app-a 同参调用应命中 app-a 的缓存");
        sB2.Should().Be(sB1, "切回 app-b 同参调用应命中 app-b 的缓存");
    }

    /// <summary>
    /// F-02 验收 3（运行时）：服务端以收到的 Body 重建请求并调用 VerifySignatureAsync，
    /// 签名必须可验证——若在 Body 就绪前签名（修复前语义），Body 参与段为空，验证必失败。
    /// </summary>
    [Fact]
    public async Task HmacSignature_ShouldSignReadyRequest_WithBody()
    {
        var appContext = new HmacAppContext("hmac-app", _httpClient);

        var scopeServices = new ServiceCollection();
        scopeServices.AddLogging();
        scopeServices.AddOptions();
        scopeServices.AddSingleton<ProbeCounter>();
        scopeServices.AddSingleton<IEnhancedHttpClient>(new DirectEnhancedHttpClient(_httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }));
        scopeServices.TryAddSingleton<IHttpContentSerializer>(sp => HttpContentSerializerFactory.CreateDefault(null));
        scopeServices.TryAddSingleton<IHttpRequestExecutor, DefaultHttpRequestExecutor>();
        scopeServices.AddHttpResponseCache();
        scopeServices.AddSingleton<ITestHmacTokenManager>(new TestHmacTokenManager(appContext));
        scopeServices.AddSingleton<ITokenProvider>(new StubTokenProvider());
        scopeServices.AddWebApiHttpClient();
        using var provider = scopeServices.BuildServiceProvider();

        var api = provider.GetRequiredService<IHmacProbeApi>();
        var request = new TestModels.CreateUserRequest { Name = "hmac-probe", Email = "hmac@example.com" };

        var response = await api.PostProbeAsync(request);

        response.Should().Be("signature-valid",
            "若在 Body 就绪前签名（修复前语义），Body 参与段为空，服务端按真实 Body 重算的签名必然不匹配");
    }

    /// <summary>
    /// F-04 验收（默认模式注册工厂化）：空容器（仅基础设施 + AddWebApiHttpClient）即可解析默认模式接口。
    /// 未注册默认应用时 fail-closed（异常消息指向注册应用）；RegisterApp(isDefault: true) 后正常解析与调用。
    /// 修复前为裸 AddTransient&lt;IFoo, Impl&gt;，激活时报「无法解析 IMudAppContext」的晦涩 DI 错误。
    /// </summary>
    [Fact]
    public async Task DefaultMode_FactoryRegistration_ShouldResolveFromMinimalContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IEnhancedHttpClient>(new DirectEnhancedHttpClient(_httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }));
        services.TryAddSingleton<IHttpContentSerializer>(sp => HttpContentSerializerFactory.CreateDefault(null));
        services.TryAddSingleton<IHttpRequestExecutor, DefaultHttpRequestExecutor>();
        services.AddHttpResponseCache();
        services.AddWebApiHttpClient();
        using var provider = services.BuildServiceProvider();

        // GetDefaultApp 未注册默认应用 → 工厂 fail-closed，消息指向「注册应用时设置 isDefault」
        var failEx = Record.Exception(() => provider.GetRequiredService<IAppScopedCacheApi>());
        failEx.Should().BeOfType<InvalidOperationException>().Which.Message
            .Should().Contain("isDefault", "未注册默认应用时异常应指向注册应用（F-04 fail-closed 语义）");

        // RegisterApp(isDefault: true) → 正常解析与调用（[Cache] 接口走 cacheProvider=GetRequiredService 路径）
        var appManager = provider.GetRequiredService<IAppManager<IMudAppContext>>();
        var f04App = new TestAppContext("f04-app", _httpClient);
        appManager.RegisterApp("f04-app", f04App, isDefault: true);
        // 默认模式生成方法执行时要求 holder.Current 非空（UseApp/SwitchTo 显式设置），
        // 工厂注入的默认应用仅满足构造与 DI 激活，不充当方法执行上下文。
        provider.GetRequiredService<IAppContextHolder>().SwitchTo(f04App);

        var api = provider.GetRequiredService<IAppScopedCacheApi>();
        Seq(await api.GetProbeAsync()).Should().BeGreaterThan(0);
    }

    private static int Seq(string? json)
        => JsonSerializer.Deserialize<JsonElement>(json!).GetProperty("seq").GetInt32();

    public void Dispose()
    {
        _server.Dispose();
        _httpClient.Dispose();
        (_services as IDisposable)?.Dispose();
    }

    /// <summary>缓存探测计数器（注册为单例，测试内可读）。</summary>
    public sealed class ProbeCounter
    {
        private int _count;
        public int Increment() => Interlocked.Increment(ref _count);
    }

    /// <summary>测试应用上下文：返回共享 HttpClient，GetService 一律为 null。</summary>
    private sealed class TestAppContext(string appKey, HttpClient httpClient) : IMudAppContext
    {
        public string AppKey => appKey;
        public IEnhancedHttpClient HttpClient => new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });
        public ITokenManager GetTokenManager(string tokenType = "") => null!;
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();
        public T? GetService<T>() where T : class => null;
    }

    /// <summary>HMAC 应用上下文：GetService 提供 IHmacSignatureProvider / IApiKeyProvider。</summary>
    private sealed class HmacAppContext(string appKey, HttpClient httpClient) : IMudAppContext
    {
        public string AppKey => appKey;
        public IEnhancedHttpClient HttpClient => new DirectEnhancedHttpClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });
        public ITokenManager GetTokenManager(string tokenType = "") => null!;
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();
        public T? GetService<T>() where T : class
        {
            if (typeof(T) == typeof(IHmacSignatureProvider))
                return (T?)(object)new DefaultHmacSignatureProvider();
            if (typeof(T) == typeof(IApiKeyProvider))
                return (T?)(object)new StaticApiKeyProvider(HmacSecretKey);
            return null;
        }
    }

    private sealed class StaticApiKeyProvider(string key) : IApiKeyProvider
    {
        public Task<string> GetApiKeyAsync(string? keyName = null, CancellationToken cancellationToken = default)
            => Task.FromResult(key);
    }

    /// <summary>HMAC 路径不取访问令牌，此存根仅为满足 DI 构造依赖。</summary>
    private sealed class StubTokenProvider : ITokenProvider
    {
        public Task<string> GetTokenAsync(IMudAppContext appContext, TokenRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("HMAC 签名路径不应调用 ITokenProvider.GetTokenAsync。");
    }

    private sealed class TestHmacTokenManager(IMudAppContext appContext) : ITestHmacTokenManager
    {
        public IMudAppContext GetDefaultApp() => appContext;
        public IMudAppContext GetApp(string appKey) => appContext;
    }
}
