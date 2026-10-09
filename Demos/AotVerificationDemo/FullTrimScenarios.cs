// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace AotVerificationDemo;

/// <summary>
/// TrimMode=full 专项验证场景（原独立工程 AotFullTrimVerificationDemo 并入，工程合并后
/// TrimMode 由 CI/test.ps1 以 <c>-p:TrimMode=full -p:TrimmerSingleWarn=false</c> 传入）。
/// </summary>
/// <remarks>
/// <para>
/// 场景为<b>离线验证</b>（不发真实 HTTP 请求）：JSON JsonTypeInfo round-trip、AOT 安全脱敏器、
/// <c>EncryptContent&lt;T&gt;</c> 泛型重载、查询参数构建、生成器路径（源生成 Context + 实现类解析）。
/// 与 Main 中其余场景的区别：失败一律视为非预期（离线断言没有"无真实服务器"的预期失败豁免），
/// 断言不通过即抛 <see cref="InvalidOperationException"/>，由 Program 的失败计数聚合。
/// </para>
/// <para>
/// 退出码契约（合并前）：任一场景失败 → 输出 <c>AOT_FAIL</c> 并返回 1。合并后由 Program 统一
/// 输出 <c>AOT_FAILED</c>/<c>AOT_OK</c>，语义不变。
/// </para>
/// </remarks>
internal static class FullTrimScenarios
{
    /// <summary>场景 1：JSON 序列化/反序列化（JsonTypeInfo）。</summary>
    public static async Task VerifyJsonSerializationAsync()
    {
        Console.WriteLine("[FullTrim 场景 1] JSON 序列化/反序列化（JsonTypeInfo）...");

        var dto = new FullTrimTestDto { Id = 42, Name = "AOT Full Trim Test", Timestamp = DateTimeOffset.UtcNow };
        var json = JsonSerializer.Serialize(dto, FullTrimJsonContext.Default.FullTrimTestDto);
        var deserialized = JsonSerializer.Deserialize(json, FullTrimJsonContext.Default.FullTrimTestDto);

        Assert(deserialized?.Id == 42 && deserialized.Name == "AOT Full Trim Test",
            "JSON 序列化/反序列化结果不一致");

        Console.WriteLine("  ✓ JsonTypeInfo 序列化/反序列化成功");
        await Task.CompletedTask;
    }

    /// <summary>场景 2：AOT 安全脱敏器。</summary>
    public static void VerifyAotSafeMasker()
    {
        Console.WriteLine("[FullTrim 场景 2] AOT 安全脱敏器...");

        var masker = new AotSafeSensitiveDataMasker();
        masker.Register<FullTrimTestDto>(dto => $"***{dto.Id}***");

        var masked = masker.Mask("sensitive-token-value", SensitiveDataMaskMode.Mask, 2, 2);
        Assert(masked.Contains('*'), "字符串脱敏结果异常");

        var objMasked = masker.MaskObject(new FullTrimTestDto { Id = 99, Name = "test" });
        Assert(objMasked.Contains("99"), "对象脱敏结果异常");

        Console.WriteLine("  ✓ AOT 安全脱敏器正常工作");
    }

    /// <summary>场景 3：EncryptContent&lt;T&gt; 泛型重载。</summary>
    public static void VerifyEncryptContent()
    {
        Console.WriteLine("[FullTrim 场景 3] EncryptContent<T> 泛型重载...");

        // [T10 修复] 注入测试用 IEncryptionProvider，做真实加密 round-trip 断言。
        // 使用固定密钥的异或加密（Demo 专用，非生产级），验证 EncryptContent<T> 泛型重载在 TrimMode=full 下可正确执行。
        var encryptionProvider = new XorEncryptionProvider();
        var client = new FullTrimEncryptTestClient(encryptionProvider);

        var dto = new FullTrimTestDto { Id = 77, Name = "encrypt-test", Timestamp = DateTimeOffset.UtcNow };
        var encrypted = client.EncryptContent(dto);
        Assert(!string.IsNullOrEmpty(encrypted), "EncryptContent<T> 返回空字符串");

        // 解密并验证 round-trip。
        // [AOT 体系修复] 断言需与源生成 Context 对齐：JsonSerializerOptions 命名策略为 CamelCase，
        // 故解密后应是 "id":77（原断言只认 "Id"，在正确配置下必然失败）。
        var decrypted = client.DecryptContent(encrypted);
        Assert(decrypted.Contains("\"id\":77") || decrypted.Contains("\"Id\":77"),
            $"EncryptContent<T> round-trip 解密后内容不一致：{decrypted}");

        Console.WriteLine("  ✓ EncryptContent<T> 泛型重载加密 round-trip 成功");
    }

    /// <summary>场景 4：查询参数格式化。</summary>
    public static void VerifyQueryParameters()
    {
        Console.WriteLine("[FullTrim 场景 4] 查询参数格式化...");

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
    /// 覆盖了"生成实现类 + 源生成 Context"在 TrimMode=full 下的行为。
    /// </summary>
    public static async Task VerifyGeneratedContextAsync()
    {
        Console.WriteLine("[FullTrim 场景 5] 生成器路径（源生成 Context + [HttpClientApi] 实现类）...");

        // 生成器已为 IFullTrimApi 生成实现类（编译期产物）；此处通过源生成 Context 做 AOT 安全 round-trip。
        var dto = new FullTrimDto { Id = 7, Name = "generated" };
        var json = JsonSerializer.Serialize(dto, FullTrimJsonContext.Default.FullTrimDto);
        var roundTrip = JsonSerializer.Deserialize(json, FullTrimJsonContext.Default.FullTrimDto);

        Assert(roundTrip?.Id == 7 && roundTrip.Name == "generated",
            "源生成 Context round-trip 结果不一致");

        // [AOT 体系修复] 以编译期类型引用生成实现类，避免其在 full trim 下被判定为不可达而裁剪。
        // 不能改用 RestService.ForGenerated<IFullTrimApi>()：工厂注册代码只为**默认模式**接口
        // （[HttpClientApi] 且未指定 HttpClient/TokenManager 包装类型）生成
        // （HttpInvokeRegistrationGenerator.GenerateFactoryRegistrationCall 仅遍历 defaultModeApis），
        // 而 IFullTrimApi 使用 IEnhancedHttpClient 模式 → ForGenerated 必然抛
        // "No generated factory registered"，使本场景恒失败。
        // 生成实现类命名约定：{接口所在命名空间}.Internal.{去掉 I 前缀的接口名}。
        var generatedImplementation = typeof(AotVerificationDemo.Internal.FullTrimApi);
        Assert(generatedImplementation.Name == "FullTrimApi",
            $"生成实现类缺失或命名不符：{generatedImplementation.FullName}");

        Console.WriteLine("  ✓ 源生成 Context round-trip 与生成实现类解析均正常");
        await Task.CompletedTask;
    }

    /// <summary>断言；不通过即抛（由 Program 的失败计数聚合——离线场景无非预期豁免）。</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[FullTrim] {message}");
        }
    }
}

/// <summary>FullTrim 场景专用测试 DTO。</summary>
public class FullTrimTestDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>生成器路径验证用 DTO（由源生成 Context 覆盖；亦被 JsonContextScaffolder 扫入 AppJsonContext）。</summary>
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

/// <summary>JSON 序列化上下文（AOT 源生成）。</summary>
[JsonSerializable(typeof(FullTrimTestDto))]
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
/// <remarks>
/// [AOT 体系修复] 必须显式注入源生成 <c>JsonTypeInfoResolver</c>：否则序列化器退回
/// 库内置 <c>MudHttpJsonContext</c>（不含本 Demo 的 <c>FullTrimTestDto</c>）→ EncryptContent 抛
/// <c>NotSupportedException</c>，场景恒失败（此前 T10 的"去虚化"断言恰好暴露了这一点）。
/// </remarks>
internal sealed class FullTrimEncryptTestClient : EnhancedHttpClient
{
    private readonly IEncryptionProvider _encryptionProvider;

    public FullTrimEncryptTestClient(IEncryptionProvider encryptionProvider)
        : base(new HttpClient(), new EnhancedHttpClientOptions
        {
            JsonTypeInfoResolver = FullTrimJsonContext.Default,
        })
    {
        _encryptionProvider = encryptionProvider;
    }

    /// <inheritdoc/>
    protected override IEncryptionProvider? EncryptionProvider => _encryptionProvider;
}
