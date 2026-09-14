using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Integration.Tests;

/// <summary>
/// <c>Task&lt;Stream&gt;</c> 直达返回的端到端验证（方案 §4.2 / §4.3）。
/// </summary>
/// <remarks>
/// <para>
/// <b>修复背景</b>：此前生成器把 <c>Task&lt;Stream&gt;</c> 交给通用分支
/// （<c>_executor.ExecuteAsync&lt;System.IO.Stream&gt;</c>），把响应体当 JSON 反序列化进 <c>Stream</c>
/// —— 编译通过但运行期必然失败。现改为直达调用 <c>IBaseHttpClient.SendStreamAsync</c>。
/// </para>
/// <para>
/// <b>本测试覆盖</b>：生成的实现类确实返回响应体原始字节；
/// 且"流所有权归调用方"的语义成立 —— Dispose 调用方拿到的流即释放底层 <see cref="HttpResponseMessage"/>/<see cref="HttpContent"/>。
/// </para>
/// </remarks>
public class StreamReturnIntegrationTests : IDisposable
{
    private const string Payload = "stream-payload-0123456789";

    private readonly TestServer _server;
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _services;
    private readonly ResponseTrackingHandler _handler;

    public StreamReturnIntegrationTests()
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
                    endpoints.MapGet("/api/stream/{id:int}", async context =>
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        context.Response.ContentType = "application/octet-stream";
                        await context.Response.WriteAsync(Payload);
                    });
                });
            }));

        // 包一层 Handler，用于观测"响应内容是否随流被释放"。
        _handler = new ResponseTrackingHandler { InnerHandler = _server.CreateHandler() };
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IEnhancedHttpClient>(
            new DirectEnhancedHttpClient(_httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }));
        services.TryAddSingleton<IHttpContentSerializer>(
            sp => HttpContentSerializerFactory.CreateDefault(sp.GetService<IOptions<JsonSerializerOptions>>()?.Value));
        services.TryAddSingleton<IHttpRequestExecutor, DefaultHttpRequestExecutor>();
        services.AddWebApiHttpClient();
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task GeneratedStreamReturn_ReadsRawResponseBody_AndReleasesResponseOnDispose()
    {
        var api = _services.GetRequiredService<IStreamProbeApi>();

        var stream = await api.GetStreamAsync(1);

        string content;
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            content = await reader.ReadToEndAsync();
        }

        content.Should().Be(Payload,
            "Task<Stream> 必须返回原始响应体（修复前会把响应体按 JSON 反序列化进 Stream，运行期失败）");

        _handler.TrackedContents.Should().ContainSingle();
        _handler.TrackedContents[0].Disposed.Should().BeFalse("读取期间响应内容不应被提前释放");

        stream.Dispose();

        _handler.TrackedContents[0].Disposed.Should().BeTrue(
            "流所有权归调用方：Dispose 返回的流即释放底层 HttpResponseMessage/HttpContent");
        // 断言"释放后不可再读"即可：具体异常类型依赖宿主（TestServer 抛 IOException("The client aborted the request.")，
        // 真实网络栈可能抛 ObjectDisposedException），断言具体类型会引入与宿主耦合的脆弱性。
        stream.Invoking(s => s.ReadByte()).Should().Throw<Exception>(
            "释放后的流不可再读（底层响应已随之释放）");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _server.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>拦截响应并把 <see cref="HttpContent"/> 替换为可观测释放的包装。</summary>
    private sealed class ResponseTrackingHandler : DelegatingHandler
    {
        public List<DisposalTrackingContent> TrackedContents { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var tracking = new DisposalTrackingContent(response.Content);
            response.Content = tracking;
            TrackedContents.Add(tracking);

            return response;
        }
    }

    /// <summary>记录自身是否被释放的 <see cref="HttpContent"/> 包装（透传读取）。</summary>
    private sealed class DisposalTrackingContent : HttpContent
    {
        private readonly HttpContent _inner;

        public DisposalTrackingContent(HttpContent inner) => _inner = inner;

        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => _inner.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
            => _inner.ReadAsStreamAsync();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary><c>Task&lt;Stream&gt;</c> 直达返回探针接口（由源生成器生成实现）。</summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IStreamProbeApi
{
    [Get("/api/stream/{id}")]
    Task<Stream> GetStreamAsync([Path] int id, CancellationToken cancellationToken = default);
}
