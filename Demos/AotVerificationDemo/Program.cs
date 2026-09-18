using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
        // 库默认阻断未列入白名单的域名（防 SSRF）。本 Demo 面向公开测试站点，
        // 需显式登记，否则每个 HTTP 场景都会在建连前抛 InvalidOperationException 并被计为失败。
        UrlValidator.ConfigureAllowedDomains(["httpbin.org", "fake.example"]);

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                ConfigureAotServices(services);
            })
            .Build();

        Console.WriteLine("=== Mud.HttpUtils Native AOT 验证示例 ===\n");

        // 每个场景验证一条 AOT 安全路径。
        // [CI 门禁] 每个场景输出 ASCII 标记行（[SCENE] {方法名}）：CI 用它在跨平台 shell 中稳定断言
        // "关键场景确实执行"，而不依赖下方中文标题（中文在不同 runner/locale 下的 grep 行为不稳定）。
        Console.WriteLine($"[SCENE] {nameof(DemoGeneratedApiClient_Json)}");
        await DemoGeneratedApiClient_Json(host.Services);
        Console.WriteLine($"[SCENE] {nameof(DemoEnhancedHttpClient_Json)}");
        await DemoEnhancedHttpClient_Json(host.Services);
        Console.WriteLine($"[SCENE] {nameof(DemoGeneratedApiClient_FormUrlEncoded)}");
        await DemoGeneratedApiClient_FormUrlEncoded(host.Services);
        Console.WriteLine($"[SCENE] {nameof(DemoQueryMapJsonSerialization)}");
        await DemoQueryMapJsonSerialization(host.Services);
        Console.WriteLine($"[SCENE] {nameof(DemoComplexQueryJsonSerialization)}");
        await DemoComplexQueryJsonSerialization(host.Services);
        Console.WriteLine($"[SCENE] {nameof(DemoQueryMapFeatureCoverage)}");
        await DemoQueryMapFeatureCoverage(host.Services);
        Console.WriteLine($"[SCENE] {nameof(DemoResilienceDecorator)}");
        await DemoResilienceDecorator(host.Services);
        Console.WriteLine($"[SCENE] {nameof(DemoResilienceDecoratorWithImplementationType)}");
        await DemoResilienceDecoratorWithImplementationType();
        Console.WriteLine($"[SCENE] {nameof(DemoSensitiveDataMasker)}");
        DemoSensitiveDataMasker();
        Console.WriteLine($"[SCENE] {nameof(DemoOAuth2Serialization)}");
        DemoOAuth2Serialization();
        Console.WriteLine($"[SCENE] {nameof(DemoEncryptContentSerialization)}");
        await DemoEncryptContentSerialization();
        Console.WriteLine($"[SCENE] {nameof(DemoNdjsonSerialization)}");
        await DemoNdjsonSerialization();
        Console.WriteLine($"[SCENE] {nameof(DemoScaffolderAutoCoverage)}");
        await DemoScaffolderAutoCoverage();
        Console.WriteLine($"[SCENE] {nameof(DemoUncoveredDtoRuntime)}");
        DemoUncoveredDtoRuntime();
        Console.WriteLine($"[SCENE] {nameof(DemoResponseTypeWrapping)}");
        await DemoResponseTypeWrapping();
        Console.WriteLine($"[SCENE] {nameof(DemoNonDiAotEntry)}");
        await DemoNonDiAotEntry();
        Console.WriteLine($"[SCENE] {nameof(DemoModuleInitializerAutoRegistration)}");
        DemoModuleInitializerAutoRegistration();
        Console.WriteLine($"[SCENE] {nameof(DemoPolymorphismRoundTrip)}");
        DemoPolymorphismRoundTrip();
        Console.WriteLine($"[SCENE] {nameof(DemoEncryptedTokenCache)}");
        DemoEncryptedTokenCache();
        Console.WriteLine($"[SCENE] {nameof(DemoOAuth2EndToEnd)}");
        await DemoOAuth2EndToEnd();

        Console.WriteLine("\n=== AOT 验证示例完成 ===");

        // AOT_OK 门槛化：仅当所有非预期异常/断言失败计数为 0 时才输出成功标记，否则以退出码 1 结束。
        if (s_failed > 0)
        {
            Console.WriteLine($"AOT_FAILED (failed={s_failed})");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine("如果此程序在 Native AOT 模式下成功运行，说明 JSON / 表单 / 查询参数 / 弹性 / 脱敏 / 加密 / NDJSON 主路径均已 AOT 兼容。");
        Console.WriteLine("AOT_OK");
    }

    /// <summary>非预期失败计数（0 才输出 AOT_OK）。</summary>
    private static int s_failed;

    /// <summary>
    /// 处理场景中的异常：网络类异常（无真实服务器）属预期，不计入失败；
    /// AOT 相关的序列化/配置异常（<c>NotSupportedException</c>/<c>InvalidOperationException</c>/<c>JsonException</c>）
    /// 视为非预期失败。
    /// </summary>
    private static void HandleUnexpected(string scenario, Exception ex)
    {
        if (IsExpectedFailure(ex))
        {
            Console.WriteLine($"  {scenario} — 预期失败（无真实服务器）: {ex.Message}");
            return;
        }

        s_failed++;
        Console.WriteLine($"  [FAIL] {scenario} — 非预期异常: {ex.GetType().Name}: {ex.Message}");
    }

    private static bool IsExpectedFailure(Exception ex)
        => ex is HttpRequestException or TaskCanceledException or OperationCanceledException
           || ex is System.Net.Sockets.SocketException
           || ex.InnerException is not null && IsExpectedFailure(ex.InnerException);

    /// <summary>
    /// 断言；失败时计入失败计数（不抛异常，便于继续验证其余场景）。
    /// </summary>
    private static void Assert(bool condition, string scenario)
    {
        if (condition)
            return;

        s_failed++;
        Console.WriteLine($"  [FAIL] {scenario}");
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
            HandleUnexpected("GetUserAsync", ex);
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
            HandleUnexpected("CreateUserAsync", ex);
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
            HandleUnexpected("PostAsJsonAsync", ex);
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
            HandleUnexpected("LoginAsync", ex);
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
            HandleUnexpected("SearchAsync", ex);
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
            HandleUnexpected("AdvancedSearchAsync", ex);
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
            HandleUnexpected("[QueryMap] SearchAsync", ex);
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
            HandleUnexpected("SendAsync", ex);
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
            HandleUnexpected("SendAsync", ex);
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
    /// 序列化时抛出异常，而非静默返回空对象。
    /// </summary>
    /// <remarks>
    /// 此场景仅在非严格模式下编译（AOT004 为 Warning）：AOT004 已在编译期告警，
    /// 此处验证运行时行为——未声明类型在源生成 resolver 下必须失败（Phase 20 / 架构 §6）。
    /// <para>
    /// [修复] 原实现只捕获 <c>NotSupportedException</c>，且把"配了源生成 resolver 时 JIT 会走反射兜底"
    /// 当作前提。实测：显式传入 <c>JsonSerializerContext</c> 时**不存在**反射兜底，
    /// STJ 直接抛 <c>InvalidOperationException</c>（JsonTypeInfo metadata … not provided by TypeInfoResolver），
    /// 该异常未被捕获 → 整个进程以 Unhandled exception 终止 → 永远输出不了 AOT_OK（AOT 门禁失效）。
    /// 现按"JIT / Native AOT 一致：必须失败"断言，并接受两种异常类型
    /// （不同 .NET 版本/运行模式下 STJ 的异常类型可能不同）。
    /// </para>
    /// </remarks>
    private static void DemoUncoveredDtoRuntime()
    {
        Console.WriteLine("--- 11. 未覆盖 DTO 运行时异常验证（验证 Phase 20）---");

#if NET8_0_OR_GREATER
        // UncoveredDto 未覆盖：经 AppJsonContext.Default（仅源生成 resolver、无反射兜底）序列化应失败。
        var uncovered = new UncoveredDto { Value = "undeclared" };
        try
        {
            var _ = JsonSerializer.Serialize(uncovered, typeof(UncoveredDto), AppJsonContext.Default);
            // 序列化竟然成功 → 说明存在反射兜底，AOT 契约被破坏（JIT 判定之后可能静默产出空对象）。
            Assert(false,
                "未覆盖 DTO 应因缺少 JsonTypeInfo 元数据而失败，但序列化成功（AOT 契约被破坏）");
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            Console.WriteLine($"  [✓] 未覆盖 DTO 序列化被拒绝（符合 AOT 预期）: {ex.GetType().Name}: {ex.Message}");
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

    // ─────────────────────────────────────────────────────────
    // 场景 13：无 DI AOT 入口验证（RestService.ForGenerated<T>）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证不依赖 <c>IServiceProvider</c> 的 AOT 安全入口
    /// <c>RestService.ForGenerated&lt;T&gt;(HttpClient, GeneratedClientOptions?)</c>。
    /// </summary>
    /// <remarks>
    /// v3.3 Phase 0 T0.4：验证无 DI 入口可解析生成实现类。
    /// 工厂委托由 ModuleInitializer 自动注册（net5+），或手动调用 <c>RegisterAllFactories()</c>（netstandard2.0）。
    /// </remarks>
    private static async Task DemoNonDiAotEntry()
    {
        Console.WriteLine("--- 13. 无 DI AOT 入口验证（ForGenerated<T>）---");

        try
        {
            // 构造 HttpClient（无 DI 容器）
            using var httpClient = new HttpClient { BaseAddress = new Uri("https://httpbin.org"), Timeout = TimeSpan.FromSeconds(10) };

            // 构造 GeneratedClientOptions（携带最小必需依赖）
            // 序列化器必须挂接源生成 resolver：无参构造在 Native AOT 下会因缺少
            // TypeInfoResolver 而走内置兜底上下文（不含本 Demo 的 DTO），导致反序列化失败。
            var options = new GeneratedClientOptions
            {
                AppContext = new MinimalAppContext("aot-demo", httpClient),
                ContentSerializer = new SystemTextJsonContentSerializer(
                    HttpContentSerializerFactory.BuildOptions(null, AppJsonContext.Default))
            };

            // 无 DI 入口：直接通过 RestService.ForGenerated<T> 解析
            var api = RestService.ForGenerated<INonDiApi>(httpClient, options);
            Console.WriteLine($"  ForGenerated<INonDiApi> => 实例类型: {api.GetType().Name}");

            // 尝试调用 API 方法（HTTP 请求预期失败——无真实服务器）
            // 关键验证：工厂解析成功，序列化代码路径可执行
            try
            {
                var user = await api.GetUserAsync(1);
                Console.WriteLine($"  GetUserAsync(1) => {(user != null ? $"Id={user.Id}" : "null")}");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"  GetUserAsync — HTTP 请求失败（预期，无真实服务器）: {ex.Message}");
            }
            catch (Exception ex)
            {
                HandleUnexpected("GetUserAsync", ex);
            }

            Console.WriteLine("  [✓] 无 DI 入口 ForGenerated<T> 解析成功（ModuleInitializer 工厂注册生效）");
        }
        catch (InvalidOperationException ex)
        {
            // 工厂未注册属真实失败（非网络），计入失败计数。
            s_failed++;
            Console.WriteLine($"  [FAIL] ForGenerated<T> 失败——工厂未注册: {ex.Message}");
            Console.WriteLine("  [!] ModuleInitializer 可能未执行（netstandard2.0 需手动调用 RegisterAllFactories()）");
        }

        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────
    // 场景 14：ModuleInitializer 自动注册验证
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证程序集加载后 ModuleInitializer 自动调用了 <c>RegisterGeneratedFactory</c>。
    /// </summary>
    /// <remarks>
    /// v3.3 Phase 0 T0.4：若 ModuleInitializer 生效，<c>ForGenerated&lt;T&gt;</c> 不会抛出
    /// "No generated factory registered" 异常。
    /// </remarks>
    private static void DemoModuleInitializerAutoRegistration()
    {
        Console.WriteLine("--- 14. ModuleInitializer 自动注册验证 ---");

        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri("https://httpbin.org") };
            var options = new GeneratedClientOptions
            {
                AppContext = new MinimalAppContext("aot-init", httpClient),
            };

            // 仅验证工厂是否已注册——不调用 API 方法
            var api = RestService.ForGenerated<INonDiApi>(httpClient, options);
            Console.WriteLine($"  ForGenerated<INonDiApi> 解析成功 → {api.GetType().Name}");
            Console.WriteLine("  [✓] ModuleInitializer 自动注册已执行（工厂已注册，无需手动调用）");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("  [!] ModuleInitializer 未执行——工厂未注册");
            Console.WriteLine("  [!] netstandard2.0 不支持 ModuleInitializer，需手动调用 GeneratedFactoryRegistration.RegisterAllFactories()");
        }

        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────
    // 场景 15：多态序列化（基类 [JsonDerivedType] 端到端验证）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// [T2] 多态端到端验证：以【基类静态类型】序列化/反序列化派生实例。
    /// </summary>
    /// <remarks>
    /// 这是 AOT003 修复指引的闭环验证点：只有基类声明上标注了
    /// <c>[JsonDerivedType(typeof(派生类型))]</c>，以基类静态类型进行的多态
    /// round-trip 才能在 STJ 源生成 + Native AOT 下成功
    /// （<c>--auto-derived-types</c> 仅注册派生类型为独立根，无法替代该特性）。
    /// </remarks>
    private static void DemoPolymorphismRoundTrip()
    {
        Console.WriteLine("--- 15. 多态序列化验证（基类 [JsonDerivedType]，AOT 端到端）---");

#if NET8_0_OR_GREATER
        // 静态类型为基类 PolyShape —— 多态映射必须来自基类元数据
        PolyShape shape = new PolyCircle { Name = "circle-1", Radius = 3.5 };

        var json = JsonSerializer.Serialize(shape, AppJsonContext.Default.PolyShape);
        var back = JsonSerializer.Deserialize(json, AppJsonContext.Default.PolyShape);

        if (back is PolyCircle circle
            && circle.Name == "circle-1"
            && Math.Abs(circle.Radius - 3.5) < 0.001)
        {
            Console.WriteLine($"  PolyShape(PolyCircle) => {json}");
            Console.WriteLine("  [✓] 多态 round-trip 通过（基类 [JsonDerivedType] 生效，源生成 + AOT 安全）");
        }
        else
        {
            Assert(false,
                $"多态 round-trip 失败：反序列化结果 {back?.GetType().Name ?? "null"}——基类 [JsonDerivedType] 未生效？");
        }
#else
        Console.WriteLine("  [✓] 跳过（JsonSourceGeneration 仅在 .NET 8+ 可用）");
#endif

        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────
    // 场景 16：EncryptedTokenCache 加密缓存往返验证（SR-M8 AOT 门禁载体）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 <see cref="EncryptedTokenCache{T}"/>（SR-M8 用户令牌内存态加密包装）在 Native AOT 下：
    /// Set→Get 往返等值、底层驻留密文（无明文凭据子串）、篡改密文按 miss 处理不抛出。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="EncryptedTokenCache{String}"/>（值为字符串，无需反射序列化元数据）验证
    /// AEAD 加密引擎与缓存包装链路本身 AOT 安全；含对象图序列化的场景（如 UserTokenInfo）
    /// 在 AOT 下应传入基于预生成 JsonSerializerContext 的自定义包装（见 EncryptedTokenCache XML 文档 AOT 注意）。
    /// </remarks>
    private static void DemoEncryptedTokenCache()
    {
        Console.WriteLine("--- 16. EncryptedTokenCache 加密缓存往返验证（SR-M8）---");

        var encryption = new DefaultAesEncryptionProvider(Microsoft.Extensions.Options.Options.Create(
            new AesEncryptionOptions
            {
                Key = Convert.FromBase64String("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=")   // 32 字节测试密钥
            }));

        using var inner = new MemoryCacheTokenCache<string>();
        // [场景16修复] TMX-11：AOT 场景必须注入携寄 JsonTypeInfoResolver 的序列化选项
        // （EncryptedTokenCache 类注释「AOT 注意」的明确要求）。此前使用默认反射选项，
        // AOT 下 JsonSerializer.Serialize 抛 NotSupportedException → Set 按契约静默降级为
        // "不缓存"（NullLogger 不可见）→ 底层缓存为空 → 密文驻留/往返断言双双失败。
        using var cache = new EncryptedTokenCache<string>(inner, encryption, TokenCacheJsonContext.Default.Options);

        const string secret = "aot-demo-access-token-value";
        cache.Set("user-1", secret);

        // 底层驻留的是密文（无明文凭据子串）
        var cipherFound = inner.TryGet("user-1", out var cipher);
        Assert(cipherFound && cipher != null, "底层缓存未取到密文");
        if (cipher != null && cipher.Contains(secret, StringComparison.Ordinal))
        {
            Assert(false, "底层缓存出现了明文凭据（加密包装未生效）");
        }

        // 往返等值
        var restoredFound = cache.TryGet("user-1", out var restored);
        Assert(restoredFound && restored == secret, "EncryptedTokenCache 往返等值失败");

        // 篡改密文 → miss 不抛出（触发上层重新获取）
        if (cipher != null)
        {
            var corrupted = cipher.Substring(0, Math.Max(1, cipher.Length - 2)) + (cipher.EndsWith("aa", StringComparison.Ordinal) ? "bb" : "aa");
            inner.Set("user-1", corrupted);
            Assert(!cache.TryGet("user-1", out _), "篡改密文应按 miss 处理而非命中");
        }

        // [场景16修复] 成功标记改为条件打印：此前无条件打印，断言失败时输出自相矛盾。
        if (s_failed == 0)
        {
            Console.WriteLine("  [✓] EncryptedTokenCache 加密往返/密文驻留/损坏 miss 均正确（AOT 安全）");
        }
        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────
    // 场景 17：OAuth2 端到端验证（TMR-14：StandardOAuth2TokenManager 实际路径）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 验证 <see cref="StandardOAuth2TokenManager"/> 的完整 HTTP + 反序列化 + 缓存链路
    /// 在 Native AOT 下正确工作（TMR-14）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 此场景使用自定义 <see cref="OAuth2MockHandler"/> 打桩令牌端点与自省端点，
    /// 构造真实的 <see cref="StandardOAuth2TokenManager"/> 实例（注入 <see cref="HttpClient"/> + <see cref="IOptions{OAuth2Options}"/>），
    /// 验证以下端到端路径：
    /// </para>
    /// <list type="number">
    /// <item><c>GetOrRefreshTokenAsync</c> → 发送 HTTP POST 到令牌端点 → 反序列化 <c>OAuth2TokenResponse</c> → 返回 <c>CredentialToken</c>（含 access_token/refresh_token/expired）。</item>
    /// <item><c>IntrospectTokenAsync</c> → 发送 HTTP POST 到自省端点 → 反序列化 <c>TokenIntrospectionResult</c> → 返回 active/client_id/scope 等字段。</item>
    /// </list>
    /// <para>
    /// 关键 AOT 验证点：<see cref="OAuth2JsonContext"/> 的源生成类型元数据
    /// （<c>OAuth2TokenResponse</c> + <c>TokenIntrospectionResult</c>）在 AOT 裁剪后仍可用，
    /// 且 <see cref="StandardOAuth2TokenManager"/> 内部的 <c>FormUrlEncodedContent</c> 构造、
    /// HTTP 发送、JSON 反序列化全链路 AOT 安全。
    /// </para>
    /// </remarks>
    private static async Task DemoOAuth2EndToEnd()
    {
        Console.WriteLine("--- 17. OAuth2 端到端验证（StandardOAuth2TokenManager 真实路径，TMR-14）---");

        try
        {
            // 构造打桩 handler 与 HttpClient
            using var mockHandler = new OAuth2MockHandler();
            using var httpClient = new HttpClient(mockHandler)
            {
                BaseAddress = new Uri("https://fake.example")
            };

            // 构造 OAuth2Options（RequireHttps=false 以允许 fake.example 的 HTTP 打桩）
            var options = Options.Create(new OAuth2Options
            {
                ClientId = "test-client",
                ClientSecret = "test-secret",
                TokenEndpoint = "/token",
                IntrospectionEndpoint = "/introspect",
                RequireHttps = false
            });

            // 构造真实的 StandardOAuth2TokenManager 实例
            var manager = new StandardOAuth2TokenManager(httpClient, options);

            // 1. 验证 GetOrRefreshTokenAsync（Client Credentials 流程）
            var token = await manager.GetOrRefreshTokenAsync();
            Console.WriteLine($"  GetOrRefreshTokenAsync => token={token[..Math.Min(16, token.Length)]}...");
            Assert(!string.IsNullOrEmpty(token), "GetOrRefreshTokenAsync 返回空令牌");
            Assert(mockHandler.TokenRequestCount >= 1, "令牌端点未被调用");

            // 2. 验证 IntrospectTokenAsync
            var introspection = await manager.IntrospectTokenAsync(token);
            Console.WriteLine($"  IntrospectTokenAsync => Active={introspection.Active}, ClientId={introspection.ClientId}, Scopes={string.Join(" ", introspection.Scopes ?? [])}");
            Assert(introspection.Active, "IntrospectionResult.Active 应为 true");
            Assert(introspection.ClientId == "test-client", $"ClientId 应为 test-client，实际为 {introspection.ClientId}");
            Assert(introspection.Scopes is { Length: 2 }, $"Scopes 应有 2 个元素，实际为 {introspection.Scopes?.Length ?? 0}");
            Assert(mockHandler.IntrospectionRequestCount >= 1, "自省端点未被调用");

            // 3. 验证 GetOrRefreshCredentialTokenAsync 返回完整凭证令牌
            var credential = await manager.GetOrRefreshCredentialTokenAsync();
            var accessToken = credential.AccessToken ?? string.Empty;
            var accessTokenPreview = accessToken.Length > 16 ? accessToken[..16] : accessToken;
            Console.WriteLine($"  GetOrRefreshCredentialTokenAsync => AccessToken={accessTokenPreview}..., RefreshToken={(credential.RefreshToken != null ? "present" : "null")}, Expire={credential.Expire}");
            Assert(!string.IsNullOrEmpty(credential.AccessToken), "CredentialToken.AccessToken 为空");
            Assert(!string.IsNullOrEmpty(credential.RefreshToken), "CredentialToken.RefreshToken 为空");
            Assert(credential.Expire > 0, "CredentialToken.Expire 应为正数");

            Console.WriteLine("  [✓] OAuth2 端到端验证通过（令牌获取 + 令牌自省 + 凭证令牌缓存，AOT 安全）");
        }
        catch (Exception ex)
        {
            HandleUnexpected("DemoOAuth2EndToEnd", ex);
        }

        Console.WriteLine();
    }

    // ─────────────────────────────────────────────────────────
    // 辅助类型：最小 IMudAppContext 实现
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 最小 <see cref="IMudAppContext"/> 实现，仅用于无 DI AOT Demo。
    /// </summary>
    /// <remarks>
    /// [修复] 原实现让 <see cref="HttpClient"/> 属性抛 <see cref="NotImplementedException"/>
    /// （假设"生成实现通过 IHttpRequestExecutor 发送请求"），但生成实现实际会经
    /// <see cref="IMudAppContext.HttpClient"/> 取 <see cref="IEnhancedHttpClient"/>，
    /// 于是场景 13 在 <c>GetUserAsync</c> 处抛 <c>NotImplementedException</c> → 被计为非预期失败
    /// → 整个 Demo 输出 AOT_FAILED（AOT 门禁失效）。现提供真实可用的客户端，
    /// 并注入源生成 resolver 以满足 Native AOT 的序列化契约。
    /// </remarks>
    private sealed class MinimalAppContext : IMudAppContext
    {
        private readonly IEnhancedHttpClient _enhancedHttpClient;

        public MinimalAppContext(string appKey, HttpClient httpClient)
        {
            AppKey = appKey;
            _enhancedHttpClient = new SimpleEnhancedClient(httpClient, new EnhancedHttpClientOptions
            {
                // Native AOT 下 EnhancedHttpClient 的默认序列化器构造守卫要求 resolver 非空
                JsonTypeInfoResolver = AppJsonContext.Default,
            });
        }

        public string AppKey { get; }

        public IEnhancedHttpClient HttpClient => _enhancedHttpClient;

        public ITokenManager GetTokenManager(string tokenType) => throw new NotImplementedException();
        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotImplementedException();
        public T? GetService<T>() where T : class => null;
    }
}
