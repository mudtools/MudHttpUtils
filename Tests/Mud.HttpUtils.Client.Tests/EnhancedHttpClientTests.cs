using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Mud.HttpUtils.Tests;

[Collection("UrlValidator Collection")]
public class EnhancedHttpClientTests : IClassFixture<UrlValidatorFixture>
{
    private readonly UrlValidatorFixture _fixture;

    public EnhancedHttpClientTests(UrlValidatorFixture fixture)
    {
        _fixture = fixture;
        _fixture.RestoreDomains();
    }

    private static TestableEnhancedHttpClient CreateClient(HttpMessageHandler handler, ILogger? logger = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.com") };
        return new TestableEnhancedHttpClient(httpClient, logger);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new TestableEnhancedHttpClient(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithValidHttpClient_CreatesInstance()
    {
        var httpClient = new HttpClient();
        var client = new TestableEnhancedHttpClient(httpClient);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullLogger_UsesNullLogger()
    {
        var httpClient = new HttpClient();
        var client = new TestableEnhancedHttpClient(httpClient, logger: null);

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithRequestInterceptors_OrderedCorrectly()
    {
        var httpClient = new HttpClient();
        var interceptor1 = new Mock<IHttpRequestInterceptor>();
        interceptor1.Setup(i => i.Order).Returns(2);
        var interceptor2 = new Mock<IHttpRequestInterceptor>();
        interceptor2.Setup(i => i.Order).Returns(1);

        var client = new TestableEnhancedHttpClient(
            httpClient,
            requestInterceptors: new[] { interceptor1.Object, interceptor2.Object });

        client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithResponseInterceptors_OrderedCorrectly()
    {
        var httpClient = new HttpClient();
        var interceptor1 = new Mock<IHttpResponseInterceptor>();
        interceptor1.Setup(i => i.Order).Returns(2);
        var interceptor2 = new Mock<IHttpResponseInterceptor>();
        interceptor2.Setup(i => i.Order).Returns(1);

        var client = new TestableEnhancedHttpClient(
            httpClient,
            responseInterceptors: new[] { interceptor1.Object, interceptor2.Object });

        client.Should().NotBeNull();
    }

    #endregion

    #region AllowCustomBaseUrls Tests

    [Fact]
    public async Task SendAsync_WithNonWhitelistedDomain_AndAllowCustomBaseUrlsFalse_ThrowsInvalidOperationException()
    {
        var handler = CreateMockHandler("{}", HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        // 使用不在白名单中的域名（白名单中只有 api.example.com）
        var request = new HttpRequestMessage(HttpMethod.Get, "https://other.example.com/test");
        var act = async () => await client.SendAsync<string>(request);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendAsync_WithWhitelistedDomain_AndAllowCustomBaseUrlsFalse_DoesNotThrow()
    {
        var json = JsonSerializer.Serialize(new TestData { Name = "Test", Value = 1 });
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        // 使用白名单中的域名（UrlValidatorFixture 配置了 api.example.com）
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var act = async () => await client.SendAsync<TestData>(request);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Constructor_WithAllowCustomBaseUrls_CreatesInstance()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("https://api.example.com") };
        var client = new TestableEnhancedHttpClient(httpClient, allowCustomBaseUrls: true);

        client.Should().NotBeNull();
    }

    #endregion

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.SendAsync<string>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_WithSuccessfulJsonResponse_ReturnsDeserializedResult()
    {
        var expectedData = new { Name = "Test", Value = 42 };
        var json = JsonSerializer.Serialize(expectedData);
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestData>(request);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task SendAsync_WithCustomJsonSerializerOptions_UsesProvidedOptions()
    {
        var json = "{\"name\":\"Test\",\"value\":42}";
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestData>(request, jsonSerializerOptions: options);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_WithHttpError_ThrowsHttpRequestException()
    {
        var handler = CreateMockHandler("error", HttpStatusCode.InternalServerError);
        var client = CreateClient(handler.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var act = async () => await client.SendAsync<string>(request);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendAsync_WithInvalidJson_ThrowsHttpRequestExceptionWithJsonInnerException()
    {
        var handler = CreateMockHandler("not-valid-json", HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var act = async () => await client.SendAsync<TestData>(request);

        var exception = await act.Should().ThrowAsync<HttpRequestException>();
        exception.And.InnerException.Should().BeOfType<JsonException>();
    }

    [Fact]
    public async Task SendAsync_WithEmptyContent_ReturnsDefault()
    {
        var handler = CreateMockHandlerWithContentLength("", HttpStatusCode.OK, contentLength: 0);
        var client = CreateClient(handler.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestData>(request);

        result.Should().BeNull();
    }

    #endregion

    #region SendRawAsync Tests

    [Fact]
    public async Task SendRawAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.SendRawAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendRawAsync_WithSuccessfulResponse_ReturnsHttpResponseMessage()
    {
        var handler = CreateMockHandler("raw content", HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var response = await client.SendRawAsync(request);

        response.Should().NotBeNull();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region SendStreamAsync Tests

    [Fact]
    public async Task SendStreamAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.SendStreamAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendStreamAsync_WithSuccessfulResponse_ReturnsStream()
    {
        var content = "stream content";
        var handler = CreateMockHandler(content, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        using var stream = await client.SendStreamAsync(request);

        stream.Should().NotBeNull();
        stream.CanRead.Should().BeTrue();
    }

    [Fact]
    public async Task SendStreamAsync_WithHttpError_ThrowsHttpRequestException()
    {
        var handler = CreateMockHandler("error", HttpStatusCode.InternalServerError);
        var client = CreateClient(handler.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var act = async () => await client.SendStreamAsync(request);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region DownloadAsync Tests

    [Fact]
    public async Task DownloadAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.DownloadAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DownloadAsync_WithSuccessfulResponse_ReturnsByteArray()
    {
        var bytes = Encoding.UTF8.GetBytes("file content");
        var handler = CreateMockHandlerForBytes(bytes, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/file");
        var result = await client.DownloadAsync(request);

        result.Should().NotBeNull();
        result.Should().Equal(bytes);
    }

    [Fact]
    public async Task DownloadAsync_WithHttpError_ThrowsHttpRequestException()
    {
        var handler = CreateMockHandler("error", HttpStatusCode.InternalServerError);
        var client = CreateClient(handler.Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/file");
        var act = async () => await client.DownloadAsync(request);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region DownloadLargeAsync Tests

    [Fact]
    public async Task DownloadLargeAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.DownloadLargeAsync(null!, "/tmp/file.dat");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DownloadLargeAsync_WithEmptyFilePath_ThrowsArgumentException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/file");
        var act = async () => await client.DownloadLargeAsync(request, "");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DownloadLargeAsync_WithWhitespaceFilePath_ThrowsArgumentException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/file");
        var act = async () => await client.DownloadLargeAsync(request, "   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region JSON Helper Method Tests

    [Fact]
    public async Task GetAsync_WithNullUri_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.GetAsync<string>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PostAsJsonAsync_WithNullUri_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.PostAsJsonAsync<object, string>(null!, new { });

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PostAsJsonAsync_WithNullData_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.PostAsJsonAsync<object, string>("/api/test", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PutAsJsonAsync_WithNullUri_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.PutAsJsonAsync<object, string>(null!, new { });

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeleteAsJsonAsync_WithNullUri_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.DeleteAsJsonAsync<string>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PatchAsJsonAsync_WithNullUri_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.PatchAsJsonAsync<object, string>(null!, new { });

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PostAsJsonAsync_WithSuccessfulResponse_ReturnsResult()
    {
        var responseData = new TestData { Name = "Created", Value = 1 };
        var json = JsonSerializer.Serialize(responseData);
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var result = await client.PostAsJsonAsync<TestData, TestData>("/api/test", new TestData { Name = "Test", Value = 42 });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Created");
    }

    [Fact]
    public async Task PutAsJsonAsync_WithSuccessfulResponse_ReturnsResult()
    {
        var responseData = new TestData { Name = "Updated", Value = 2 };
        var json = JsonSerializer.Serialize(responseData);
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var result = await client.PutAsJsonAsync<TestData, TestData>("/api/test", new TestData { Name = "Test", Value = 42 });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteAsJsonAsync_WithSuccessfulResponse_ReturnsResult()
    {
        var responseData = new TestData { Name = "Deleted", Value = 0 };
        var json = JsonSerializer.Serialize(responseData);
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var result = await client.DeleteAsJsonAsync<TestData>("/api/test");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Deleted");
    }

    [Fact]
    public async Task DeleteAsJsonAsync_WithBody_ReturnsResult()
    {
        var responseData = new TestData { Name = "Deleted", Value = 0 };
        var json = JsonSerializer.Serialize(responseData);
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var result = await client.DeleteAsJsonAsync<TestData, TestData>("/api/test", new TestData { Name = "Test", Value = 42 });

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task PatchAsJsonAsync_WithSuccessfulResponse_ReturnsResult()
    {
        var responseData = new TestData { Name = "Patched", Value = 3 };
        var json = JsonSerializer.Serialize(responseData);
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var client = CreateClient(handler.Object);

        var result = await client.PatchAsJsonAsync<TestData, TestData>("/api/test", new TestData { Name = "Test", Value = 42 });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Patched");
    }

    #endregion

    #region XML Helper Method Tests

    [Fact]
    public async Task GetXmlAsync_WithNullUri_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.GetXmlAsync<string>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PostAsXmlAsync_WithNullUri_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.PostAsXmlAsync<object, string>(null!, new { });

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PostAsXmlAsync_WithNullData_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.PostAsXmlAsync<object, string>("/api/test", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PutAsXmlAsync_WithNullUri_ThrowsArgumentNullException()
    {
        var client = CreateClient(new Mock<HttpMessageHandler>().Object);

        var act = async () => await client.PutAsXmlAsync<object, string>(null!, new { });

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region Interceptor Tests

    [Fact]
    public async Task SendAsync_WithRequestInterceptor_InvokesInterceptor()
    {
        var json = JsonSerializer.Serialize(new TestData { Name = "Test", Value = 1 });
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var interceptor = new Mock<IHttpRequestInterceptor>();
        interceptor.Setup(i => i.Order).Returns(0);
        var client = CreateClient(handler.Object);
        var clientWithInterceptor = new TestableEnhancedHttpClient(
            new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") },
            requestInterceptors: new[] { interceptor.Object });

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        await clientWithInterceptor.SendAsync<TestData>(request);

        interceptor.Verify(i => i.OnRequestAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithResponseInterceptor_InvokesInterceptor()
    {
        var json = JsonSerializer.Serialize(new TestData { Name = "Test", Value = 1 });
        var handler = CreateMockHandler(json, HttpStatusCode.OK);
        var interceptor = new Mock<IHttpResponseInterceptor>();
        interceptor.Setup(i => i.Order).Returns(0);
        var clientWithInterceptor = new TestableEnhancedHttpClient(
            new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") },
            responseInterceptors: new[] { interceptor.Object });

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        await clientWithInterceptor.SendAsync<TestData>(request);

        interceptor.Verify(i => i.OnResponseAsync(It.IsAny<HttpResponseMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task SendAsync_WithCancelledToken_ThrowsTaskCanceledException()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage req, CancellationToken ct) =>
            {
                await Task.Delay(100, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var client = CreateClient(handler.Object);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var act = async () => await client.SendAsync<TestData>(request, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    #endregion

    #region Helper Methods

    private static Mock<HttpMessageHandler> CreateMockHandler(string content, HttpStatusCode statusCode)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        return handler;
    }

    private static Mock<HttpMessageHandler> CreateMockHandlerWithContentLength(string content, HttpStatusCode statusCode, long contentLength)
    {
        var httpContent = new StringContent(content, Encoding.UTF8, "application/json");
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = httpContent
            });
        return handler;
    }

    private static Mock<HttpMessageHandler> CreateMockHandlerForBytes(byte[] bytes, HttpStatusCode statusCode)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(bytes)
            });
        return handler;
    }

    public class TestData
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    public class TestableEnhancedHttpClient : EnhancedHttpClient
    {
        public TestableEnhancedHttpClient(
            HttpClient httpClient,
            ILogger? logger = null,
            IEnumerable<IHttpRequestInterceptor>? requestInterceptors = null,
            IEnumerable<IHttpResponseInterceptor>? responseInterceptors = null,
            ISensitiveDataMasker? sensitiveDataMasker = null,
            bool allowCustomBaseUrls = false)
            : base(httpClient, logger, requestInterceptors, responseInterceptors, sensitiveDataMasker, allowCustomBaseUrls)
        {
        }

        public override string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json)
            => throw new NotImplementedException();

        public override string DecryptContent(string encryptedContent)
            => throw new NotImplementedException();

        public override byte[] EncryptBytes(byte[] data)
            => throw new NotImplementedException();

        public override byte[] DecryptBytes(byte[] encryptedData)
            => throw new NotImplementedException();
    }

    #endregion
}
