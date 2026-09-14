// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace AotFullTrimVerificationDemo;

/// <summary>
/// TrimMode=full 验证 Demo。
/// 验证 Mud.HttpUtils 库在完整裁剪模式（TrimMode=full）下无遗漏的反射依赖。
/// </summary>
/// <remarks>
/// 与 AotVerificationDemo 的区别：
/// - AotVerificationDemo 使用默认 TrimMode=partial + TrimmerSingleWarn=true
/// - 本项目使用 TrimMode=full + TrimmerSingleWarn=false，暴露所有裁剪告警
///
/// 退出码契约：任一场景失败 → 输出 <c>AOT_FAIL</c> 并返回 1；全部通过 → 输出 <c>AOT_OK</c> 并返回 0。
/// 该契约使 <c>test.ps1 -AOT -FullTrim</c> 能可靠判定成功/失败（此前仅打印一行提示、无退出码）。
/// </remarks>
internal static class Program
{
    private static int s_failed;

    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Mud.HttpUtils TrimMode=full AOT 验证 ===");
        Console.WriteLine();

        await RunScenarioAsync("场景 1: JSON 序列化/反序列化", VerifyJsonSerializationAsync);
        await RunScenarioAsync("场景 2: AOT 安全脱敏器", () =>
        {
            VerifyAotSafeMasker();
            return Task.CompletedTask;
        });
        await RunScenarioAsync("场景 3: EncryptContent<T> 泛型重载", () =>
        {
            VerifyEncryptContent();
            return Task.CompletedTask;
        });
        await RunScenarioAsync("场景 4: 查询参数格式化", () =>
        {
            VerifyQueryParameters();
            return Task.CompletedTask;
        });
        await RunScenarioAsync("场景 5: 生成器路径（源生成 Context）", VerifyGeneratedContextAsync);

        Console.WriteLine();

        if (s_failed > 0)
        {
            Console.WriteLine($"AOT_FAIL (failed={s_failed})");
            return 1;
        }

        Console.WriteLine("=== 所有验证场景通过 ===");
        Console.WriteLine("AOT_OK");
        return 0;
    }

    private static async Task RunScenarioAsync(string name, Func<Task> scenario)
    {
        try
        {
            await scenario().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_failed++;
            Console.WriteLine($"  [FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Assert(bool condition, string scenario)
    {
        if (condition) return;
        s_failed++;
        Console.WriteLine($"  [FAIL] {scenario}");
    }

    private static async Task VerifyJsonSerializationAsync()
    {
        Console.WriteLine("[场景 1] JSON 序列化/反序列化（JsonTypeInfo）...");

        var dto = new TestDto { Id = 42, Name = "AOT Full Trim Test", Timestamp = DateTimeOffset.UtcNow };
        var json = JsonSerializer.Serialize(dto, FullTrimJsonContext.Default.TestDto);
        var deserialized = JsonSerializer.Deserialize(json, FullTrimJsonContext.Default.TestDto);

        Assert(deserialized?.Id == 42 && deserialized.Name == "AOT Full Trim Test",
            "JSON 序列化/反序列化结果不一致");

        Console.WriteLine("  ✓ JsonTypeInfo 序列化/反序列化成功");
        await Task.CompletedTask;
    }

    private static void VerifyAotSafeMasker()
    {
        Console.WriteLine("[场景 2] AOT 安全脱敏器...");

        var masker = new AotSafeSensitiveDataMasker();
        masker.Register<TestDto>(dto => $"***{dto.Id}***");

        var masked = masker.Mask("sensitive-token-value", SensitiveDataMaskMode.Mask, 2, 2);
        Assert(masked.Contains('*'), "字符串脱敏结果异常");

        var objMasked = masker.MaskObject(new TestDto { Id = 99, Name = "test" });
        Assert(objMasked.Contains("99"), "对象脱敏结果异常");

        Console.WriteLine("  ✓ AOT 安全脱敏器正常工作");
    }

    private static void VerifyEncryptContent()
    {
        Console.WriteLine("[场景 3] EncryptContent<T> 泛型重载...");

        // [T10 修复] 注入测试用 IEncryptionProvider，做真实加密 round-trip 断言。
        // 使用固定密钥的异或加密（Demo 专用，非生产级），验证 EncryptContent<T> 泛型重载在 TrimMode=full 下可正确执行。
        var encryptionProvider = new XorEncryptionProvider();
        var client = new EncryptTestClient(encryptionProvider);

        var dto = new TestDto { Id = 77, Name = "encrypt-test", Timestamp = DateTimeOffset.UtcNow };
        var encrypted = client.EncryptContent(dto);
        Assert(!string.IsNullOrEmpty(encrypted), "EncryptContent<T> 返回空字符串");

        // 解密并验证 round-trip
        var decrypted = client.DecryptContent(encrypted);
        Assert(decrypted.Contains("\"Id\":77") || decrypted.Contains("\"id\":77"),
            "EncryptContent<T> round-trip 解密后内容不一致");

        Console.WriteLine("  ✓ EncryptContent<T> 泛型重载加密 round-trip 成功");
    }

    private static void VerifyQueryParameters()
    {
        Console.WriteLine("[场景 4] 查询参数格式化...");

        // 验证基本的 URL 查询参数构建（不使用反射式 DefaultUrlParameterFormatter）
        var builder = new QueryParameterBuilder();
        builder.Add("page", "1");
        builder.Add("size", "20");
        builder.Add("filter", "active");

        var queryString = builder.ToString();
        Assert(queryString.Contains("page=1") && queryString.Contains("size=20"),
            "查询参数构建结果异常");

        Console.WriteLine("  ✓ 查询参数构建正常");
    }

    /// <summary>
    /// 场景 5：生成器路径。
    /// <c>[HttpClientApi]</c> 接口在编译期由源生成器生成实现类（并触发 AotStrictMode 下的生成代码检查）；
    /// <c>[HttpJsonSerializable]</c> DTO 由源生成 Context 覆盖，运行时经 <c>JsonTypeInfo</c> round-trip 验证。
    /// 覆盖了此前完全缺失的“生成实现类 + 源生成 Context”在 TrimMode=full 下的行为。
    /// </summary>
    private static async Task VerifyGeneratedContextAsync()
    {
        Console.WriteLine("[场景 5] 生成器路径（源生成 Context + [HttpClientApi] 实现类）...");

        // 生成器已为 IFullTrimApi 生成实现类（编译期产物）；此处通过源生成 Context 做 AOT 安全 round-trip。
        var dto = new FullTrimDto { Id = 7, Name = "generated" };
        var json = JsonSerializer.Serialize(dto, FullTrimJsonContext.Default.FullTrimDto);
        var roundTrip = JsonSerializer.Deserialize(json, FullTrimJsonContext.Default.FullTrimDto);

        Assert(roundTrip?.Id == 7 && roundTrip.Name == "generated",
            "源生成 Context round-trip 结果不一致");

        // 引用生成实现类，避免其在 full trim 下被判定为不可达而裁剪（同时验证生成产物可实例化引用）。
        var api = RestService.ForGenerated<IFullTrimApi>(new HttpClient { BaseAddress = new Uri("https://fake.example") });
        Assert(api is not null, "生成实现类无法通过 RestService.ForGenerated<T> 解析");

        Console.WriteLine("  ✓ 源生成 Context round-trip 与生成实现类解析均正常");
        await Task.CompletedTask;
    }
}

/// <summary>
/// 测试用 DTO。
/// </summary>
public class TestDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// 生成器路径验证用 DTO（由源生成 Context 覆盖）。
/// </summary>
[HttpJsonSerializable]
public class FullTrimDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 生成器路径验证用 API 接口（由 [HttpClientApi] 源生成实现类 + ModuleInitializer 工厂注册）。
/// </summary>
[HttpClientApi(HttpClient = "IEnhancedHttpClient")]
public interface IFullTrimApi
{
    [Get("/api/items/{id}")]
    Task<FullTrimDto?> GetAsync([Path] int id);
}

/// <summary>
/// JSON 序列化上下文（AOT 源生成）。
/// </summary>
[JsonSerializable(typeof(TestDto))]
[JsonSerializable(typeof(FullTrimDto))]
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class FullTrimJsonContext : JsonSerializerContext;

/// <summary>
/// [T10] Demo 专用异或加密提供器（非生产级，仅用于验证 EncryptContent&lt;T&gt; 代码路径在 TrimMode=full 下可正确执行）。
/// </summary>
internal sealed class XorEncryptionProvider : IEncryptionProvider
{
    private static readonly byte[] Key = "MudAotDemoKey!"u8.ToArray();

    public string Encrypt(string plainText)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(EncryptBytes(bytes));
    }

    public string Decrypt(string cipherText)
    {
        var bytes = Convert.FromBase64String(cipherText);
        return System.Text.Encoding.UTF8.GetString(DecryptBytes(bytes));
    }

    public byte[] EncryptBytes(byte[] data)
    {
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ Key[i % Key.Length]);
        return result;
    }

    public byte[] DecryptBytes(byte[] encryptedData)
    {
        // XOR 对称性：解密与加密相同
        return EncryptBytes(encryptedData);
    }
}

/// <summary>
/// [T10] 测试用 EnhancedHttpClient 子类，覆盖 EncryptionProvider 以注入 <see cref="XorEncryptionProvider"/>。
/// </summary>
internal sealed class EncryptTestClient : EnhancedHttpClient
{
    private readonly IEncryptionProvider _encryptionProvider;

    public EncryptTestClient(IEncryptionProvider encryptionProvider)
        : base(new HttpClient(), new EnhancedHttpClientOptions())
    {
        _encryptionProvider = encryptionProvider;
    }

    /// <inheritdoc/>
    protected override IEncryptionProvider? EncryptionProvider => _encryptionProvider;
}
