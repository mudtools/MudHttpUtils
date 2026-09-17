// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Diagnostics.Metrics;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// DefaultHttpRequestExecutor 的单元测试。
/// 覆盖 SendAndDeserializeAsync / SendAsResponseAsync / SendAsync / ExecuteAsync 的核心场景。
/// </summary>
public class DefaultHttpRequestExecutorTests
{
    private static readonly Uri TestUri = new("https://api.example.com/test");

    private static HttpRequestMessage CreateRequest() =>
        new(HttpMethod.Get, TestUri);

    private static HttpResponseMessage CreateResponse(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string content = "{}",
        string contentType = "application/json")
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, contentType)
        };
        return response;
    }

    private static ResponseDescriptor JsonDescriptor(bool allowAnyStatusCode = false, bool isVoid = false, bool enableDecrypt = false) =>
        new()
        {
            AllowAnyStatusCode = allowAnyStatusCode,
            IsVoidReturn = isVoid,
            EnableDecrypt = enableDecrypt,
            ResponseContentType = "application/json"
        };

    private static ResponseDescriptor XmlDescriptor<T>()
    {
        var descriptor = new ResponseDescriptor
        {
            ResponseContentType = "application/xml"
        };
        return descriptor;
    }

    #region 构造函数

    [Fact]
    public void Constructor_WithNullLogger_ShouldCreateInstanceWithNullLogger()
    {
        var executor = new DefaultHttpRequestExecutor(null!);
        executor.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithOnlyLogger_ShouldCreateInstance()
    {
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);
        executor.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithAllDependencies_ShouldCreateInstance()
    {
        var mockCache = new Mock<IHttpResponseCache>();
        var mockResolver = new Mock<IResiliencePolicyResolver>();

        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance, mockCache.Object, mockResolver.Object);

        executor.Should().NotBeNull();
    }

    #endregion

    #region SendAndDeserializeAsync

    [Fact]
    public async Task SendAndDeserializeAsync_Success_ShouldReturnDeserializedResult()
    {
        var json = """{"Id":42,"Name":"Alice"}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: json));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(), null);

        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.Name.Should().Be("Alice");
    }

    [Fact]
    public async Task SendAndDeserializeAsync_ErrorStatusCode_ShouldThrowApiException()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError, content: "server error"));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var act = () => executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(), null);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ex.Which.Content.Should().Be("server error");
    }

    [Fact]
    public async Task SendAndDeserializeAsync_AllowAnyStatusCode_WithError_ShouldNotThrow()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.BadRequest, content: """{"Id":0}"""));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(allowAnyStatusCode: true), null);

        result.Should().NotBeNull();
        result!.Id.Should().Be(0);
    }

    [Fact]
    public async Task SendAndDeserializeAsync_VoidReturn_ShouldReturnDefault()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: """{"ignored":true}"""));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(isVoid: true), null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendAndDeserializeAsync_StringReturn_ShouldReturnRawString()
    {
        var rawText = "plain text response";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: rawText, contentType: "text/plain"));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var result = await executor.SendAndDeserializeAsync<string>(
            CreateRequest(), mockClient.Object, JsonDescriptor(), null);

        result.Should().Be(rawText);
    }

    [Fact]
    public async Task SendAndDeserializeAsync_WithEncrypt_ShouldDecryptBeforeDeserialize()
    {
        var encrypted = "ENCRYPTED_PAYLOAD";
        var decrypted = """{"Id":7,"Name":"Bob"}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.As<IEncryptableHttpClient>()
            .Setup(c => c.DecryptContent(encrypted))
            .Returns(decrypted);
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: encrypted));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(enableDecrypt: true), null);

        result.Should().NotBeNull();
        result!.Id.Should().Be(7);
        result.Name.Should().Be("Bob");
        mockClient.As<IEncryptableHttpClient>().Verify(c => c.DecryptContent(encrypted), Times.Once);
    }

    [Fact]
    public async Task SendAndDeserializeAsync_StringReturnWithEncrypt_ShouldReturnDecryptedString()
    {
        var encrypted = "ENCRYPTED_TEXT";
        var decrypted = "decrypted text";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.As<IEncryptableHttpClient>()
            .Setup(c => c.DecryptContent(encrypted))
            .Returns(decrypted);
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: encrypted, contentType: "text/plain"));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var result = await executor.SendAndDeserializeAsync<string>(
            CreateRequest(), mockClient.Object, JsonDescriptor(enableDecrypt: true), null);

        result.Should().Be(decrypted);
    }

    [Fact]
    public async Task SendAndDeserializeAsync_XmlResponse_ShouldDeserializeFromXml()
    {
        var xml = """<TestUser><Id>99</Id><Name>Charlie</Name></TestUser>""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: xml, contentType: "application/xml"));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var descriptor = XmlDescriptor<TestUser>();
        descriptor.XmlSerializer = new XmlSerializer(typeof(TestUser));

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), mockClient.Object, descriptor, null);

        result.Should().NotBeNull();
        result!.Id.Should().Be(99);
        result.Name.Should().Be("Charlie");
    }

    [Fact]
    public async Task SendAndDeserializeAsync_InvalidJson_ShouldThrowApiException()
    {
        var invalidJson = """{"Id": not_a_number}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: invalidJson));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var act = () => executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(), null);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.OK);
        ex.Which.Content.Should().Contain("Failed to deserialize JSON response");
    }

    [Fact]
    public async Task SendAndDeserializeAsync_InvalidJson_LargeBody_RawContentLimited_H1()
    {
        // M4-H-1：反序列化失败路径嵌入异常消息的 raw content 必须受 MaxExceptionContentLength 限量
        // （默认 10240），避免超长成功响应体被原样塞入异常消息导致内存/日志膨胀。T-1.x 验收。
        var bigInvalidJson = new string('{', 50 * 1024); // 结构非法但超长的响应体
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: bigInvalidJson));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var ex = await FluentActions.Awaiting(() =>
            executor.SendAndDeserializeAsync<TestUser>(
                CreateRequest(), mockClient.Object, JsonDescriptor(), null))
            .Should().ThrowAsync<ApiException>();

        ex.Which.Content.Should().Contain("Failed to deserialize JSON response");
        var limit = HttpExecutionConstants.DefaultMaxExceptionContentLength;
        ex.Which.Content.Should().Contain(
            $"Raw content: {new string('{', limit)}...[已截断]",
            "反序列化失败路径的 raw content 应按上限截断并追加截断后缀（H-1）");
        ex.Which.Content!.Length.Should().BeLessThan(bigInvalidJson.Length);
    }

    [Fact]
    public async Task SendAndDeserializeAsync_NonReplayableContent_CaptureSkipped_H2()
    {
        // M4-H-2：声明为不可重放（IRequestContentReplayHint.IsReplayable=false）的 HttpContent，
        // 捕获请求体时必须跳过，避免读取一次性源流导致后续发送空/截断请求体。T-2.x 验收。
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError, "server-error"));
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            captureRequestContent: true);

        var request = new HttpRequestMessage(HttpMethod.Post, TestUri)
        {
            Content = new NonReplayableContent(),
        };

        var ex = await FluentActions.Awaiting(() =>
            executor.SendAndDeserializeAsync<TestUser>(
                request, mockClient.Object, JsonDescriptor(allowAnyStatusCode: false), null))
            .Should().ThrowAsync<ApiException>();

        ex.Which.RequestContent.Should().BeNull("不可重放内容不应被捕获（H-2 守卫）；回填需为 null");
    }

    [Fact]
    public async Task SendAndDeserializeAsync_ReplayableContent_StillCaptured_H2()
    {
        // M4-H-2 对照组：可重放内容（普通 StringContent）仍正常捕获，守卫不破坏既有行为。
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError, "server-error"));
        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            captureRequestContent: true);

        var request = new HttpRequestMessage(HttpMethod.Post, TestUri)
        {
            Content = new StringContent("""{"name":"x"}""", Encoding.UTF8, "application/json"),
        };

        var ex = await FluentActions.Awaiting(() =>
            executor.SendAndDeserializeAsync<TestUser>(
                request, mockClient.Object, JsonDescriptor(allowAnyStatusCode: false), null))
            .Should().ThrowAsync<ApiException>();

        ex.Which.RequestContent.Should().NotBeNull("可重放内容应正常捕获（H-2 对照组）");
    }

    [Fact]
    public async Task SendAndDeserializeAsync_InvalidXml_ShouldThrowApiException()
    {
        var invalidXml = """<TestUser><Id>not_int</Id></TestUser>""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: invalidXml, contentType: "application/xml"));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var descriptor = XmlDescriptor<TestUser>();
        descriptor.XmlSerializer = new XmlSerializer(typeof(TestUser));

        var act = () => executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), mockClient.Object, descriptor, null);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.Content.Should().Contain("Failed to deserialize XML response");
    }

    #endregion

    #region SendAsResponseAsync

    [Fact]
    public async Task SendAsResponseAsync_Success_ShouldReturnResponseWithContent()
    {
        var json = """{"Id":1,"Name":"Dan"}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: json));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var response = await executor.SendAsResponseAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(), null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.IsSuccessStatusCode.Should().BeTrue();
        response.Content.Should().NotBeNull();
        response.Content!.Id.Should().Be(1);
        response.ErrorContent.Should().BeNull();
    }

    [Fact]
    public async Task SendAsResponseAsync_ErrorStatusCode_ShouldReturnResponseWithErrorContent()
    {
        var errorBody = """{"error":"bad request"}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.BadRequest, content: errorBody));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var response = await executor.SendAsResponseAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(), null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.IsSuccessStatusCode.Should().BeFalse();
        response.Content.Should().BeNull();
        response.ErrorContent.Should().Be(errorBody);
    }

    [Fact]
    public async Task SendAsResponseAsync_StringReturn_ShouldReturnResponseWithString()
    {
        var rawText = "hello world";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: rawText, contentType: "text/plain"));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var response = await executor.SendAsResponseAsync<string>(
            CreateRequest(), mockClient.Object, JsonDescriptor(), null);

        response.Content.Should().Be(rawText);
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsResponseAsync_WithDecrypt_ShouldDecryptContent()
    {
        var encrypted = "ENC";
        var decrypted = """{"Id":5,"Name":"Eve"}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.As<IEncryptableHttpClient>()
            .Setup(c => c.DecryptContent(encrypted))
            .Returns(decrypted);
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: encrypted));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var response = await executor.SendAsResponseAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(enableDecrypt: true), null);

        response.Content!.Id.Should().Be(5);
        response.Content.Name.Should().Be("Eve");
    }

    [Fact]
    public async Task SendAsResponseAsync_InvalidJson_ShouldReturnErrorResponse()
    {
        var invalidJson = """{invalid}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: invalidJson));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var response = await executor.SendAsResponseAsync<TestUser>(
            CreateRequest(), mockClient.Object, JsonDescriptor(), null);

        response.IsSuccessStatusCode.Should().BeTrue();
        response.Content.Should().BeNull();
        response.ErrorContent.Should().Contain("Failed to deserialize JSON response");
    }

    #endregion

    #region SendAsync (void)

    [Fact]
    public async Task SendAsync_Success_ShouldNotThrow()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse());
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        await executor.SendAsync(CreateRequest(), mockClient.Object, JsonDescriptor());
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ErrorStatusCode_ShouldThrowApiException()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError, "error"));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var act = () => executor.SendAsync(CreateRequest(), mockClient.Object, JsonDescriptor());
        await act.Should().ThrowAsync<ApiException>();
    }

    [Fact]
    public async Task SendAsync_AllowAnyStatusCode_ShouldNotThrow()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.BadRequest, "error"));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        await executor.SendAsync(CreateRequest(), mockClient.Object, JsonDescriptor(allowAnyStatusCode: true));
    }

    #endregion

    #region ExecuteAsync<TResult>

    [Fact]
    public async Task ExecuteAsync_DirectNoCacheNoResilience_ShouldCallSendAndDeserializeDirectly()
    {
        var json = """{"Id":10,"Name":"Frank"}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: json));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = null,
            Resilience = null
        };

        var result = await executor.ExecuteAsync<TestUser>(
            CreateRequest(), mockClient.Object, descriptor, null);

        result!.Id.Should().Be(10);
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithCache_CacheMiss_ShouldCallUnderlyingAndCacheResult()
    {
        var json = """{"Id":20,"Name":"Grace"}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: json));
        var mockCache = new Mock<IHttpResponseCache>();
        TestUser? fetchedValue = null;
        mockCache.Setup(c => c.GetOrFetchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<TestUser?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<TestUser?>>, TimeSpan, bool, CancellationToken>(
                async (key, fetch, exp, sliding, ct) =>
                {
                    fetchedValue = await fetch();
                    return fetchedValue;
                });
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance, mockCache.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = new CacheOptions { DurationSeconds = 60 },
            CacheKey = "test-key"
        };

        var result = await executor.ExecuteAsync<TestUser>(CreateRequest(), mockClient.Object, descriptor, null);

        result!.Id.Should().Be(20);
        mockCache.Verify(c => c.GetOrFetchAsync(
            "test-key", It.IsAny<Func<Task<TestUser?>>>(), TimeSpan.FromSeconds(60),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithCache_CacheHit_ShouldReturnCachedValueWithoutHttpCall()
    {
        var cachedUser = new TestUser { Id = 30, Name = "Cached" };
        var mockClient = new Mock<IBaseHttpClient>();
        var mockCache = new Mock<IHttpResponseCache>();
        mockCache.Setup(c => c.GetOrFetchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<TestUser?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedUser);
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance, mockCache.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = new CacheOptions { DurationSeconds = 60 },
            CacheKey = "hit-key"
        };

        var result = await executor.ExecuteAsync<TestUser>(CreateRequest(), mockClient.Object, descriptor, null);

        result!.Id.Should().Be(30);
        result.Name.Should().Be("Cached");
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithResilience_ShouldResolvePolicyAndExecuteViaWrapper()
    {
        var json = """{"Id":40,"Name":"Heidi"}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: json));
        var mockResolver = new Mock<IResiliencePolicyResolver>();
        var requestTemplateRef = CreateRequest();
        mockResolver.Setup(r => r.ResolvePolicyWrapper<TestUser>(
                It.IsAny<ResilienceExecutionOptions>(), It.IsAny<HttpRequestMessage>()))
            .Returns<ResilienceExecutionOptions, HttpRequestMessage>(
                (options, requestTemplate) =>
                    (coreExecute, ct) => coreExecute(requestTemplate, ct));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance, null, mockResolver.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 2 }
        };

        var result = await executor.ExecuteAsync<TestUser>(requestTemplateRef, mockClient.Object, descriptor, null);

        result!.Id.Should().Be(40);
        mockResolver.Verify(r => r.ResolvePolicyWrapper<TestUser>(
            It.IsAny<ResilienceExecutionOptions>(), It.IsAny<HttpRequestMessage>()), Times.Once);
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CachePlusResilience_CacheMiss_ShouldWrapResilienceInCache()
    {
        var json = """{"Id":50,"Name":"Ivan"}""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: json));
        var mockCache = new Mock<IHttpResponseCache>();
        mockCache.Setup(c => c.GetOrFetchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<TestUser?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<TestUser?>>, TimeSpan, bool, CancellationToken>(
                async (key, fetch, exp, sliding, ct) => await fetch());
        var mockResolver = new Mock<IResiliencePolicyResolver>();
        mockResolver.Setup(r => r.ResolvePolicyWrapper<TestUser>(
                It.IsAny<ResilienceExecutionOptions>(), It.IsAny<HttpRequestMessage>()))
            .Returns<ResilienceExecutionOptions, HttpRequestMessage>(
                (options, requestTemplate) =>
                    (coreExecute, ct) => coreExecute(requestTemplate, ct));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance, mockCache.Object, mockResolver.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = new CacheOptions { DurationSeconds = 30 },
            CacheKey = "combo-key",
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 1 }
        };

        var result = await executor.ExecuteAsync<TestUser>(CreateRequest(), mockClient.Object, descriptor, null);

        result!.Id.Should().Be(50);
        // 缓存应被调用一次（包裹弹性策略）
        mockCache.Verify(c => c.GetOrFetchAsync(
            "combo-key", It.IsAny<Func<Task<TestUser?>>>(), TimeSpan.FromSeconds(30),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        // 弹性策略解析器应被调用一次
        mockResolver.Verify(r => r.ResolvePolicyWrapper<TestUser>(
            It.IsAny<ResilienceExecutionOptions>(), It.IsAny<HttpRequestMessage>()), Times.Once);
        // HTTP 调用应仅一次（弹性策略包装器透传，缓存未命中执行一次）
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CachePlusResilience_CacheHit_ShouldSkipBothResilienceAndHttp()
    {
        var cachedUser = new TestUser { Id = 60, Name = "Judy" };
        var mockClient = new Mock<IBaseHttpClient>();
        var mockCache = new Mock<IHttpResponseCache>();
        mockCache.Setup(c => c.GetOrFetchAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<TestUser?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedUser);
        var mockResolver = new Mock<IResiliencePolicyResolver>();
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance, mockCache.Object, mockResolver.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = new CacheOptions { DurationSeconds = 30 },
            CacheKey = "hit-combo-key",
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 1 }
        };

        var result = await executor.ExecuteAsync<TestUser>(CreateRequest(), mockClient.Object, descriptor, null);

        result!.Id.Should().Be(60);
        // 缓存命中，弹性策略包装器虽然被解析但 fetchFunc 未被调用，因此 HTTP 不会被调用
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region ExecuteAsync (void)

    [Fact]
    public async Task ExecuteAsync_VoidNoResilience_ShouldCallSendAsyncDirectly()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse());
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(isVoid: true),
            Resilience = null
        };

        await executor.ExecuteAsync(CreateRequest(), mockClient.Object, descriptor);

        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_VoidWithResilience_ShouldApplyResilienceWrapper()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse());
        var mockResolver = new Mock<IResiliencePolicyResolver>();
        mockResolver.Setup(r => r.ResolvePolicyWrapper<object>(
                It.IsAny<ResilienceExecutionOptions>(), It.IsAny<HttpRequestMessage>()))
            .Returns<ResilienceExecutionOptions, HttpRequestMessage>(
                (options, requestTemplate) =>
                    (coreExecute, ct) => coreExecute(requestTemplate, ct));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance, null, mockResolver.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(isVoid: true),
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 2 }
        };

        await executor.ExecuteAsync(CreateRequest(), mockClient.Object, descriptor);

        mockResolver.Verify(r => r.ResolvePolicyWrapper<object>(
            It.IsAny<ResilienceExecutionOptions>(), It.IsAny<HttpRequestMessage>()), Times.Once);
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_VoidWithResilienceButNullResolver_ShouldFallBackToDirectSend()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse());
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance, null, null);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(isVoid: true),
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 2 }
        };

        await executor.ExecuteAsync(CreateRequest(), mockClient.Object, descriptor);

        // 无 resolver 时，应回退到直接 SendAsync
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DownloadAsync / DownloadLargeAsync

    [Fact]
    public async Task DownloadAsync_ShouldDelegateToHttpClient()
    {
        var data = new byte[] { 1, 2, 3 };
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var result = await executor.DownloadAsync(CreateRequest(), mockClient.Object);

        result.Should().Equal(data);
    }

    [Fact]
    public async Task DownloadLargeAsync_ShouldDelegateToHttpClient()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var tempFile = Path.Combine(Path.GetTempPath(), $"mud_test_{Guid.NewGuid():N}.bin");
        try
        {
            await executor.DownloadLargeAsync(CreateRequest(), mockClient.Object, tempFile);

            // 验证执行器调用了 SendRawAsync
            mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
            // 验证文件内容正确
            File.Exists(tempFile).Should().BeTrue();
            (await File.ReadAllBytesAsync(tempFile)).Should().Equal(data);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DownloadLargeAsync_WithProgress_ShouldReportBytesWritten()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var tempFile = Path.Combine(Path.GetTempPath(), $"mud_test_{Guid.NewGuid():N}.bin");
        var reportedBytes = new List<long>();
        var progress = new RecordingProgress(reportedBytes);
        try
        {
            await executor.DownloadLargeAsync(CreateRequest(), mockClient.Object, tempFile, progress: progress);

            // 验证进度报告至少触发一次，且最终累计等于数据长度
            reportedBytes.Should().NotBeEmpty();
            reportedBytes.Last().Should().Be(data.Length);
            File.Exists(tempFile).Should().BeTrue();
            (await File.ReadAllBytesAsync(tempFile)).Should().Equal(data);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DownloadLargeAsync_WithResilience_ShouldApplyResilienceWrapperAndWriteFile()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6 };
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var mockResolver = new Mock<IResiliencePolicyResolver>();
        mockResolver.Setup(r => r.ResolvePolicyWrapper<object>(
                It.IsAny<ResilienceExecutionOptions>(), It.IsAny<HttpRequestMessage>()))
            .Returns<ResilienceExecutionOptions, HttpRequestMessage>(
                (options, requestTemplate) =>
                    (coreExecute, ct) => coreExecute(requestTemplate, ct));
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance, null, mockResolver.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(isVoid: true),
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 3 }
        };

        var tempFile = Path.Combine(Path.GetTempPath(), $"mud_test_{Guid.NewGuid():N}.bin");
        try
        {
            await executor.DownloadLargeAsync(CreateRequest(), mockClient.Object, tempFile, true, 81920, descriptor);

            mockResolver.Verify(r => r.ResolvePolicyWrapper<object>(
                It.IsAny<ResilienceExecutionOptions>(), It.IsAny<HttpRequestMessage>()), Times.Once);
            mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
            File.Exists(tempFile).Should().BeTrue();
            (await File.ReadAllBytesAsync(tempFile)).Should().Equal(data);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task DownloadLargeAsync_WithResilienceButNullResolver_ShouldFallBackToDirectSend()
    {
        var data = new byte[] { 7, 8, 9 };
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(isVoid: true),
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 3 }
        };

        var tempFile = Path.Combine(Path.GetTempPath(), $"mud_test_{Guid.NewGuid():N}.bin");
        try
        {
            await executor.DownloadLargeAsync(CreateRequest(), mockClient.Object, tempFile, true, 81920, descriptor);

            mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
            File.Exists(tempFile).Should().BeTrue();
            (await File.ReadAllBytesAsync(tempFile)).Should().Equal(data);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    #endregion

    #region Download Observability

    [Fact]
    public async Task DownloadAsync_RecordsDownloadBytesAndDurationMetrics()
    {
        var data = new byte[] { 1, 2, 3 };
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var capturedBytes = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        var capturedDurations = new List<(double Value, KeyValuePair<string, object?>[] Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "mud.http.download.bytes")
                capturedBytes.Add((value, tags.ToArray()));
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "mud.http.download.duration")
                capturedDurations.Add((value, tags.ToArray()));
        });
        meterListener.Start();

        var result = await executor.DownloadAsync(CreateRequest(), mockClient.Object);

        result.Should().Equal(data);
        // 验证下载字节数指标
        capturedBytes.Should().ContainSingle();
        capturedBytes[0].Value.Should().Be(3);
        capturedBytes[0].Tags.Should().Contain(t => t.Key == "outcome" && (string?)t.Value == "success");
        // 验证下载耗时指标
        capturedDurations.Should().ContainSingle();
        capturedDurations[0].Value.Should().BeGreaterThanOrEqualTo(0);
        capturedDurations[0].Tags.Should().Contain(t => t.Key == "outcome" && (string?)t.Value == "success");
    }

    [Fact]
    public async Task DownloadLargeAsync_RecordsDownloadBytesAndDurationMetrics()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var tempFile = Path.Combine(Path.GetTempPath(), $"mud_test_{Guid.NewGuid():N}.bin");
        var capturedBytes = new List<long>();
        var capturedDurations = new List<double>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.download.bytes")
                capturedBytes.Add(value);
        });
        meterListener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name == "mud.http.download.duration")
                capturedDurations.Add(value);
        });
        meterListener.Start();

        try
        {
            await executor.DownloadLargeAsync(CreateRequest(), mockClient.Object, tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }

        // 验证下载字节数指标（等于文件大小 = 5）
        capturedBytes.Should().ContainSingle().Which.Should().Be(5);
        // 验证下载耗时指标
        capturedDurations.Should().ContainSingle().Which.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task DownloadAsync_RecordsErrorMetric_OnDownloadFailure()
    {
        // 使用会抛出异常的 HttpContent 模拟下载失败
        var mockClient = new Mock<IBaseHttpClient>();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingByteArrayContent()
        };
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var capturedDurations = new List<(double Value, KeyValuePair<string, object?>[] Tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MudHttpMeter.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "mud.http.download.duration")
                capturedDurations.Add((value, tags.ToArray()));
        });
        meterListener.Start();

        var act = async () => await executor.DownloadAsync(CreateRequest(), mockClient.Object);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // 验证失败时记录了 outcome=error 的耗时指标
        capturedDurations.Should().ContainSingle();
        capturedDurations[0].Value.Should().BeGreaterThanOrEqualTo(0);
        capturedDurations[0].Tags.Should().Contain(t => t.Key == "outcome" && (string?)t.Value == "error");
    }

    #region M1-#16.2 异常擦除器在执行器路径被调用

    [Fact]
    public async Task SendAndDeserializeAsync_NonSuccess_InvokesExceptionRedactor_OnExecutorPath()
    {
        // T-16.2：生成代码路径（DefaultHttpRequestExecutor）抛 ApiException 前调用 IExceptionRedactor，
        // 与内置方法路径（EnhancedHttpClient.Diag_CaptureFlow）行为一致；且 ApiException.RequestUri
        // 保留完整 URI（#5.4），RequestContent 受 MaxExceptionContentLength 约束（#16）。
        ApiException? seen = null;
        var redactor = new DelegateExceptionRedactor(ex => seen = ex);

        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError, "err-body"));

        var executor = new DefaultHttpRequestExecutor(
            NullLogger<DefaultHttpRequestExecutor>.Instance,
            exceptionRedactor: redactor,
            captureRequestContent: true);

        var request = new HttpRequestMessage(HttpMethod.Post, TestUri)
        {
            Content = new StringContent("""{"name":"x"}""", Encoding.UTF8, "application/json"),
        };

        var act = () => executor.SendAndDeserializeAsync<TestUser>(
            request, mockClient.Object, JsonDescriptor(allowAnyStatusCode: false), null);

        (await act.Should().ThrowAsync<ApiException>()).Which.Content.Should().Be("err-body");

        seen.Should().NotBeNull("执行器路径应在抛出前调用 IExceptionRedactor");
        seen!.Content.Should().Be("err-body");
        seen.RequestContent.Should().NotBeNull("CaptureRequestContent=true 时应回填请求体");
        seen.RequestUri.Should().Be(TestUri.ToString(), "ApiException.RequestUri 保留完整 URI（#5.4）");
    }

    #endregion

    #region M1-N-1 两条路径错误响应体默认上限一致

    [Fact]
    public async Task SendAndDeserializeAsync_NonSuccess_DefaultLimit_MatchesEnhancedClientPath_T1_2()
    {
        // T-1.2 / N-1：生成代码路径（DefaultHttpRequestExecutor）默认 maxExceptionContentLength = null → 10240，
        // 与内置方法路径（EnhancedHttpClient.ErrorContent_DefaultLimit_AppliesWithoutExplicitConfig）结果长度一致。
        var bigError = new string('x', 50 * 1024);
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError, bigError));

        var executor = new DefaultHttpRequestExecutor(NullLogger<DefaultHttpRequestExecutor>.Instance);

        var ex = await FluentActions.Awaiting(() =>
            executor.SendAndDeserializeAsync<TestUser>(
                CreateRequest(), mockClient.Object, JsonDescriptor(allowAnyStatusCode: false), null))
            .Should().ThrowAsync<ApiException>();

        ex.Which.Content.Should().NotBeNull();
        ex.Which.Content!.Length.Should().Be(
            HttpExecutionConstants.DefaultMaxExceptionContentLength + "...[已截断]".Length);
        ex.Which.Content.Should().EndWith("...[已截断]");
    }

    #endregion

    /// <summary>
    /// 自定义 HttpContent，其 SerializeToStreamAsync 抛出异常，用于测试下载失败场景。
    /// HttpContent.ReadAsByteArrayAsync 内部会调用 SerializeToStreamAsync，因此会传播异常。
    /// </summary>
    private sealed class ThrowingByteArrayContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new InvalidOperationException("Simulated download failure"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>
    /// M4-H-2：声明为不可重放（<see cref="IRequestContentReplayHint.IsReplayable"/>=false）的内容，
    /// 捕获请求体时须被守卫跳过，避免消费一次性源流。
    /// </summary>
    private sealed class NonReplayableContent : HttpContent, IRequestContentReplayHint
    {
        bool IRequestContentReplayHint.IsReplayable => false;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var bytes = Encoding.UTF8.GetBytes("non-replayable-body");
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 20;
            return true;
        }
    }

    #endregion

    public sealed class TestUser
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
