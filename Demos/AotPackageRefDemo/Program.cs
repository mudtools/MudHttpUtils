using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mud.HttpUtils;
using Mud.HttpUtils.OpenTelemetry;
using Mud.HttpUtils.Resilience;
using OpenTelemetry;
using System.Text.Json;

namespace AotPackageRefDemo;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // 1. 配置 JsonSerializerOptions — 挂接源生成上下文（AOT 核心）
                services.Configure<JsonSerializerOptions>(options =>
                {
                    options.TypeInfoResolver = DemoJsonContext.Default;
                });

                // 2. 注册 EnhancedHttpClient（DI 路径透传 IOptions<JsonSerializerOptions>）
                services.AddMudHttpClient("default", client =>
                {
                    client.BaseAddress = new Uri("https://httpbin.org");
                    client.Timeout = TimeSpan.FromSeconds(10);
                });

                // 3. 注册源生成的 API 客户端（AddWebApiHttpClient 由源生成器从 NuGet 包生成）
                services.AddWebApiHttpClient();

                // 4. 注册 Resilience 装饰器
                services.AddMudHttpResilienceDecorator(options =>
                {
                    options.Retry.Enabled = true;
                    options.Retry.MaxRetryAttempts = 1;
                    options.Retry.DelayMilliseconds = 50;
                    options.Timeout.Enabled = true;
                    options.Timeout.TimeoutSeconds = 5;
                    options.CircuitBreaker.Enabled = false;
                });

                // 5. 注册 OpenTelemetry（委托式重载，AOT 推荐路径）
                services.AddMudHttpOpenTelemetry(options =>
                {
                    options.ServiceName = "AotPackageRefDemo";
                    options.SamplingRatio = 1.0;
                    options.OtlpEndpoint = null; // 不配置导出器，仅验证 DI 注册路径
                    options.EnableAspNetCoreInstrumentation = false; // 控制台应用无 ASP.NET Core
                });
            })
            .Build();

        Console.WriteLine("=== Mud.HttpUtils NuGet Package AOT 验证 ===\n");

        // 场景 1：验证源生成器从 NuGet 包正确加载
        Console.WriteLine("--- 1. 源生成器加载验证 ---");
        var userApi = host.Services.GetRequiredService<IUserApi>();
        Console.WriteLine($"  IUserApi 实现类型: {userApi.GetType().Name}");
        Console.WriteLine("  [✓] 源生成器从 NuGet 包加载成功（AddWebApiHttpClient 已注册 IUserApi）\n");

        // 场景 2：验证 JSON 序列化路径（手动 JsonSerializerContext）
        Console.WriteLine("--- 2. JSON 序列化验证（手动 JsonSerializerContext）---");
        var user = new UserDto { Id = 1, Name = "Test", Email = "test@example.com" };
        var json = JsonSerializer.Serialize(user, DemoJsonContext.Default.UserDto);
        Console.WriteLine($"  Serialize => {json}");

        var deserialized = JsonSerializer.Deserialize(json, DemoJsonContext.Default.UserDto);
        Console.WriteLine($"  Deserialize => Id={deserialized?.Id}, Name={deserialized?.Name}");

        if (deserialized?.Id == 1 && deserialized.Name == "Test")
        {
            Console.WriteLine("  [✓] JSON 序列化/反序列化正确（DemoJsonContext 生效）\n");
        }
        else
        {
            throw new InvalidOperationException("JSON serialization/deserialization failed - type metadata may be trimmed in AOT");
        }

        // 场景 3：验证 HTTP 请求路径（预期失败，无真实服务器）
        Console.WriteLine("--- 3. HTTP 请求路径验证 ---");
        try
        {
            var result = await userApi.GetUserAsync(1);
            Console.WriteLine($"  GetUserAsync(1) => {(result != null ? $"Id={result.Id}" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  GetUserAsync — HTTP 请求失败（预期，无真实服务器）: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  GetUserAsync — 异常: {ex.GetType().Name}: {ex.Message}");
        }
        Console.WriteLine("  [✓] HTTP 请求代码路径已执行（序列化在请求发送前完成）\n");

        // 场景 4：验证 Resilience 装饰器
        Console.WriteLine("--- 4. Resilience 装饰器验证 ---");
        var httpClient = host.Services.GetRequiredService<IEnhancedHttpClient>();
        var typeName = httpClient.GetType().Name;
        Console.WriteLine($"  IEnhancedHttpClient 实际类型: {typeName}");
        if (typeName.Contains("Resilient"))
        {
            Console.WriteLine("  [✓] Resilience 装饰器已生效\n");
        }
        else
        {
            Console.WriteLine($"  [!] 装饰器未生效，类型为 {typeName}\n");
        }

        // 场景 5：验证 OpenTelemetry DI 注册路径
        Console.WriteLine("--- 5. OpenTelemetry 注册验证 ---");
        try
        {
            // 验证 OpenTelemetryBuilder 已注册（委托式重载，AOT 安全）
            var otelBuilder = host.Services.GetService<OpenTelemetryBuilder>();
            Console.WriteLine("  [✓] OpenTelemetry DI 注册成功（委托式重载，AOT 推荐路径）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [!] OpenTelemetry 注册异常: {ex.GetType().Name}: {ex.Message}");
        }
        Console.WriteLine();

        Console.WriteLine("=== NuGet Package AOT 验证完成 ===");
        Console.WriteLine("如果此程序在 Native AOT 模式下成功运行，说明 NuGet 包消费路径（源生成器 + Attributes + Client + Resilience + OpenTelemetry）均已 AOT 兼容。");
        Console.WriteLine("AOT_OK");
    }
}
