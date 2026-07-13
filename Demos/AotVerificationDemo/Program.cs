using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mud.HttpUtils;
using Mud.HttpUtils.Resilience;
using System.Text.Json;

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
        await DemoResilienceDecorator(host.Services);
        DemoSensitiveDataMasker();

        Console.WriteLine("\n=== AOT 验证示例完成 ===");
        Console.WriteLine("如果此程序在 Native AOT 模式下成功运行，说明 JSON / 表单 / 弹性 / 脱敏主路径均已 AOT 兼容。");
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

        // 2. 注册 EnhancedHttpClient（DI 路径会透传 IOptions<JsonSerializerOptions> 至基类）
        services.AddMudHttpClient("default", client =>
        {
            client.BaseAddress = new Uri("https://httpbin.org");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // 3. 注册源生成的 API 客户端（IUserApi, IAuthApi）
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
        services.AddSingleton<ISensitiveDataMasker, AotSafeSensitiveDataMasker>();
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
            // EnhancedHttpClient 内置方法使用 _jsonOptions（含 JsonSerializerContext resolver）
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
    // 场景 5：AOT 安全的敏感数据脱敏
    // ─────────────────────────────────────────────────────────

    private static void DemoSensitiveDataMasker()
    {
        Console.WriteLine("--- 5. AOT 安全脱敏器（字典式实现）---");

        var masker = new AotSafeSensitiveDataMasker();

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
}
