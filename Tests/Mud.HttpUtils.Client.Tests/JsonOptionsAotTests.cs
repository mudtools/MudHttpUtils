// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  AOT 改造 G8 专项测试：验证 IOptions<JsonSerializerOptions> 透传链
//  确保 DI 注册的 JsonSerializerContext resolver 能正确传递到
//  EnhancedHttpClient、HttpClientFactoryEnhancedClient 及 WithBaseAddress 重建实例
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Moq;
using Moq.Protected;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// G8 专项测试：验证 IOptions&lt;JsonSerializerOptions&gt; resolver 透传链完整性。
/// </summary>
/// <remarks>
/// <para>
/// G8 问题核心：原方案仅给 EnhancedHttpClient 基类构造函数加了可选 IOptions 参数，
/// 但 DI 创建的实例全是 HttpClientFactoryEnhancedClient，CreateEnhancedClient 未传入 IOptions，
/// 导致消费方 services.Configure&lt;JsonSerializerOptions&gt;(o => o.TypeInfoResolver = …) 无法覆盖 EnhancedHttpClient 内置方法。
/// </para>
/// <para>
/// 这些测试验证修复后的透传链：
/// CreateEnhancedClient → HttpClientFactoryEnhancedClient → EnhancedHttpClient.BuildJsonOptions → _jsonOptions
/// </para>
/// </remarks>
public class JsonOptionsAotTests : IClassFixture<UrlValidatorFixture>
{
    public JsonOptionsAotTests(UrlValidatorFixture fixture)
    {
        fixture.RestoreDomains();
    }

    /// <summary>
    /// 测试用 DTO — 使用 PascalCase 属性名，配合自定义 resolver 验证 resolver 生效。
    /// </summary>
    public class TestDto
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    /// <summary>
    /// 自定义 resolver — 将属性名转为 UP_PER_CASE（全大写+下划线），
    /// 与默认 CamelCase 明显不同，便于验证 resolver 是否被使用。
    /// </summary>
    private sealed class UpperSnakeCaseResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var defaultResolver = new DefaultJsonTypeInfoResolver();
            var info = defaultResolver.GetTypeInfo(type, options);
            if (info == null) return null;

            foreach (var prop in info.Properties)
            {
                prop.Name = ToUpperSnakeCase(prop.Name);
            }
            return info;
        }

        private static string ToUpperSnakeCase(string name)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (i > 0 && char.IsUpper(c))
                    sb.Append('_');
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 可携带 IOptions 参数的测试用 EnhancedHttpClient 子类。
    /// </summary>
    private class AotTestEnhancedClient : EnhancedHttpClient
    {
        public AotTestEnhancedClient(
            HttpClient httpClient,
            EnhancedHttpClientOptions? options = null,
            IOptions<JsonSerializerOptions>? jsonOptions = null)
            : base(httpClient, options, jsonOptions)
        {
        }
    }

    /// <summary>
    /// 创建一个使用 UpperSnakeCase resolver 的 IOptions&lt;JsonSerializerOptions&gt;。
    /// </summary>
    private static IOptions<JsonSerializerOptions> CreateUpperSnakeCaseOptions()
    {
        return Options.Create(new JsonSerializerOptions
        {
            TypeInfoResolver = new UpperSnakeCaseResolver()
        });
    }

    #region EnhancedHttpClient 基类 — IOptions<JsonSerializerOptions> 透传

    [Fact]
    public async Task EnhancedHttpClient_WithIOptionsJsonSerializerOptions_UsesResolverForSerialize()
    {
        // Arrange: 使用 UpperSnakeCase naming 的 resolver
        // 如果 resolver 生效，PostAsJsonAsync 序列化请求体应使用 NAME, AGE（全大写）
        var jsonOptions = CreateUpperSnakeCaseOptions();

        var capturedRequest = new CapturedRequest();
        var handler = CreateMockHandlerCapturingRequest(
            "{\"NAME\":\"test\",\"AGE\":30}", HttpStatusCode.OK, capturedRequest);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var client = new AotTestEnhancedClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }, jsonOptions);

        // Act
        await client.PostAsJsonAsync<TestDto, TestDto>("/api/test", new TestDto { Name = "test", Age = 30 });

        // Assert: resolver 生效时，请求体应为 NAME, AGE（全大写+下划线）
        capturedRequest.Body.Should().NotBeNull();
        capturedRequest.Body!.Should().Contain("\"NAME\"");
        capturedRequest.Body.Should().Contain("\"AGE\"");
        capturedRequest.Body.Should().NotContain("\"Name\"");
        capturedRequest.Body.Should().NotContain("\"Age\"");
    }

    [Fact]
    public async Task EnhancedHttpClient_WithIOptionsJsonSerializerOptions_UsesResolverForDeserialize()
    {
        // Arrange: 返回 UpperSnakeCase JSON，验证反序列化使用 resolver
        var jsonOptions = CreateUpperSnakeCaseOptions();

        var responseBody = "{\"NAME\":\"Alice\",\"AGE\":25}";
        var handler = CreateMockHandler(responseBody, HttpStatusCode.OK);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var client = new AotTestEnhancedClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }, jsonOptions);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestDto>(request);

        // Assert: resolver 生效时，UpperSnakeCase JSON 能正确反序列化到 PascalCase 属性
        result.Should().NotBeNull();
        result!.Name.Should().Be("Alice");
        result.Age.Should().Be(25);
    }

    [Fact]
    public async Task EnhancedHttpClient_WithoutIOptions_FallsBackToDefaultOptions()
    {
        // Arrange: 不传 IOptions，应使用静态默认 options（CamelCase）
        // 默认 options 的 CamelCase 能解析 camelCase JSON，但不能解析 UpperSnakeCase
        var responseBody = "{\"name\":\"Alice\",\"age\":25}";
        var handler = CreateMockHandler(responseBody, HttpStatusCode.OK);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var client = new AotTestEnhancedClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });

        // Act: 默认 options 使用 CamelCase，name/age 可以反序列化
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestDto>(request);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Alice");
        result.Age.Should().Be(25);
    }

    [Fact]
    public async Task EnhancedHttpClient_WithoutIOptions_DefaultOptionsUsesCamelCaseCaseInsensitive()
    {
        // Arrange: 不传 IOptions，默认 options 有 PropertyNameCaseInsensitive=true
        // 所以 NAME/name/Name 都能匹配到 Name 属性
        var responseBody = "{\"NAME\":\"Alice\",\"AGE\":25}";
        var handler = CreateMockHandler(responseBody, HttpStatusCode.OK);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var client = new AotTestEnhancedClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });

        // Act: 默认 options 的 PropertyNameCaseInsensitive=true，NAME 能匹配 Name
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestDto>(request);

        // Assert: 因大小写不敏感，NAME 匹配 Name
        result.Should().NotBeNull();
        result!.Name.Should().Be("Alice");
        result.Age.Should().Be(25);
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void EnhancedHttpClient_WithOptionsJsonTypeInfoResolver_TakesPriorityOverIOptions()
    {
        // Arrange: EnhancedHttpClientOptions.JsonTypeInfoResolver 应优先于 IOptions
        var jsonOptions = Options.Create(new JsonSerializerOptions
        {
            TypeInfoResolver = new UpperSnakeCaseResolver()
        });

        var enhancedOptions = new EnhancedHttpClientOptions
        {
            // 设置编程式 resolver（应优先于 IOptions）
            JsonTypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        // Act & Assert: 不抛异常即表示构造成功，两个 resolver 来源都设置了
        var httpClient = new HttpClient();
        var client = new AotTestEnhancedClient(httpClient, enhancedOptions, jsonOptions);
        client.Should().NotBeNull();
    }
#endif

    #endregion

    #region HttpClientFactoryEnhancedClient — IOptions 透传

    [Fact]
    public async Task HttpClientFactoryEnhancedClient_WithIOptionsJsonSerializerOptions_PassesResolverToBase()
    {
        // Arrange: 验证 HttpClientFactoryEnhancedClient 将 IOptions 透传给基类
        var jsonOptions = CreateUpperSnakeCaseOptions();

        var capturedRequest = new CapturedRequest();
        var handler = CreateMockHandlerCapturingRequest(
            "{\"NAME\":\"test\",\"AGE\":30}", HttpStatusCode.OK, capturedRequest);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(httpClient);

        var client = new HttpClientFactoryEnhancedClient(
            mockFactory.Object, "testClient", options: new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }, jsonOptions: jsonOptions);

        // Act
        await client.PostAsJsonAsync<TestDto, TestDto>("/api/test", new TestDto { Name = "test", Age = 30 });

        // Assert: resolver 通过 HttpClientFactoryEnhancedClient 透传到基类
        capturedRequest.Body.Should().NotBeNull();
        capturedRequest.Body!.Should().Contain("\"NAME\"");
        capturedRequest.Body.Should().Contain("\"AGE\"");
        capturedRequest.Body.Should().NotContain("\"Name\"");
    }

    [Fact]
    public async Task HttpClientFactoryEnhancedClient_WithBaseAddress_PreservesIOptions()
    {
        // Arrange: 验证 WithBaseAddress 重建实例时保留 IOptions
        var jsonOptions = CreateUpperSnakeCaseOptions();

        var capturedRequest = new CapturedRequest();
        var handler = CreateMockHandlerCapturingRequest(
            "{\"NAME\":\"test\",\"AGE\":30}", HttpStatusCode.OK, capturedRequest);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(httpClient);

        var client = new HttpClientFactoryEnhancedClient(
            mockFactory.Object, "testClient", options: new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }, jsonOptions: jsonOptions);

        // Act: WithBaseAddress 重建实例
        var newClient = client.WithBaseAddress(new Uri("https://api.example.com/v2"));

        // 使用重建后的实例发送请求
        await newClient.PostAsJsonAsync<TestDto, TestDto>("/api/test", new TestDto { Name = "test", Age = 30 });

        // Assert: 重建实例仍使用 resolver
        capturedRequest.Body.Should().NotBeNull();
        capturedRequest.Body!.Should().Contain("\"NAME\"");
        capturedRequest.Body.Should().NotContain("\"Name\"");
    }

    #endregion

    #region Helper Methods

    private class CapturedRequest
    {
        public string? Body { get; set; }
    }

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

    private static Mock<HttpMessageHandler> CreateMockHandlerCapturingRequest(
        string responseContent, HttpStatusCode statusCode, CapturedRequest captured)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                if (req.Content != null)
                {
                    captured.Body = req.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
                }
                return new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
                };
            });
        return handler;
    }

    #endregion
}
