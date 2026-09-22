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

namespace Mud.HttpUtils.Integration.Tests;

/// <summary>
/// M6-HC-02 样例接口：消费真实生成器（Generator 以 Analyzer 引用参与本工程编译），
/// 验证 <c>[HeaderCollection]</c> 字典头接线后的端到端行为。
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IHeaderCollectionApi
{
    [Post("/api/headers/capture")]
    Task<string?> CaptureHeadersAsync([HeaderCollection] IDictionary<string, string?> headers, CancellationToken cancellationToken = default);

    [Post("/api/headers/capture")]
    Task<string?> CaptureObjectHeadersAsync([HeaderCollection] IDictionary<string, object?> headers, CancellationToken cancellationToken = default);

    [Post("/api/headers/capture-mixed")]
    Task<string?> CaptureMixedAsync([Header("X-Fixed-Header")] string fixedValue, [HeaderCollection] IDictionary<string, string?> headers, CancellationToken cancellationToken = default);
}

/// <summary>
/// M6-HC-02 / M6-HC-30 端到端回归（真实生成器 + TestServer）：
/// <list type="bullet">
///   <item>[HeaderCollection] 字典逐项真实发射到请求头（接线前静默丢失）；</item>
///   <item>含 CR/LF 的非法项「跳过而非抛出」，其余正常项仍发射（HC-30）；</item>
///   <item>空字典不发射任何动态头，请求正常完成；</item>
///   <item>[Header] 单值与 [HeaderCollection] 字典混用时两者均发射且互不干扰。</item>
/// </list>
/// </remarks>
public class HeaderCollectionEndToEndTests : IDisposable
{
    private readonly TestServer _server;
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, string> _capturedHeaders = new(StringComparer.OrdinalIgnoreCase);

    public HeaderCollectionEndToEndTests()
    {
        _server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapPost("/api/headers/capture", async context =>
                    {
                        CaptureHeaders(context);
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("captured");
                    });

                    endpoints.MapPost("/api/headers/capture-mixed", async context =>
                    {
                        CaptureHeaders(context);
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("captured");
                    });
                });
            }));

        _httpClient = _server.CreateClient();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IEnhancedHttpClient>(new DirectEnhancedHttpClient(_httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }));
        // 注册 IHttpContentSerializer 和 IHttpRequestExecutor（AddWebApiHttpClient 仅注册接口→实现映射，不注册基础设施服务）
        services.TryAddSingleton<IHttpContentSerializer>(sp => HttpContentSerializerFactory.CreateDefault(sp.GetService<IOptions<JsonSerializerOptions>>()?.Value));
        services.TryAddSingleton<IHttpRequestExecutor, DefaultHttpRequestExecutor>();
        services.AddWebApiHttpClient();
        _services = services.BuildServiceProvider();
    }

    private void CaptureHeaders(HttpContext context)
    {
        foreach (var header in context.Request.Headers)
        {
            _capturedHeaders[header.Key] = header.Value.ToString();
        }
    }

    private IHeaderCollectionApi CreateApi() => _services.GetRequiredService<IHeaderCollectionApi>();

    /// <summary>字典项逐项发射：桩服务器收到的请求头须包含全部键值对。</summary>
    [Fact]
    public async Task HeaderCollection_StringValues_EmitsAllEntriesToRequestHeaders()
    {
        var api = CreateApi();

        var result = await api.CaptureHeadersAsync(new Dictionary<string, string?>
        {
            ["X-Tenant"] = "t1",
            ["X-Trace"] = "abc",
        });

        result.Should().Be("captured");
        _capturedHeaders.Should().ContainKey("X-Tenant").WhoseValue.Should().Be("t1");
        _capturedHeaders.Should().ContainKey("X-Trace").WhoseValue.Should().Be("abc");
    }

    /// <summary>
    /// HC-30「跳过而非抛出」：含 CR/LF 的非法项不得出现在请求头（也不得使整体请求失败），
    /// 其余正常项仍发射。
    /// </summary>
    [Fact]
    public async Task HeaderCollection_InvalidCrLfValue_SkipsEntryAndEmitsOthers()
    {
        var api = CreateApi();

        var act = () => api.CaptureHeadersAsync(new Dictionary<string, string?>
        {
            ["X-Ok"] = "fine",
            ["X-Bad"] = "bad\r\nInjected: 1",
        });

        await act.Should().NotThrowAsync("非法项应被生成代码跳过，而非抛出或注入附加头");

        _capturedHeaders.Should().ContainKey("X-Ok").WhoseValue.Should().Be("fine", "正常项必须仍发射");
        _capturedHeaders.Keys.Should().NotContain("X-Bad", "非法项不得发射");
        _capturedHeaders.Keys.Should().NotContain("Injected", "CR/LF 不得注入附加头（头部注入攻击面）");
    }

    /// <summary><c>IDictionary&lt;string, object?&gt;</c> 版本：值经 ToString 发射。</summary>
    [Fact]
    public async Task HeaderCollection_ObjectValues_EmitToStringResults()
    {
        var api = CreateApi();

        await api.CaptureObjectHeadersAsync(new Dictionary<string, object?>
        {
            ["X-Count"] = 42,
            ["X-Tag"] = "beta",
        });

        _capturedHeaders.Should().ContainKey("X-Count").WhoseValue.Should().Be("42");
        _capturedHeaders.Should().ContainKey("X-Tag").WhoseValue.Should().Be("beta");
    }

    /// <summary>空字典：请求正常发送且不发射任何动态头。</summary>
    [Fact]
    public async Task HeaderCollection_EmptyDictionary_SendsRequestWithoutDynamicHeaders()
    {
        var api = CreateApi();

        var result = await api.CaptureHeadersAsync(new Dictionary<string, string?>());

        result.Should().Be("captured");
        _capturedHeaders.Keys.Should().NotContain(k => k.StartsWith("X-", StringComparison.Ordinal),
            "空字典不得发射任何动态头");
    }

    /// <summary>[Header] 单值与 [HeaderCollection] 字典混用：两者均发射且互不干扰。</summary>
    [Fact]
    public async Task HeaderCollection_MixedWithHeaderAttribute_EmitsBoth()
    {
        var api = CreateApi();

        await api.CaptureMixedAsync(
            "fixed-1",
            new Dictionary<string, string?> { ["X-Tenant"] = "t1" });

        _capturedHeaders.Should().ContainKey("X-Fixed-Header").WhoseValue.Should().Be("fixed-1", "[Header] 单值须发射");
        _capturedHeaders.Should().ContainKey("X-Tenant").WhoseValue.Should().Be("t1", "[HeaderCollection] 字典项须发射");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
        (_services as IDisposable)?.Dispose();
    }
}
