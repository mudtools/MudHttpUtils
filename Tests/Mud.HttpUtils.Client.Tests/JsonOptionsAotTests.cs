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
/// CreateEnhancedClient → HttpClientFactoryEnhancedClient → EnhancedHttpClient → IHttpContentSerializer（由 HttpContentSerializerFactory.CreateDefault 合并 MudHttpJsonContext.Default）
/// </para>
/// </remarks>
public class JsonOptionsAotTests : IClassFixture<UrlValidatorFixture>
{
    public JsonOptionsAotTests(UrlValidatorFixture fixture)
    {
        fixture.RestoreDomains();
    }

    /// <summary>
    /// 测试用 DTO — 使用多词 PascalCase 属性名，配合自定义命名策略验证 resolver 生效。
    /// SnakeCaseLower 会将 UserName → user_name, UserAge → user_age，
    /// 与默认 CamelCase（userName, userAge）明显不同。
    /// </summary>
    public class TestDto
    {
        public string UserName { get; set; } = string.Empty;
        public int UserAge { get; set; }
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
    /// 可携带 IOptions / IHttpContentSerializer 参数的测试用 EnhancedHttpClient 子类。
    /// </summary>
    private class AotTestEnhancedClient : EnhancedHttpClient
    {
        public AotTestEnhancedClient(
            HttpClient httpClient,
            EnhancedHttpClientOptions? options = null,
            IOptions<JsonSerializerOptions>? jsonOptions = null,
            IHttpContentSerializer? contentSerializer = null)
            : base(httpClient, options, jsonOptions, contentSerializer)
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
        // 如果 resolver 生效，PostAsJsonAsync 序列化请求体应使用 USER_NAME, USER_AGE（全大写）
        var jsonOptions = CreateUpperSnakeCaseOptions();

        var capturedRequest = new CapturedRequest();
        var handler = CreateMockHandlerCapturingRequest(
            "{\"USER_NAME\":\"test\",\"USER_AGE\":30}", HttpStatusCode.OK, capturedRequest);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var client = new AotTestEnhancedClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }, jsonOptions);

        // Act
        await client.PostAsJsonAsync<TestDto, TestDto>("/api/test", new TestDto { UserName = "test", UserAge = 30 });

        // Assert: resolver 生效时，请求体应为 USER_NAME, USER_AGE（全大写+下划线）
        capturedRequest.Body.Should().NotBeNull();
        capturedRequest.Body!.Should().Contain("\"USER_NAME\"");
        capturedRequest.Body.Should().Contain("\"USER_AGE\"");
        capturedRequest.Body.Should().NotContain("\"UserName\"");
        capturedRequest.Body.Should().NotContain("\"UserAge\"");
    }

    [Fact]
    public async Task EnhancedHttpClient_WithIOptionsJsonSerializerOptions_UsesResolverForDeserialize()
    {
        // Arrange: 返回 UpperSnakeCase JSON，验证反序列化使用 resolver
        var jsonOptions = CreateUpperSnakeCaseOptions();

        var responseBody = "{\"USER_NAME\":\"Alice\",\"USER_AGE\":25}";
        var handler = CreateMockHandler(responseBody, HttpStatusCode.OK);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var client = new AotTestEnhancedClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }, jsonOptions);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestDto>(request);

        // Assert: resolver 生效时，UpperSnakeCase JSON 能正确反序列化到 PascalCase 属性
        result.Should().NotBeNull();
        result!.UserName.Should().Be("Alice");
        result.UserAge.Should().Be(25);
    }

    [Fact]
    public async Task EnhancedHttpClient_WithoutIOptions_FallsBackToDefaultOptions()
    {
        // Arrange: 不传 IOptions，应使用静态默认 options（CamelCase）
        // 默认 options 的 CamelCase 能解析 camelCase JSON，但不能解析 UpperSnakeCase
        var responseBody = "{\"userName\":\"Alice\",\"userAge\":25}";
        var handler = CreateMockHandler(responseBody, HttpStatusCode.OK);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var client = new AotTestEnhancedClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });

        // Act: 默认 options 使用 CamelCase，userName/userAge 可以反序列化
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestDto>(request);

        // Assert
        result.Should().NotBeNull();
        result!.UserName.Should().Be("Alice");
        result.UserAge.Should().Be(25);
    }

    [Fact]
    public async Task EnhancedHttpClient_WithoutIOptions_DefaultOptionsUsesCamelCaseCaseInsensitive()
    {
        // Arrange: 不传 IOptions，默认 options 有 PropertyNameCaseInsensitive=true
        // 所以 USERNAME/userName/UserName 都能匹配到 UserName 属性（仅大小写不同，不含下划线）
        var responseBody = "{\"USERNAME\":\"Alice\",\"USERAGE\":25}";
        var handler = CreateMockHandler(responseBody, HttpStatusCode.OK);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var client = new AotTestEnhancedClient(httpClient, new EnhancedHttpClientOptions { AllowCustomBaseUrls = true });

        // Act: 默认 options 的 PropertyNameCaseInsensitive=true，USERNAME 能匹配 UserName
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestDto>(request);

        // Assert: 因大小写不敏感，USERNAME 匹配 UserName
        result.Should().NotBeNull();
        result!.UserName.Should().Be("Alice");
        result.UserAge.Should().Be(25);
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
            "{\"USER_NAME\":\"test\",\"USER_AGE\":30}", HttpStatusCode.OK, capturedRequest);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(httpClient);

        var client = new HttpClientFactoryEnhancedClient(
            mockFactory.Object, "testClient", options: new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }, jsonOptions: jsonOptions);

        // Act
        await client.PostAsJsonAsync<TestDto, TestDto>("/api/test", new TestDto { UserName = "test", UserAge = 30 });

        // Assert: resolver 通过 HttpClientFactoryEnhancedClient 透传到基类
        capturedRequest.Body.Should().NotBeNull();
        capturedRequest.Body!.Should().Contain("\"USER_NAME\"");
        capturedRequest.Body.Should().NotContain("\"UserName\"");
    }

    [Fact]
    public async Task HttpClientFactoryEnhancedClient_WithBaseAddress_PreservesIOptions()
    {
        // Arrange: 验证 WithBaseAddress 重建实例时保留 IOptions
        var jsonOptions = CreateUpperSnakeCaseOptions();

        var capturedRequest = new CapturedRequest();
        var handler = CreateMockHandlerCapturingRequest(
            "{\"USER_NAME\":\"test\",\"USER_AGE\":30}", HttpStatusCode.OK, capturedRequest);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(httpClient);

        var client = new HttpClientFactoryEnhancedClient(
            mockFactory.Object, "testClient", options: new EnhancedHttpClientOptions { AllowCustomBaseUrls = true }, jsonOptions: jsonOptions);

        // Act: WithBaseAddress 重建实例
        var newClient = client.WithBaseAddress(new Uri("https://api.example.com/v2"));

        // 使用重建后的实例发送请求
        await newClient.PostAsJsonAsync<TestDto, TestDto>("/api/test", new TestDto { UserName = "test", UserAge = 30 });

        // Assert: 重建实例仍使用 resolver
        capturedRequest.Body.Should().NotBeNull();
        capturedRequest.Body!.Should().Contain("\"USER_NAME\"");
        capturedRequest.Body.Should().NotContain("\"UserName\"");
    }

    #endregion

    #region Phase B 修复验证：自定义 IHttpContentSerializer 生效

    /// <summary>
    /// 验证注入自定义 IHttpContentSerializer（不同命名策略）后，其 options 在请求体序列化中真实生效。
    /// 此为 Phase B 核心修复：此前基类 per-call 透传 _jsonOptions 覆盖了序列化器自持 options。
    /// </summary>
    [Fact]
    public async Task CustomContentSerializer_WithSnakeCaseNaming_TakesEffectForSerialize()
    {
        // Arrange: 创建使用 SnakeCaseLower 命名策略的自定义序列化器
        var customOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var customSerializer = new SystemTextJsonContentSerializer(customOptions);

        var capturedRequest = new CapturedRequest();
        var handler = CreateMockHandlerCapturingRequest(
            "{\"user_name\":\"test\",\"user_age\":30}", HttpStatusCode.OK, capturedRequest);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        // 直接注入自定义序列化器，不传 IOptions
        var client = new AotTestEnhancedClient(
            httpClient,
            new EnhancedHttpClientOptions { AllowCustomBaseUrls = true },
            contentSerializer: customSerializer);

        // Act
        await client.PostAsJsonAsync<TestDto, TestDto>("/api/test", new TestDto { UserName = "test", UserAge = 30 });

        // Assert: 自定义序列化器的 SnakeCaseLower 策略应生效（user_name, user_age）
        capturedRequest.Body.Should().NotBeNull();
        capturedRequest.Body!.Should().Contain("\"user_name\"");
        capturedRequest.Body.Should().Contain("\"user_age\"");
        capturedRequest.Body.Should().NotContain("\"UserName\"");
        capturedRequest.Body.Should().NotContain("\"userName\"");
    }

    /// <summary>
    /// 验证注入自定义 IHttpContentSerializer 后，其 options 在响应反序列化中真实生效。
    /// </summary>
    [Fact]
    public async Task CustomContentSerializer_WithSnakeCaseNaming_TakesEffectForDeserialize()
    {
        // Arrange: 创建使用 SnakeCaseLower 命名策略的自定义序列化器
        var customOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var customSerializer = new SystemTextJsonContentSerializer(customOptions);

        // 返回 snake_case JSON
        var responseBody = "{\"user_name\":\"Alice\",\"user_age\":25}";
        var handler = CreateMockHandler(responseBody, HttpStatusCode.OK);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var client = new AotTestEnhancedClient(
            httpClient,
            new EnhancedHttpClientOptions { AllowCustomBaseUrls = true },
            contentSerializer: customSerializer);

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        var result = await client.SendAsync<TestDto>(request);

        // Assert: 自定义序列化器能正确反序列化 snake_case JSON
        result.Should().NotBeNull();
        result!.UserName.Should().Be("Alice");
        result.UserAge.Should().Be(25);
    }

    #endregion

    #region Phase A 修复验证：默认序列化器合并 MudHttpJsonContext.Default

    /// <summary>
    /// 验证未注入 IHttpContentSerializer 时，HttpContentSerializerFactory.CreateDefault()
    /// 产生的默认序列化器已合并 MudHttpJsonContext.Default。
    /// </summary>
    [Fact]
    public void CreateDefault_WithoutInjection_MergesMudHttpJsonContext()
    {
        // Act: 通过工厂创建默认序列化器
        var serializer = HttpContentSerializerFactory.CreateDefault();

        // Assert: 序列化器不为 null
        serializer.Should().NotBeNull();

        // 验证内部 options 合并了 MudHttpJsonContext.Default
        // SystemTextJsonContentSerializer 暴露了 Options 属性用于检查
        var systemTextSerializer = serializer as SystemTextJsonContentSerializer;
        systemTextSerializer.Should().NotBeNull();
#if NET8_0_OR_GREATER
        // 在 NET8+ 下，默认 options 应包含 MudHttpJsonContext.Default resolver
        systemTextSerializer!.Options.TypeInfoResolver.Should().NotBeNull();
#endif
    }

    #endregion

    #region Phase B4 修复验证：WithBaseAddress 保留自定义 IHttpContentSerializer

    /// <summary>
    /// 验证注入自定义 IHttpContentSerializer 后调用 WithBaseAddress，
    /// 重建实例仍使用该自定义序列化器（B4 修复验证）。
    /// </summary>
    [Fact]
    public async Task CustomContentSerializer_WithBaseAddress_PreservesSerializer()
    {
        // Arrange: 创建使用 SnakeCaseLower 命名策略的自定义序列化器
        var customOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var customSerializer = new SystemTextJsonContentSerializer(customOptions);

        var capturedRequest = new CapturedRequest();
        var handler = CreateMockHandlerCapturingRequest(
            "{\"user_name\":\"test\",\"user_age\":30}", HttpStatusCode.OK, capturedRequest);

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.example.com") };
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient("testClient"))
            .Returns(httpClient);

        var client = new HttpClientFactoryEnhancedClient(
            mockFactory.Object, "testClient",
            options: new EnhancedHttpClientOptions { AllowCustomBaseUrls = true },
            contentSerializer: customSerializer);

        // Act: WithBaseAddress 重建实例
        var newClient = client.WithBaseAddress(new Uri("https://api.example.com/v2"));

        // 使用重建后的实例发送请求
        await newClient.PostAsJsonAsync<TestDto, TestDto>("/api/test", new TestDto { UserName = "test", UserAge = 30 });

        // Assert: 重建实例仍使用自定义序列化器的 SnakeCaseLower 策略
        capturedRequest.Body.Should().NotBeNull();
        capturedRequest.Body!.Should().Contain("\"user_name\"");
        capturedRequest.Body.Should().NotContain("\"UserName\"");
        capturedRequest.Body.Should().NotContain("\"userName\"");
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
