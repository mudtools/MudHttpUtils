// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mud.HttpUtils;
using Mud.HttpUtils.Resilience;
using Polly.CircuitBreaker;
using ResilienceDemo.Models;

namespace ResilienceDemo;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                Demo1_BasicResilience(services);
                Demo2_ResilienceWithDecorator(services);
                Demo3_ResilienceFromConfiguration(services);
                Demo4_CustomPolicyComposition(services);
            })
            .Build();

        Console.WriteLine("=== Mud.HttpUtils.Resilience 功能演示 ===\n");

        await DemoResilienceOptions(host.Services);
        await DemoRetryBehavior(host.Services);
        await DemoCircuitBreaker(host.Services);
        await DemoDecoratorPattern(host.Services);
        await DemoRequestCloning();

        Console.WriteLine("\n=== 演示完成 ===");
    }

    #region DI 注册演示

    private static void Demo1_BasicResilience(IServiceCollection services)
    {
        services.AddMudHttpClient("resilientApi", client =>
        {
            client.BaseAddress = new Uri("https://api.example.com");
        });

        services.AddMudHttpResilience(options =>
        {
            options.Retry.Enabled = true;
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.DelayMilliseconds = 500;
            options.Retry.UseExponentialBackoff = true;
            options.Timeout.Enabled = true;
            options.Timeout.TimeoutSeconds = 10;
            options.CircuitBreaker.Enabled = false;
        });
    }

    private static void Demo2_ResilienceWithDecorator(IServiceCollection services)
    {
        services.AddMudHttpClient("decoratedApi", client =>
        {
            client.BaseAddress = new Uri("https://protected-api.example.com");
        });

        services.AddMudHttpResilienceDecorator(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.DelayMilliseconds = 200;
            options.Timeout.TimeoutSeconds = 5;
            options.CircuitBreaker.Enabled = true;
            options.CircuitBreaker.FailureThreshold = 3;
            options.CircuitBreaker.BreakDurationSeconds = 10;
        });
    }

    private static void Demo3_ResilienceFromConfiguration(IServiceCollection services)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MudHttpResilience:Retry:Enabled"] = "true",
                ["MudHttpResilience:Retry:MaxRetryAttempts"] = "5",
                ["MudHttpResilience:Retry:DelayMilliseconds"] = "1000",
                ["MudHttpResilience:Timeout:Enabled"] = "true",
                ["MudHttpResilience:Timeout:TimeoutSeconds"] = "30",
                ["MudHttpResilience:CircuitBreaker:Enabled"] = "true",
                ["MudHttpResilience:CircuitBreaker:FailureThreshold"] = "5",
                ["MudHttpResilience:CircuitBreaker:BreakDurationSeconds"] = "30",
            })
            .Build();

        services.AddMudHttpClient("configApi", "https://config-api.example.com");
        services.AddMudHttpResilience(config);
    }

    private static void Demo4_CustomPolicyComposition(IServiceCollection services)
    {
        services.AddMudHttpClient("customApi", "https://custom-api.example.com");
        services.AddMudHttpResilience(options =>
        {
            options.Retry.Enabled = true;
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.DelayMilliseconds = 1000;
            options.Retry.RetryStatusCodes = [408, 429, 500, 502, 503, 504];
            options.Timeout.Enabled = true;
            options.Timeout.TimeoutSeconds = 15;
            options.CircuitBreaker.Enabled = true;
            options.CircuitBreaker.FailureThreshold = 5;
            options.CircuitBreaker.BreakDurationSeconds = 30;
            // 启用高级熔断策略：采样窗口 60s 内失败率达 50% 且至少 10 次请求时触发熔断
            // FailureThreshold 在此模式下表示失败率百分比（1-100）
            options.CircuitBreaker.SamplingDurationSeconds = 60;
            options.CircuitBreaker.FailureThreshold = 50; // 50% 失败率
            options.CircuitBreaker.MinimumThroughput = 10;
        });
    }

    #endregion

    #region ResilienceOptions 演示

    private static async Task DemoResilienceOptions(IServiceProvider services)
    {
        Console.WriteLine("--- 1. ResilienceOptions 配置演示 ---");

        var policyProvider = services.GetRequiredService<IResiliencePolicyProvider>();

        var retryPolicy = policyProvider.GetRetryPolicy<WeatherForecast?>();
        var timeoutPolicy = policyProvider.GetTimeoutPolicy<WeatherForecast?>();
        var circuitBreakerPolicy = policyProvider.GetCircuitBreakerPolicy<WeatherForecast?>();
        var combinedPolicy = policyProvider.GetCombinedPolicy<WeatherForecast?>();

        Console.WriteLine($"  重试策略: {retryPolicy.GetType().Name}");
        Console.WriteLine($"  超时策略: {timeoutPolicy.GetType().Name}");
        Console.WriteLine($"  熔断策略: {circuitBreakerPolicy.GetType().Name}");
        Console.WriteLine($"  组合策略: {combinedPolicy.GetType().Name}");
        Console.WriteLine("  策略组合顺序: 重试(外层) → 熔断 → 超时(内层)");

        await Task.CompletedTask;
        Console.WriteLine();
    }

    #endregion

    #region 重试行为演示

    private static async Task DemoRetryBehavior(IServiceProvider services)
    {
        Console.WriteLine("--- 2. 重试策略行为演示 ---");

        var policyProvider = services.GetRequiredService<IResiliencePolicyProvider>();
        var retryPolicy = policyProvider.GetRetryPolicy<string?>();

        int attemptCount = 0;

        try
        {
            var result = await retryPolicy.ExecuteAsync(async () =>
            {
                attemptCount++;
                Console.WriteLine($"  第 {attemptCount} 次尝试...");

                if (attemptCount < 3)
                {
                    throw new HttpRequestException("模拟服务暂时不可用 (503)");
                }

                return await Task.FromResult("请求成功！");
            });

            Console.WriteLine($"  最终结果: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  重试耗尽后失败: {ex.Message}");
        }

        Console.WriteLine();
    }

    #endregion

    #region 熔断器演示

    private static async Task DemoCircuitBreaker(IServiceProvider services)
    {
        Console.WriteLine("--- 3. 熔断器行为演示 ---");

        var customOptions = new ResilienceOptions
        {
            Retry = new RetryOptions { Enabled = false },
            Timeout = new TimeoutOptions { Enabled = false },
            CircuitBreaker = new CircuitBreakerOptions
            {
                Enabled = true,
                FailureThreshold = 3,
                BreakDurationSeconds = 5,
            }
        };

        var provider = new PollyResiliencePolicyProvider(customOptions);
        var cbPolicy = provider.GetCircuitBreakerPolicy<string?>();

        for (int i = 1; i <= 5; i++)
        {
            try
            {
                await cbPolicy.ExecuteAsync(async () =>
                {
                    throw new HttpRequestException($"模拟失败 #{i}");
                });
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"  第 {i} 次请求: HttpRequestException - {ex.Message}");
            }
            catch (IsolatedCircuitException ex)
            {
                Console.WriteLine($"  第 {i} 次请求: 熔断器已开启 - {ex.Message}");
            }
        }

        Console.WriteLine("  等待熔断恢复...");
        await Task.Delay(6000);

        try
        {
            await cbPolicy.ExecuteAsync(async () =>
            {
                Console.WriteLine("  熔断器半开状态：试探请求成功");
                return await Task.FromResult("恢复成功");
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  试探请求失败: {ex.Message}");
        }

        Console.WriteLine();
    }

    #endregion

    #region 装饰器模式演示

    private static async Task DemoDecoratorPattern(IServiceProvider services)
    {
        Console.WriteLine("--- 4. 装饰器模式（ResilientHttpClient）---");

        var httpClient = services.GetRequiredService<IEnhancedHttpClient>();
        Console.WriteLine($"  IEnhancedHttpClient 实际类型: {httpClient.GetType().Name}");

        if (httpClient is ResilientHttpClient resilientClient)
        {
            Console.WriteLine("  已通过装饰器包装，所有请求自动经过弹性策略");
            Console.WriteLine("  调用链: 调用方 → ResilientHttpClient → Polly 策略 → 请求克隆 → HttpClientFactoryEnhancedClient");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/weather/forecast");
            var result = await httpClient.SendAsync<List<WeatherForecast>>(request);
            Console.WriteLine($"  请求结果: {(result != null ? $"获取到 {result.Count} 条数据" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  请求失败（预期行为，演示服务不可用）: {ex.Message}");
        }

        Console.WriteLine();
    }

    #endregion

    #region 请求克隆演示

    private static async Task DemoRequestCloning()
    {
        Console.WriteLine("--- 5. HttpRequestMessageCloner 请求克隆（概念说明）---");

        Console.WriteLine("  HttpRequestMessageCloner 是 internal 类，由 ResilientHttpClient 内部自动调用。");
        Console.WriteLine("  克隆内容包括：");
        Console.WriteLine("    - HTTP 方法和请求 URI");
        Console.WriteLine("    - 请求头（Headers）");
        Console.WriteLine("    - 请求体内容（Content）及 Content Headers");
        Console.WriteLine("    - HTTP 版本（Version）");
        Console.WriteLine("    - 请求选项（Options，仅 .NET 5+）");
        Console.WriteLine("  用途：Polly 重试时 HttpRequestMessage 不能重复发送，克隆确保每次重试使用新的请求实例");
        Console.WriteLine("  调用链：调用方 → ResilientHttpClient → Polly 策略 → 请求克隆 → 内部 IEnhancedHttpClient");

        await Task.CompletedTask;
        Console.WriteLine();
    }

    #endregion
}
