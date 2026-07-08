// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Xml.Serialization;

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
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        var act = () => new DefaultHttpRequestExecutor(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithOnlyHttpClient_ShouldCreateInstance()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);
        executor.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithAllDependencies_ShouldCreateInstance()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        var mockCache = new Mock<IHttpResponseCache>();
        var mockResolver = new Mock<IResiliencePolicyResolver>();

        var executor = new DefaultHttpRequestExecutor(
            mockClient.Object, mockCache.Object, mockResolver.Object);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), JsonDescriptor(), null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var act = () => executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), JsonDescriptor(), null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), JsonDescriptor(allowAnyStatusCode: true), null);

        result.Should().NotBeNull();
        result!.Id.Should().Be(0);
    }

    [Fact]
    public async Task SendAndDeserializeAsync_VoidReturn_ShouldReturnDefault()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: """{"ignored":true}"""));
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), JsonDescriptor(isVoid: true), null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SendAndDeserializeAsync_StringReturn_ShouldReturnRawString()
    {
        var rawText = "plain text response";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: rawText, contentType: "text/plain"));
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var result = await executor.SendAndDeserializeAsync<string>(
            CreateRequest(), JsonDescriptor(), null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), JsonDescriptor(enableDecrypt: true), null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var result = await executor.SendAndDeserializeAsync<string>(
            CreateRequest(), JsonDescriptor(enableDecrypt: true), null);

        result.Should().Be(decrypted);
    }

    [Fact]
    public async Task SendAndDeserializeAsync_XmlResponse_ShouldDeserializeFromXml()
    {
        var xml = """<TestUser><Id>99</Id><Name>Charlie</Name></TestUser>""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: xml, contentType: "application/xml"));
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var descriptor = XmlDescriptor<TestUser>();
        descriptor.XmlSerializer = new XmlSerializer(typeof(TestUser));

        var result = await executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), descriptor, null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var act = () => executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), JsonDescriptor(), null);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.OK);
        ex.Which.Content.Should().Contain("Failed to deserialize JSON response");
    }

    [Fact]
    public async Task SendAndDeserializeAsync_InvalidXml_ShouldThrowApiException()
    {
        var invalidXml = """<TestUser><Id>not_int</Id></TestUser>""";
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(content: invalidXml, contentType: "application/xml"));
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var descriptor = XmlDescriptor<TestUser>();
        descriptor.XmlSerializer = new XmlSerializer(typeof(TestUser));

        var act = () => executor.SendAndDeserializeAsync<TestUser>(
            CreateRequest(), descriptor, null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var response = await executor.SendAsResponseAsync<TestUser>(
            CreateRequest(), JsonDescriptor(), null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var response = await executor.SendAsResponseAsync<TestUser>(
            CreateRequest(), JsonDescriptor(), null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var response = await executor.SendAsResponseAsync<string>(
            CreateRequest(), JsonDescriptor(), null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var response = await executor.SendAsResponseAsync<TestUser>(
            CreateRequest(), JsonDescriptor(enableDecrypt: true), null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var response = await executor.SendAsResponseAsync<TestUser>(
            CreateRequest(), JsonDescriptor(), null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        await executor.SendAsync(CreateRequest(), JsonDescriptor());
        mockClient.Verify(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ErrorStatusCode_ShouldThrowApiException()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError, "error"));
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var act = () => executor.SendAsync(CreateRequest(), JsonDescriptor());
        await act.Should().ThrowAsync<ApiException>();
    }

    [Fact]
    public async Task SendAsync_AllowAnyStatusCode_ShouldNotThrow()
    {
        var mockClient = new Mock<IBaseHttpClient>();
        mockClient.Setup(c => c.SendRawAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.BadRequest, "error"));
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        await executor.SendAsync(CreateRequest(), JsonDescriptor(allowAnyStatusCode: true));
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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = null,
            Resilience = null
        };

        var result = await executor.ExecuteAsync<TestUser>(
            CreateRequest(), descriptor, null);

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
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<TestUser?>>, TimeSpan, CancellationToken>(
                async (key, fetch, exp, ct) =>
                {
                    fetchedValue = await fetch();
                    return fetchedValue;
                });
        var executor = new DefaultHttpRequestExecutor(mockClient.Object, mockCache.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = new CacheOptions { DurationSeconds = 60 },
            CacheKey = "test-key"
        };

        var result = await executor.ExecuteAsync<TestUser>(CreateRequest(), descriptor, null);

        result!.Id.Should().Be(20);
        mockCache.Verify(c => c.GetOrFetchAsync(
            "test-key", It.IsAny<Func<Task<TestUser?>>>(), TimeSpan.FromSeconds(60),
            It.IsAny<CancellationToken>()), Times.Once);
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
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedUser);
        var executor = new DefaultHttpRequestExecutor(mockClient.Object, mockCache.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = new CacheOptions { DurationSeconds = 60 },
            CacheKey = "hit-key"
        };

        var result = await executor.ExecuteAsync<TestUser>(CreateRequest(), descriptor, null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object, null, mockResolver.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 2 }
        };

        var result = await executor.ExecuteAsync<TestUser>(requestTemplateRef, descriptor, null);

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
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<TestUser?>>, TimeSpan, CancellationToken>(
                async (key, fetch, exp, ct) => await fetch());
        var mockResolver = new Mock<IResiliencePolicyResolver>();
        mockResolver.Setup(r => r.ResolvePolicyWrapper<TestUser>(
                It.IsAny<ResilienceExecutionOptions>(), It.IsAny<HttpRequestMessage>()))
            .Returns<ResilienceExecutionOptions, HttpRequestMessage>(
                (options, requestTemplate) =>
                    (coreExecute, ct) => coreExecute(requestTemplate, ct));
        var executor = new DefaultHttpRequestExecutor(mockClient.Object, mockCache.Object, mockResolver.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = new CacheOptions { DurationSeconds = 30 },
            CacheKey = "combo-key",
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 1 }
        };

        var result = await executor.ExecuteAsync<TestUser>(CreateRequest(), descriptor, null);

        result!.Id.Should().Be(50);
        // 缓存应被调用一次（包裹弹性策略）
        mockCache.Verify(c => c.GetOrFetchAsync(
            "combo-key", It.IsAny<Func<Task<TestUser?>>>(), TimeSpan.FromSeconds(30),
            It.IsAny<CancellationToken>()), Times.Once);
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
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedUser);
        var mockResolver = new Mock<IResiliencePolicyResolver>();
        var executor = new DefaultHttpRequestExecutor(mockClient.Object, mockCache.Object, mockResolver.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(),
            Cache = new CacheOptions { DurationSeconds = 30 },
            CacheKey = "hit-combo-key",
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 1 }
        };

        var result = await executor.ExecuteAsync<TestUser>(CreateRequest(), descriptor, null);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(isVoid: true),
            Resilience = null
        };

        await executor.ExecuteAsync(CreateRequest(), descriptor);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object, null, mockResolver.Object);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(isVoid: true),
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 2 }
        };

        await executor.ExecuteAsync(CreateRequest(), descriptor);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object, null, null);

        var descriptor = new ExecutionDescriptor
        {
            Response = JsonDescriptor(isVoid: true),
            Resilience = new ResilienceExecutionOptions { RetryEnabled = true, MaxRetries = 2 }
        };

        await executor.ExecuteAsync(CreateRequest(), descriptor);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var result = await executor.DownloadAsync(CreateRequest());

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var tempFile = Path.Combine(Path.GetTempPath(), $"mud_test_{Guid.NewGuid():N}.bin");
        try
        {
            await executor.DownloadLargeAsync(CreateRequest(), tempFile);

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
        var executor = new DefaultHttpRequestExecutor(mockClient.Object);

        var tempFile = Path.Combine(Path.GetTempPath(), $"mud_test_{Guid.NewGuid():N}.bin");
        var reportedBytes = new List<long>();
        var progress = new Progress<long>(b => reportedBytes.Add(b));
        try
        {
            await executor.DownloadLargeAsync(CreateRequest(), tempFile, progress: progress);

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

    #endregion

    public sealed class TestUser
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
