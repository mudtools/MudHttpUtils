using HttpClientDemo.Apis;
using HttpClientDemo.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mud.HttpUtils;

namespace HttpClientDemo;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                Demo1_BasicRegistration(services);
                Demo2_EncryptionRegistration(services);
                Demo3_MultiNamedClients(services);
            })
            .Build();

        Console.WriteLine("=== Mud.HttpUtils.Client 功能演示 ===\n");

        await DemoBasicHttpClient(host.Services);
        await DemoEncryption(host.Services);
        await DemoMultiNamedClients(host.Services);
        await DemoHttpClientResolver(host.Services);
        await DemoMessageSanitizer();
        await DemoUrlValidator();

        Console.WriteLine("\n=== 演示完成 ===");
    }

    #region DI 注册演示

    private static void Demo1_BasicRegistration(IServiceCollection services)
    {
        services.AddMudHttpClient("userApi", client =>
        {
            client.BaseAddress = new Uri("https://api.example.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }

    private static void Demo2_EncryptionRegistration(IServiceCollection services)
    {
        services.AddMudHttpClient("encryptedApi", encryption =>
        {
            encryption.Key = Convert.FromBase64String("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXoxMjM0NTY=");
            encryption.IV = Convert.FromBase64String("YWJjZGVmZ2hpamtsbW5vcA==");
        }, client =>
        {
            client.BaseAddress = new Uri("https://secure-api.example.com");
        });
    }

    private static void Demo3_MultiNamedClients(IServiceCollection services)
    {
        services.AddMudHttpClient("orderApi", "https://order-api.example.com");
    }

    #endregion

    #region 基础 HTTP 客户端演示

    private static async Task DemoBasicHttpClient(IServiceProvider services)
    {
        Console.WriteLine("--- 1. 基础 HTTP 客户端（IEnhancedHttpClient）---");

        var httpClient = services.GetRequiredService<IEnhancedHttpClient>();

        try
        {
            var user = await httpClient.GetAsync<UserInfo>("/api/users/1");
            Console.WriteLine($"  GET /api/users/1 => {(user != null ? $"Id={user.Id}, Name={user.Name}" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  请求失败（预期行为，演示服务不可用）: {ex.Message}");
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/users")
            {
                Content = new StringContent("{\"name\":\"Test\",\"email\":\"test@example.com\"}", System.Text.Encoding.UTF8, "application/json")
            };
            var created = await httpClient.SendAsync<UserInfo>(request);
            Console.WriteLine($"  POST /api/users => {(created != null ? $"Id={created.Id}" : "null")}");
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"  请求失败（预期行为）: {ex.Message}");
        }

        Console.WriteLine();
    }

    #endregion

    #region 加密演示

    private static async Task DemoEncryption(IServiceProvider services)
    {
        Console.WriteLine("--- 2. AES 加密功能 ---");

        var httpClient = services.GetRequiredService<IEnhancedHttpClient>();

        if (httpClient is IEncryptableHttpClient encryptableClient)
        {
            var originalData = "Hello, Mud.HttpUtils!";
            var encrypted = encryptableClient.EncryptContent(new { message = originalData });
            Console.WriteLine($"  原始数据: {originalData}");
            Console.WriteLine($"  加密后: {encrypted}");

            var decrypted = encryptableClient.DecryptContent(encrypted);
            Console.WriteLine($"  解密后: {decrypted}");
        }

        Console.WriteLine();
    }

    #endregion

    #region 多命名客户端演示

    private static async Task DemoMultiNamedClients(IServiceProvider services)
    {
        Console.WriteLine("--- 3. 多命名客户端（IHttpClientResolver）---");

        var resolver = services.GetRequiredService<IHttpClientResolver>();

        if (resolver.TryGetClient("userApi", out var userClient))
        {
            Console.WriteLine($"  成功获取 userApi 客户端: {userClient.GetType().Name}");
        }

        if (resolver.TryGetClient("orderApi", out var orderClient))
        {
            Console.WriteLine($"  成功获取 orderApi 客户端: {orderClient.GetType().Name}");
        }

        if (!resolver.TryGetClient("nonExistentApi", out _))
        {
            Console.WriteLine("  nonExistentApi 客户端不存在（预期行为）");
        }

        Console.WriteLine();
    }

    #endregion

    #region HttpClientResolver 演示

    private static async Task DemoHttpClientResolver(IServiceProvider services)
    {
        Console.WriteLine("--- 4. HttpClientResolver 动态解析 ---");

        var resolver = services.GetRequiredService<IHttpClientResolver>();

        try
        {
            var client = resolver.GetClient("userApi");
            Console.WriteLine($"  GetClient(\"userApi\") => {client.GetType().Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  GetClient 异常: {ex.Message}");
        }

        try
        {
            resolver.GetClient("unknownApi");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"  GetClient(\"unknownApi\") 抛出 InvalidOperationException（预期行为）");
        }

        Console.WriteLine();
    }

    #endregion

    #region MessageSanitizer 演示

    private static async Task DemoMessageSanitizer()
    {
        Console.WriteLine("--- 5. MessageSanitizer 脱敏功能 ---");

        var jsonWithSensitiveData = """
            {
                "username": "zhangsan",
                "password": "MySecret123",
                "phone": "13800138000",
                "email": "zhangsan@example.com",
                "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0",
                "id_card": "110101199001011234",
                "normal_field": "this is normal data"
            }
            """;

        var sanitized = MessageSanitizer.Sanitize(jsonWithSensitiveData);
        Console.WriteLine("  原始数据:");
        Console.WriteLine($"    {jsonWithSensitiveData.Replace("\n", "").Trim()}");
        Console.WriteLine("  脱敏后:");
        Console.WriteLine($"    {sanitized}");

        await Task.CompletedTask;
        Console.WriteLine();
    }

    #endregion

    #region UrlValidator 演示

    private static async Task DemoUrlValidator()
    {
        Console.WriteLine("--- 6. UrlValidator 安全验证（概念说明）---");

        Console.WriteLine("  UrlValidator 是 internal 类，由 EnhancedHttpClient 内部自动调用。");
        Console.WriteLine("  功能包括：");
        Console.WriteLine("    - 域名白名单验证（ConfigureAllowedDomains 配置）");
        Console.WriteLine("    - 私有 IP 地址检测（防止 SSRF）");
        Console.WriteLine("    - 内网域名检测（.local, .internal 等）");
        Console.WriteLine("    - HTTPS 协议强制");
        Console.WriteLine("  使用方式：");
        Console.WriteLine("    UrlValidator.ConfigureAllowedDomains([\"api.example.com\"]);");
        Console.WriteLine("    UrlValidator.AddAllowedDomain(\"new-api.example.com\");");

        await Task.CompletedTask;
        Console.WriteLine();
    }

    #endregion
}
