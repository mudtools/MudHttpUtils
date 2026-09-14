// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// N-2 回归用例：成功响应体可选守卫 MaxSuccessResponseBytes
// 覆盖 EnhancedHttpClient（JSON / XML / DownloadAsync byte[]）与 DefaultHttpRequestExecutor（生成代码路径）
// 两条路径：Content-Length 预判 + 守卫流读取阶段校验（chunked 无 Content-Length 场景）。

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mud.HttpUtils.Testing;
using Mud.HttpUtils.Tests;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// N-2：成功响应体可选守卫（<c>EnhancedHttpClientOptions.MaxSuccessResponseBytes</c>）。
/// 默认 0 = 不限制；设为正数后超限抛 <see cref="ApiRequestException"/>。
/// </summary>
public class N2SuccessResponseLimitTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    private static (DirectEnhancedHttpClient Client, StubHttp Stub) Create(
        EnhancedHttpClientOptions? options = null)
    {
        var stub = new StubHttp();
        // 白名单域名（经 UrlValidatorFixture 注册）+ https → 通过严格模式校验
        var httpClient = new HttpClient(stub) { BaseAddress = new Uri("https://api.example.com") };
        var client = new DirectEnhancedHttpClient(httpClient, options);
        return (client, stub);
    }

    public void Dispose() => _fixture.RestoreDomains();

    #region JSON 反序列化路径（EnhancedHttpClient）

    [Fact]
    public async Task Json_ContentLengthExceedsLimit_ThrowsApiRequestException()
    {
        // 02 §N-2 验收：MaxSuccessResponseBytes = 1024 + 10 KB 成功响应（已知 Content-Length）→ 预判即抛
        var (client, stub) = Create(new EnhancedHttpClientOptions { MaxSuccessResponseBytes = 1024 });
        var bigBody = new string('1', 10 * 1024); // 数字字面量，避免提前触发 JsonException
        stub.RespondToAnyRequest(HttpStatusCode.OK, bigBody);

        var ex = await FluentActions.Awaiting(() =>
                client.SendAsync<long>(new HttpRequestMessage(HttpMethod.Get, "/api/values")))
            .Should().ThrowAsync<ApiRequestException>();

        ex.Which.Message.Should().Contain("1024");
    }

    [Fact]
    public async Task Json_ChunkedExceedsLimitDuringRead_ThrowsApiRequestException()
    {
        // chunked（无 Content-Length）10 KB 流式成功体 → 守卫流在读取阶段校验
        var (client, stub) = Create(new EnhancedHttpClientOptions { MaxSuccessResponseBytes = 1024 });
        stub.RespondToAnyRequest(HttpStatusCode.OK).WithLazyContent(10 * 1024, chunkSize: 8192, fillByte: (byte)'1');

        var ex = await FluentActions.Awaiting(() =>
                client.SendAsync<long>(new HttpRequestMessage(HttpMethod.Get, "/api/values")))
            .Should().ThrowAsync<ApiRequestException>();

        ex.Which.Message.Should().Contain("1024");
    }

    [Fact]
    public async Task Json_WithinLimit_DeserializesCorrectly()
    {
        // 未超限 → 正常反序列化（守卫不改变成功路径语义）
        var (client, stub) = Create(new EnhancedHttpClientOptions { MaxSuccessResponseBytes = 64 * 1024 });
        var largeArray = "[" + string.Join(",", Enumerable.Repeat("\"item-value-64-characters-long-string-padding-padding!!\"", 200)) + "]";
        stub.RespondToAnyRequest(HttpStatusCode.OK, largeArray);

        var result = await client.SendAsync<string[]>(
            new HttpRequestMessage(HttpMethod.Get, "/api/values"));

        result.Should().HaveCount(200);
    }

    [Fact]
    public async Task Json_ExactlyEqualToLimit_DoesNotThrow()
    {
        // 语义为"超过"才拒绝：读取字节数 == 上限不抛
        var (client, stub) = Create(new EnhancedHttpClientOptions { MaxSuccessResponseBytes = 1024 });
        var body = "\"" + new string('a', 1022) + "\""; // 恰好 1024 字节的合法 JSON 字符串
        stub.RespondToAnyRequest(HttpStatusCode.OK, body);

        var result = await client.SendAsync<string>(
            new HttpRequestMessage(HttpMethod.Get, "/api/values"));

        result.Should().HaveLength(1022);
    }

    [Fact]
    public async Task Json_DefaultZeroLimit_NoGuard()
    {
        // 默认 0 = 不限制（成功路径行为不变，M1 T-1.3 回归）：1 MB chunked JSON 数组照常反序列化
        var (client, stub) = Create();
        var bigJson = "[" + string.Join(",", Enumerable.Repeat("1", 200000)) + "]"; // ≈ 400 KB 合法 JSON
        stub.RespondToAnyRequest(HttpStatusCode.OK)
            .WithStreamContent(() => new MemoryStream(Encoding.UTF8.GetBytes(bigJson)));

        var result = await client.SendAsync<long[]>(
            new HttpRequestMessage(HttpMethod.Get, "/api/values"));

        result.Should().HaveCount(200000);
    }

    #endregion

    #region DownloadAsync（byte[] 全量缓冲路径）

    [Fact]
    public async Task Download_ContentLengthExceedsLimit_ThrowsApiRequestException()
    {
        var (client, stub) = Create(new EnhancedHttpClientOptions { MaxSuccessResponseBytes = 1024 });
        var bigBody = new string('x', 10 * 1024);
        stub.RespondToAnyRequest(HttpStatusCode.OK, bigBody);

        var ex = await FluentActions.Awaiting(() =>
                client.DownloadAsync(new HttpRequestMessage(HttpMethod.Get, "/api/file")))
            .Should().ThrowAsync<ApiRequestException>();

        ex.Which.Message.Should().Contain("1024");
    }

    [Fact]
    public async Task Download_WithinLimit_ReturnsFullBytes()
    {
        var (client, stub) = Create(new EnhancedHttpClientOptions { MaxSuccessResponseBytes = 64 * 1024 });
        var bigBody = new string('x', 10 * 1024);
        stub.RespondToAnyRequest(HttpStatusCode.OK, bigBody);

        var result = await client.DownloadAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/file"));

        result.Should().HaveCount(10 * 1024);
    }

    #endregion

    #region XML 反序列化路径（EnhancedHttpClient）

    [Fact]
    public async Task Xml_ExceedsLimit_ThrowsApiRequestException()
    {
        var (client, stub) = Create(new EnhancedHttpClientOptions { MaxSuccessResponseBytes = 1024 });
        var bigBody = new string('x', 10 * 1024);
        stub.RespondToAnyRequest(HttpStatusCode.OK, bigBody, contentType: "text/xml");

        var ex = await FluentActions.Awaiting(() =>
                client.SendXmlAsync<XmlItem>(new HttpRequestMessage(HttpMethod.Get, "/api/xml")))
            .Should().ThrowAsync<ApiRequestException>();

        ex.Which.Message.Should().Contain("1024");
    }

    [Fact]
    public async Task Xml_WithinLimit_DeserializesCorrectly()
    {
        // 守卫模式下经 StreamContent 读取并透传 Content-Type（charset 语义不变）
        var (client, stub) = Create(new EnhancedHttpClientOptions { MaxSuccessResponseBytes = 64 * 1024 });
        stub.RespondToAnyRequest(HttpStatusCode.OK, "<XmlItem><Value>42</Value></XmlItem>", contentType: "text/xml");

        var result = await client.SendXmlAsync<XmlItem>(
            new HttpRequestMessage(HttpMethod.Get, "/api/xml"));

        result.Should().NotBeNull();
        result!.Value.Should().Be(42);
    }

    #endregion

    #region DefaultHttpRequestExecutor（生成代码路径）

    private static HttpResponseMessage ExecutorResponse(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Executor_ContentLengthExceedsLimit_ThrowsApiRequestException()
    {
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            maxSuccessResponseBytes: 1024);
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutorResponse(new string('1', 10 * 1024)));

        var ex = await FluentActions.Awaiting(() => executor.SendAndDeserializeAsync<long>(
                new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/test")),
                mockClient.Object,
                new ResponseDescriptor { ResponseContentType = "application/json" },
                null))
            .Should().ThrowAsync<ApiRequestException>();

        ex.Which.Message.Should().Contain("1024");
    }

    [Fact]
    public async Task Executor_WithinLimit_DeserializesCorrectly()
    {
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            maxSuccessResponseBytes: 64 * 1024);
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutorResponse("""{"Id":42,"Name":"Alice"}"""));

        var result = await executor.SendAndDeserializeAsync<ExecutorUser>(
            new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/test")),
            mockClient.Object,
            new ResponseDescriptor { ResponseContentType = "application/json" },
            null);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
    }

    [Fact]
    public async Task Executor_DefaultZeroLimit_NoGuard()
    {
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);
        var mockClient = new Mock<IBaseHttpClient>();
        // 10 KB 合法 JSON（未启用守卫 → 全量读取并反序列化）
        var bigJson = "{\"Id\":42,\"Name\":\"" + new string('a', 10 * 1024) + "\"}";
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutorResponse(bigJson));

        var result = await executor.SendAndDeserializeAsync<ExecutorUser>(
            new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/test")),
            mockClient.Object,
            new ResponseDescriptor { ResponseContentType = "application/json" },
            null);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
    }

    [Fact]
    public async Task Executor_DownloadAsync_ContentLengthExceedsLimit_ThrowsApiRequestException()
    {
        // N-2：executor byte[] 下载路径（DownloadAsync）Content-Length 预判
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            maxSuccessResponseBytes: 1024);
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[10 * 1024])
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var ex = await FluentActions.Awaiting(() => executor.DownloadAsync(
                new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/file")),
                mockClient.Object))
            .Should().ThrowAsync<ApiRequestException>();

        ex.Which.Message.Should().Contain("1024");
    }

    [Fact]
    public async Task Executor_DownloadAsync_ChunkedExceedsLimitDuringRead_ThrowsApiRequestException()
    {
        // N-2：executor byte[] 下载路径 —— chunked（无 Content-Length）超限由守卫流在读取阶段校验
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            maxSuccessResponseBytes: 1024);
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            // 无 Content-Length 的流式内容（chunked 语义）—— 只能靠守卫流读取阶段拦截
            Content = new StreamContent(new MemoryStream(Enumerable.Repeat((byte)'x', 10 * 1024).ToArray()))
        };
        response.Content.Headers.ContentLength = null;
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var ex = await FluentActions.Awaiting(() => executor.DownloadAsync(
                new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/file")),
                mockClient.Object))
            .Should().ThrowAsync<ApiRequestException>();

        ex.Which.Message.Should().Contain("1024");
    }

    [Fact]
    public async Task Executor_DownloadAsync_WithinLimit_ReturnsFullBytes()
    {
        // N-2：executor byte[] 下载路径 —— 未超限时守卫不改变语义
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            maxSuccessResponseBytes: 64 * 1024);
        var mockClient = new Mock<IBaseHttpClient>();
        var data = new byte[10 * 1024];
        new Random(42).NextBytes(data);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await executor.DownloadAsync(
            new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/file")),
            mockClient.Object);

        result.Should().Equal(data);
    }

    [Fact]
    public async Task DiWiring_OptionFlowsToExecutor()
    {
        // DI 装配：IOptions<EnhancedHttpClientOptions>.MaxSuccessResponseBytes → DefaultHttpRequestExecutor 守卫
        var services = new ServiceCollection();
        services.Configure<EnhancedHttpClientOptions>(o => o.MaxSuccessResponseBytes = 1024);
        services.AddMudHttpClient("n2-di-test");
        var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IHttpRequestExecutor>();

        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExecutorResponse(new string('1', 10 * 1024)));

        var ex = await FluentActions.Awaiting(() => executor.SendAndDeserializeAsync<long>(
                new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.example.com/test")),
                mockClient.Object,
                new ResponseDescriptor { ResponseContentType = "application/json" },
                null))
            .Should().ThrowAsync<ApiRequestException>();

        ex.Which.Message.Should().Contain("1024");
    }

    #endregion

    public sealed class ExecutorUser
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class XmlItem
    {
        public int Value { get; set; }
    }
}
