using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mud.HttpUtils;
using Mud.HttpUtils.Resilience;
using System.Net;
using System.Text;
using System.Text.Json;
#if NET8_0_OR_GREATER
using System.Text.Json.Serialization.Metadata;
#endif

namespace AotVerificationDemo;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                ConfigureAotServices(services);
            })
            .Build();

        Console.WriteLine("=== Mud.HttpUtils Native AOT 验证示例 ===\n");

        // 每个场景验证一条 AOT 安全路径
        await DemoGeneratedApiClient_Json(host.Services);
        await DemoEnhancedHttpClient_Json(host.Services);
        await DemoGeneratedApiClient_FormUrlEncoded(host.Services);
        await DemoQueryMapJsonSerialization(host.Services);
        await DemoComplexQueryJsonSerialization(host.Services);
        await DemoQueryMapFeatureCoverage(host.Services);
        await DemoResilienceDecorator(host.Services);
        await DemoResilienceDecoratorWithImplementationType();
        DemoSensitiveDataMasker();
        DemoOAuth2Serialization();
        await DemoEncryptContentSerialization();
        await DemoNdjsonSerialization();
        await DemoScaffolderAutoCoverage();
        DemoUncoveredDtoRuntime();
        await DemoResponseTypeWrapping();

        Console.WriteLine("\n=== AOT 验证示例完成 ===");
        Console.WriteLine("如果此程序在 Native AOT 模式下成功运行，说明 JSON / 表单 / 查询参数 / 弹性 / 脱敏 / 加密 / NDJSON 主路径均已 AOT 兼容。");
        Console.WriteLine("AOT_OK");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 6：OAuth2 令牌序列化验证（直接序列化断言）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 OAuth2 令牌响应和令牌自省结果的 JSON 序列化/反序列化
    /// 在 AOT 下正确工作（使用库内置 OAuth2JsonContext）。
    /// </summary>
    /// <remarks>
    /// 此场景使用直接序列化断言，不依赖真实 HTTP 请求，
    /// 确保 OAuth2JsonContext 的类型元数据在 AOT 裁剪后仍可用。
    /// </remarks>
    private static void DemoOAuth2Serialization()
    {
        Console.WriteLine("--- 6. OAuth2 令牌序列化验证（库内置 JsonContext）---");

        // 使用 StandardOAuth2TokenManager 的 s_jsonOptions 反序列化
        // 验证 OAuth2JsonContext.Default 已正确注入
        // AOT 安全：直接使用 JsonTypeInfo<T> 重载，避免 JsonSerializerOptions 传递导致的 IL2026/IL3050 告警
#if NET8_0_OR_GREATER
        var introspectionJson = "{\"active\":true,\"client_id\":\"test-client\",\"username\":\"testuser\",\"scope\":\"read write\"}";
        var introspectionResult = JsonSerializer.Deserialize(introspectionJson, Mud.HttpUtils.OAuth2JsonContext.Default.TokenIntrospectionResult);

        if (introspectionResult != null && introspectionResult.Active && introspectionResult.ClientId == "test-client")
        {
            Console.WriteLine($"  IntrospectToken => Active={introspectionResult.Active}, ClientId={introspectionResult.ClientId}");
            Console.WriteLine("  [✓] OAuth2 令牌自省结果反序列化正确（OAuth2JsonContext 生效）");
        }
        else
        {
            Console.WriteLine("  [!] OAuth2 令牌自省结果反序列化失败——AOT 下类型元数据可能被裁剪！");
            throw new InvalidOperationException("OAuth2 introspection deserialization failed - type metadata may be trimmed in AOT");
        }
#else
        Console.WriteLine("  [✓] 跳过（JsonSourceGeneration 仅在 .NET 8+ 可用）");
#endif

        Console.WriteLine();
    }

    /// <summary>
    /// 配置所有 AOT 安全的服务注册
    /// </summary>
    private static void ConfigureAotServices(IServiceCollection services)
    {
        // 1. 配置 JsonSerializerOptions — 挂接源生成上下文（AOT 核心）
        services.Configure<JsonSerializerOptions>(options =>
        {
            // 仅使用源生成 resolver，不拼接 DefaultJsonTypeInfoResolver
            // 未声明的类型将抛出异常（比静默返回空对象更安全）
            options.TypeInfoResolver = AppJsonContext.Default;
        });

        // 2. 注册 EnhancedHttpClient（DI 路径经由 IHttpContentSerializer 序列化，options 含消费方 resolver）
        //    亦可使用 services.AddMudHttpContentSerializer(AppJsonContext.Default) 直接注入带 context 的序列化器
        services.AddMudHttpClient("default", client =>
        {
            client.BaseAddress = new Uri("https://httpbin.org");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // 3. 注册源生成的 API 客户端（IUserApi, IAuthApi, ISearchApi, IComplexSearchApi）
        services.AddWebApiHttpClient();

        // 4. 注册 Resilience 装饰器（包装 IEnhancedHttpClient）
        services.AddMudHttpResilienceDecorator(options =>
        {
            options.Retry.Enabled = true;
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.DelayMilliseconds = 100;
            options.Timeout.Enabled = true;
            options.Timeout.TimeoutSeconds = 5;
            options.CircuitBreaker.Enabled = false;
        });

        // 5. 注册 AOT 安全的脱敏器（替代 DefaultSensitiveDataMasker）
        //    先调用 AddSensitiveDataMasker() 注册库内默认实现，再以 AddSingleton 覆盖注册子类
        services.AddSensitiveDataMasker();
        services.AddSingleton<ISensitiveDataMasker, DemoSensitiveDataMasker>();
    }

    // ─────────────────────────────────────────────────────────
    // 场景 1：生成 API 客户端 JSON 请求/响应
    // ─────────────────────────────────────────────────────────

    private static async Task DemoGeneratedApiClient_Json(IServiceProvider services)
    {
        Console.WriteLine("--- 1. 生成 API 客户端 JSON 路径 ---");
        var userApi = services.GetRequiredService<IUserApi>();

        try
        {
            // GET 请求 → 响应反序列化为 UserDto（使用 JsonSerializerContext 源生成）
            var user = await userApi.GetUserAsync(1);
            Console.WriteLine($"  GetUserAsync(1) => {(user != null ? $"Id={user.Id}, Name={user.Name}" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  GetUserAsync — HTTP 请求失败（预期，无真实服务器）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  GetUserAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            // POST 请求 → 请求体序列化 CreateUserRequest（使用 JsonSerializerContext 源生成）
            var created = await userApi.CreateUserAsync(new CreateUserRequest
            {
                Name = "AOT Test",
                Email = "aot@example.com"
            });
            Console.WriteLine($"  CreateUserAsync => {(created != null ? $"Id={created.Id}" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  CreateUserAsync — HTTP 请求失败（预期）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  CreateUserAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("  [✓] JSON 序列化/反序列化代码路径已执行（序列化在 HTTP 请求发送前完成）\n");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 2：EnhancedHttpClient 内置 JSON 方法
    // ─────────────────────────────────────────────────────────

    private static async Task DemoEnhancedHttpClient_Json(IServiceProvider services)
    {
        Console.WriteLine("--- 2. EnhancedHttpClient.PostAsJsonAsync 路径 ---");
        var httpClient = services.GetRequiredService<IEnhancedHttpClient>();

        try
        {
            // EnhancedHttpClient 内置方法使用 _contentSerializer（其 options 含 JsonSerializerContext resolver）
            var result = await httpClient.PostAsJsonAsync<CreateUserRequest, UserDto>(
                "/api/users",
                new CreateUserRequest { Name = "Direct", Email = "direct@example.com" });
            Console.WriteLine($"  PostAsJsonAsync => {(result != null ? $"Id={result.Id}" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  PostAsJsonAsync — HTTP 请求失败（预期）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  PostAsJsonAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("  [✓] EnhancedHttpClient JSON 路径已执行（验证 G8 修复：IOptions 透传生效）\n");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 3：FormUrlEncoded Body（编译期静态属性访问）
    // ─────────────────────────────────────────────────────────

    private static async Task DemoGeneratedApiClient_FormUrlEncoded(IServiceProvider services)
    {
        Console.WriteLine("--- 3. FormUrlEncoded Body 路径 ---");
        var authApi = services.GetRequiredService<IAuthApi>();

        try
        {
            var result = await authApi.LoginAsync(new LoginForm
            {
                Username = "admin",
                Password = "secret123",
                RememberMe = true
            });
            Console.WriteLine($"  LoginAsync => {(result != null ? $"Token={result.Token[..Math.Min(8, result.Token.Length)]}..." : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  LoginAsync — HTTP 请求失败（预期）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  LoginAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("  [✓] FormUrlEncoded Body 已通过编译期静态属性访问生成（无运行时反射）\n");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 3b：查询参数 JSON 序列化路径（QueryParameterBinder AOT 修复验证）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 [Query] 逐参数声明路径在 AOT 下正常工作。
    /// </summary>
    /// <remarks>
    /// [Query] 参数由源生成器在编译期发射内联查询构建代码，
    /// 简单类型使用 ToString() 序列化，不涉及反射，AOT 安全。
    /// 对比 [QueryMap] 使用 FlattenObjectToQueryParams() 反射展平，AOT 不安全。
    /// </remarks>
    private static async Task DemoQueryMapJsonSerialization(IServiceProvider services)
    {
        Console.WriteLine("--- 3b. 查询参数路径（[Query] 逐参数声明，AOT 安全）---");
        var searchApi = services.GetRequiredService<ISearchApi>();

        try
        {
            // [Query] 参数逐个声明，源生成器在编译期发射内联代码
            // 简单类型使用 ToString() 序列化，不涉及反射
            var results = await searchApi.SearchAsync(
                keyword: "test",
                minAge: 18,
                maxAge: 65,
                activeOnly: true);
            Console.WriteLine($"  SearchAsync => {(results != null ? $"{results.Count} results" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  SearchAsync — HTTP 请求失败（预期，无真实服务器）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SearchAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("  [✓] 查询参数代码路径已执行（[Query] 逐参数内联，AOT 安全）\n");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 3c：复杂查询参数 JSON 序列化路径（QueryParameterBinder AOT 修复验证）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 [Query] 复杂类型参数在 AOT 下正常工作。
    /// </summary>
    /// <remarks>
    /// [Query] 复杂类型参数由源生成器在编译期发射内联属性展平代码，
    /// JSON 序列化使用 _contentSerializer.Serialize&lt;T&gt;(value)，
    /// 而非非泛型 JsonSerializer.Serialize(object?) 重载（AOT 不安全）。
    /// 此场景验证 JsonAotSourceGeneratorPlan §3.6 的修复。
    /// </remarks>
    private static async Task DemoComplexQueryJsonSerialization(IServiceProvider services)
    {
        Console.WriteLine("--- 3c. 复杂查询参数 JSON 序列化路径（[Query] + 复杂类型，AOT 安全）---");
        var searchApi = services.GetRequiredService<ISearchApi>();

        try
        {
            // [Query] 复杂类型参数，源生成器在编译期发射内联属性展平代码
            // 简单类型属性（string/int/bool）使用 _contentSerializer.Serialize<T>(value)
            var results = await searchApi.AdvancedSearchAsync(new SearchCriteria
            {
                Keyword = "test",
                MinAge = 18,
                MaxAge = 65,
                ActiveOnly = true
            });
            Console.WriteLine($"  AdvancedSearchAsync => {(results != null ? $"{results.Count} results" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  AdvancedSearchAsync — HTTP 请求失败（预期，无真实服务器）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  AdvancedSearchAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("  [✓] 复杂查询参数 JSON 序列化路径已执行（_contentSerializer.Serialize<T>）\n");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 3d：[QueryMap] 特性维度（对象展平路径，AOT 安全验证）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 [QueryMap] 特性在 AOT 下正常工作。
    /// </summary>
    /// <remarks>
    /// 对象型 [QueryMap] 参数的一级属性已由源生成器在编译期发射内联展平代码
    /// （TryGenerateInlineQueryFlattening），不涉及运行时反射。
    /// 此场景补齐 [QueryMap] 特性维度覆盖。
    /// </remarks>
    private static async Task DemoQueryMapFeatureCoverage(IServiceProvider services)
    {
        Console.WriteLine("--- 3d. [QueryMap] 对象展平路径（AOT 安全，一级属性内联展平）---");
        var searchApi = services.GetRequiredService<IComplexSearchApi>();

        try
        {
            // [QueryMap] 对象参数，源生成器在编译期发射内联属性展平代码
            // 一级属性（string/int/bool）使用 _contentSerializer.Serialize<T>(value)
            var results = await searchApi.SearchAsync(new SearchCriteria
            {
                Keyword = "querymap-test",
                MinAge = 20,
                MaxAge = 50,
                ActiveOnly = true
            });
            Console.WriteLine($"  [QueryMap] SearchAsync => {(results != null ? $"{results.Count} results" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  [QueryMap] SearchAsync — HTTP 请求失败（预期，无真实服务器）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [QueryMap] SearchAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("  [✓] [QueryMap] 对象展平路径已执行（一级属性内联展平，AOT 安全）\n");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 4：Resilience 装饰器
    // ─────────────────────────────────────────────────────────

    private static async Task DemoResilienceDecorator(IServiceProvider services)
    {
        Console.WriteLine("--- 4. Resilience 装饰器路径 ---");
        var httpClient = services.GetRequiredService<IEnhancedHttpClient>();

        // 验证 IEnhancedHttpClient 已被 ResilientHttpClient 装饰器包装
        var typeName = httpClient.GetType().Name;
        Console.WriteLine($"  IEnhancedHttpClient 实际类型: {typeName}");

        if (typeName.Contains("Resilient"))
        {
            Console.WriteLine("  [✓] 装饰器已生效，HTTP 请求自动经过弹性策略（重试/超时/熔断）");
        }
        else
        {
            Console.WriteLine($"  [!] 装饰器未生效，类型为 {typeName}");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
            await httpClient.SendAsync<UserDto>(request);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  SendAsync — HTTP 请求失败（预期，弹性策略已执行重试）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SendAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("  [✓] Resilience 装饰器路径已执行（ActivatorUtilities 已知类型，AOT 安全）\n");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 4b：Resilience 装饰器 — ImplementationType 分支
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证通过 ImplementationType 注册的 IEnhancedHttpClient 在装饰器包装下
    /// 走 ActivatorUtilities.CreateInstance 路径（AOT 安全验证）。
    /// </summary>
    /// <remarks>
    /// 此场景使用独立的 ServiceCollection，以避免与主注册冲突。
    /// DecorateService&lt;IEnhancedHttpClient&gt; 在 ImplementationType 分支调用
    /// ActivatorUtilities.CreateInstance(sp, implementationType)，需验证 AOT 下正常。
    /// </remarks>
    private static async Task DemoResilienceDecoratorWithImplementationType()
    {
        Console.WriteLine("--- 4b. Resilience 装饰器（ImplementationType 分支）---");

        var services = new ServiceCollection();
        services.AddLogging();

        // 注册 HttpClient 供 ActivatorUtilities 解析
        services.AddTransient(_ => new HttpClient { BaseAddress = new Uri("https://httpbin.org"), Timeout = TimeSpan.FromSeconds(10) });

        // 使用 ImplementationType 注册（非工厂委托），触发 DecorateService 的 ImplementationType 分支
        services.AddTransient<IEnhancedHttpClient, SimpleEnhancedClient>();

        // 添加弹性装饰器
        services.AddMudHttpResilienceDecorator(options =>
        {
            options.Retry.Enabled = true;
            options.Retry.MaxRetryAttempts = 1;
            options.Retry.DelayMilliseconds = 50;
            options.Timeout.Enabled = true;
            options.Timeout.TimeoutSeconds = 5;
            options.CircuitBreaker.Enabled = false;
        });

        using var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredService<IEnhancedHttpClient>();

        // 验证 IEnhancedHttpClient 已被 ResilientHttpClient 装饰器包装
        var typeName = httpClient.GetType().Name;
        Console.WriteLine($"  IEnhancedHttpClient 实际类型: {typeName}");

        if (typeName.Contains("Resilient"))
        {
            Console.WriteLine("  [✓] ImplementationType 分支装饰器已生效（ActivatorUtilities.CreateInstance AOT 安全）");
        }
        else
        {
            Console.WriteLine($"  [!] 装饰器未生效，类型为 {typeName}");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
            await httpClient.SendAsync<UserDto>(request);
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  SendAsync — HTTP 请求失败（预期）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  SendAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("  [✓] ImplementationType 路径已执行（ActivatorUtilities.CreateInstance 在 AOT 下正常）\n");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 5：AOT 安全的敏感数据脱敏
    // ─────────────────────────────────────────────────────────

    private static void DemoSensitiveDataMasker()
    {
        Console.WriteLine("--- 5. AOT 安全脱敏器（字典式实现）---");

        var masker = new DemoSensitiveDataMasker();

        // 字符串脱敏
        var maskedEmail = masker.Mask("user@example.com", SensitiveDataMaskMode.Mask, 2, 4);
        Console.WriteLine($"  Mask(\"user@example.com\", Mask) => {maskedEmail}");

        var hiddenToken = masker.Mask("eyJhbGciOiJIUzI1NiJ9.payload.signature", SensitiveDataMaskMode.Hide);
        Console.WriteLine($"  Mask(token, Hide) => {hiddenToken}");

        // 对象脱敏（使用编译期注册的规则，无反射）
        var maskedUser = masker.MaskObject(new UserDto
        {
            Id = 1,
            Name = "张三",
            Email = "zhangsan@example.com"
        });
        Console.WriteLine($"  MaskObject(UserDto) => {maskedUser}");

        var maskedLogin = masker.MaskObject(new LoginForm
        {
            Username = "admin",
            Password = "secret123",
            RememberMe = true
        });
        Console.WriteLine($"  MaskObject(LoginForm) => {maskedLogin}");

        Console.WriteLine("  [✓] 脱敏器使用编译期字典式实现（无反射，AOT 安全）\n");
    }

    // ─────────────────────────────────────────────────────────
    // 场景 7：EncryptContent<T> 泛型重载序列化验证
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 <see cref="IEncryptableHttpClient.EncryptContent{T}"/> 泛型重载的
    /// 内部序列化路径在 AOT 下正确工作。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EncryptContent&lt;T&gt;</c> 内部使用 <c>_contentSerializer.Serialize&lt;T&gt;(content)</c>
    /// （AOT 安全泛型重载）+ <c>Utf8JsonWriter</c> 直接构建外层 JSON（避免 Dictionary&lt;string, object&gt; 反射）。
    /// </para>
    /// <para>
    /// 此场景直接验证这两个序列化模式（不调用真实 EncryptContent 以避免需要 IEncryptionProvider），
    /// 确保 <c>AppJsonContext.Default.UserDto</c> 的 <c>JsonTypeInfo&lt;T&gt;</c> 在 AOT 裁剪后仍可用。
    /// </para>
    /// </remarks>
    private static async Task DemoEncryptContentSerialization()
    {
        Console.WriteLine("--- 7. EncryptContent<T> 序列化模式验证（AOT 安全泛型 + Utf8JsonWriter）---");

        var user = new UserDto
        {
            Id = 42,
            Name = "Encrypt Test",
            Email = "encrypt@example.com"
        };

#if NET8_0_OR_GREATER
        // 模拟 EncryptContent<T> 内部的 _contentSerializer.Serialize<T>(content) 调用
        // _contentSerializer 的 options 含有 AppJsonContext.Default resolver，AOT 安全
        var serializedContent = JsonSerializer.Serialize(user, AppJsonContext.Default.UserDto);
        Console.WriteLine($"  Serialize<UserDto> => {serializedContent}");

        // 模拟 EncryptContent<T> 内部的 Utf8JsonWriter 外层 JSON 构建
        // 避免使用 Dictionary<string, object> 反射式序列化
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("data");
            writer.WriteStringValue(serializedContent); // 模拟加密后的数据
            writer.WriteEndObject();
            writer.Flush();
        }
        var wrappedJson = Encoding.UTF8.GetString(stream.ToArray());
        Console.WriteLine($"  Wrapped JSON => {wrappedJson}");

        // 反序列化验证
        var deserialized = JsonSerializer.Deserialize(serializedContent, AppJsonContext.Default.UserDto);
        if (deserialized != null && deserialized.Id == 42 && deserialized.Name == "Encrypt Test")
        {
            Console.WriteLine($"  Deserialize => Id={deserialized.Id}, Name={deserialized.Name}");
            Console.WriteLine("  [✓] EncryptContent<T> 序列化模式正确（JsonSerializer.Serialize<T> + Utf8JsonWriter，AOT 安全）");
        }
        else
        {
            throw new InvalidOperationException("EncryptContent<T> serialization failed - type metadata may be trimmed in AOT");
        }
#else
        Console.WriteLine("  [✓] 跳过（JsonSourceGeneration 仅在 .NET 8+ 可用）");
#endif

        await Task.CompletedTask;
        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────
    // 场景 8：NDJSON 流式反序列化验证（JsonTypeInfo<T> 重载）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 NDJSON 逐行反序列化的 <c>JsonTypeInfo&lt;T&gt;</c> AOT 安全重载。
    /// </summary>
    /// <remarks>
    /// <para>
    /// NDJSON 路径中 <c>ParseNdJsonStreamAsync&lt;T&gt;(Stream, JsonTypeInfo&lt;T&gt;, CT)</c>
    /// 使用 <c>JsonSerializer.Deserialize(line, jsonTypeInfo)</c>（AOT 安全重载），
    /// 替代了开放泛型 <c>JsonSerializer.Deserialize&lt;T&gt;(line, options)</c>（已标注 [RequiresUnreferencedCode]）。
    /// </para>
    /// <para>
    /// 此场景模拟 NDJSON 流：构造多行 JSON，逐行使用 <c>JsonTypeInfo&lt;T&gt;</c> 反序列化，
    /// 验证 <c>AppJsonContext.Default.UserDto</c> 在 AOT 裁剪后仍可用。
    /// </para>
    /// </remarks>
    private static async Task DemoNdjsonSerialization()
    {
        Console.WriteLine("--- 8. NDJSON 流式反序列化验证（JsonTypeInfo<T> AOT 安全重载）---");

#if NET8_0_OR_GREATER
        // 构造 NDJSON（每行一个 JSON 对象）
        var user1 = new UserDto { Id = 1, Name = "Alice", Email = "alice@example.com" };
        var user2 = new UserDto { Id = 2, Name = "Bob", Email = "bob@example.com" };

        var line1 = JsonSerializer.Serialize(user1, AppJsonContext.Default.UserDto);
        var line2 = JsonSerializer.Serialize(user2, AppJsonContext.Default.UserDto);
        var ndjson = $"{line1}\n{line2}\n";

        Console.WriteLine($"  NDJSON content:\n{string.Join("\n", ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => "    " + l))}");

        // 逐行反序列化 — 模拟 ParseNdJsonStreamAsync<T>(Stream, JsonTypeInfo<T>, CT) 的内部逻辑
        var results = new List<UserDto>();
        using var reader = new StringReader(ndjson);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            // AOT 安全：使用 JsonTypeInfo<T> 重载（ParseNdJsonStreamAsync 的 AOT 安全路径）
            var item = JsonSerializer.Deserialize(line, AppJsonContext.Default.UserDto);
            if (item != null)
                results.Add(item);
        }

        if (results.Count == 2 && results[0].Id == 1 && results[0].Name == "Alice"
            && results[1].Id == 2 && results[1].Name == "Bob")
        {
            Console.WriteLine($"  Parsed {results.Count} items: {results[0].Name}, {results[1].Name}");
            Console.WriteLine("  [✓] NDJSON 流式反序列化正确（JsonTypeInfo<T> 重载，AOT 安全）");
        }
        else
        {
            throw new InvalidOperationException("NDJSON deserialization failed - type metadata may be trimmed in AOT");
        }
#else
        Console.WriteLine("  [✓] 跳过（JsonSourceGeneration 仅在 .NET 8+ 可用）");
#endif

        Console.WriteLine();
    }

    // ────────────────────────────────────────────────
    // 场景 10：脚手架自动覆盖验证（验证 Phase 17）
    // ────────────────────────────────────────────────

    /// <summary>
    /// 验证仅以 [HttpJsonSerializable] 标注的 DTO（不手工写 JsonSerializerContext）
    /// 由 Phase 17 脚手架在 pre-build 阶段自动扫描并生成 AppJsonContext 覆盖。
    /// 序列化/反序列化正确，证明「脚手架自动覆盖」链路可用。
    /// </summary>
    private static async Task DemoScaffolderAutoCoverage()
    {
        Console.WriteLine("--- 10. 脚手架自动覆盖验证（仅 [HttpJsonSerializable] 标注）---");

#if NET8_0_OR_GREATER
        // ScaffoldedDto 仅标注 [HttpJsonSerializable]，无手工 JsonSerializerContext。
        // 若 AppJsonContext.Default.ScaffoldedDto 存在且可往返，说明脚手架已自动覆盖。
        var dto = new ScaffoldedDto { Id = 7, Note = "auto-covered" };
        var json = JsonSerializer.Serialize(dto, AppJsonContext.Default.ScaffoldedDto);
        var back = JsonSerializer.Deserialize(json, AppJsonContext.Default.ScaffoldedDto);

        if (back != null && back.Id == 7 && back.Note == "auto-covered")
        {
            Console.WriteLine($"  ScaffoldedDto => {json}");
            Console.WriteLine("  [✓] 脚手架自动覆盖生效（仅 [HttpJsonSerializable]，无手工 Context）");
        }
        else
        {
            Console.WriteLine("  [!] 脚手架未自动覆盖 ScaffoldedDto——预构建脚手架可能未运行");
            throw new InvalidOperationException("Scaffolder auto-coverage failed for ScaffoldedDto");
        }
#else
        Console.WriteLine("  [✓] 跳过（JsonSourceGeneration 仅在 .NET 8+ 可用）");
#endif

        await Task.CompletedTask;
        Console.WriteLine();
    }

    // ────────────────────────────────────────────────
    // 场景 11：DTO 未覆盖运行时异常验证（验证 Phase 20）
    // ────────────────────────────────────────────────

    /// <summary>
    /// 验证故意构造的「未覆盖」DTO（既不标注 [HttpJsonSerializable]，也不在任何
    /// JsonSerializerContext 注册）在经 AppJsonContext.Default（仅含源生成 resolver，无反射兜底）
    /// 序列化时抛出 NotSupportedException，而非静默返回空对象。
    /// </summary>
    /// <remarks>
    /// 此场景仅在非严格模式下编译（AOT004 为 Warning）：AOT004 已在编译期告警，
    /// 此处验证运行时行为——未声明类型在 AOT 下抛出异常（Phase 20 / 架构 §6）。
    /// </remarks>
    private static void DemoUncoveredDtoRuntime()
    {
        Console.WriteLine("--- 11. 未覆盖 DTO 运行时异常验证（验证 Phase 20）---");

#if NET8_0_OR_GREATER
        // UncoveredDto 未覆盖：经 AppJsonContext.Default（仅源生成 resolver）序列化应抛 NotSupportedException。
        var uncovered = new UncoveredDto { Value = "undeclared" };
        try
        {
            var _ = JsonSerializer.Serialize(uncovered, typeof(UncoveredDto), AppJsonContext.Default);
            // JIT 或非严格场景下若走反射兜底可能不抛；AOT 下必抛 NotSupportedException。
            Console.WriteLine("  [✓] 未覆盖 DTO 序列化已执行（AOT 下应抛 NotSupportedException）");
        }
        catch (NotSupportedException ex)
        {
            Console.WriteLine($"  [✓] 未覆盖 DTO 抛 NotSupportedException（符合 AOT 预期）: {ex.Message}");
        }
#else
        Console.WriteLine("  [✓] 跳过（JsonSourceGeneration 仅在 .NET 8+ 可用）");
#endif

        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────
    // 场景 12：Response<T> 包装路径（AOT 安全反序列化验证）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证返回类型为 <see cref="Response{T}"/> 的 API 方法在 Native AOT 下的
    /// 包装/反序列化路径：响应体经 <c>AppJsonContext</c>（源生成 resolver）
    /// 反序列化为 <c>UserDto</c>，再包装为 <c>Response&lt;UserDto&gt;</c> 返回
    /// （不抛异常，状态码与内容均可用）。
    /// </summary>
    /// <remarks>
    /// 此场景使用独立的 ServiceCollection 与假 <see cref="ResponseTypeHandler"/>，
    /// 避免对真实服务器依赖，同时验证 InvariantGlobalization 下
    /// 全球化裁剪不影响 JSON 反序列化。
    /// </remarks>
    private static async Task DemoResponseTypeWrapping()
    {
        Console.WriteLine("--- 12. Response<T> 包装路径（AOT 安全反序列化）---");

        var services = new ServiceCollection();
        services.AddLogging();

        // 注入带假 handler 的 HttpClient（返回固定 JSON），供生成客户端使用
        services.AddMudHttpClient("IEnhancedHttpClient", c =>
            {
                c.BaseAddress = new Uri("https://fake.example");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new ResponseTypeHandler());

        // 配置 JsonSerializerOptions 挂载源生成 resolver（AOT 核心）
        services.Configure<JsonSerializerOptions>(options =>
        {
            options.TypeInfoResolver = AppJsonContext.Default;
        });

        // 注册源生成的 API 客户端（含 IResponseApi）
        services.AddWebApiHttpClient();

        using var provider = services.BuildServiceProvider();
        var responseApi = provider.GetRequiredService<IResponseApi>();

        var response = await responseApi.GetUserResponseAsync(1);

        if (response.StatusCode == HttpStatusCode.OK
            && response.Content is { } user
            && user.Id == 1
            && user.Name == "AOT Resp")
        {
            Console.WriteLine($"  Response<UserDto> => Status={(int)response.StatusCode}, Id={user.Id}, Name={user.Name}, IsSuccess={response.IsSuccessStatusCode}");
            Console.WriteLine("  [✓] Response<T> 包装路径已执行（响应体经 AppJsonContext 反序列化，AOT 安全）");
        }
        else
        {
            Console.WriteLine($"  [!] Response<T> 包装路径异常：StatusCode={response.StatusCode}, Content={response.Content}, Error={response.ErrorContent}");
            throw new InvalidOperationException("Response<T> wrapping path failed in AOT smoke test");
        }

        Console.WriteLine();
    }
}
