// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  AOT 快车道（IAotJsonContentSerializer）往返一致性测试
// -----------------------------------------------------------------------

#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;
using Mud.HttpUtils;

namespace Mud.HttpUtils.Tests;

/// <summary>快车道测试用 DTO。</summary>
/// <remarks>
/// [AOT 体系修复] 必须包含至少一个**多词**属性名：单属性名（Id/Name）在 CamelCase 与 SnakeCaseLower
/// 下的转换结果完全相同（都是 "id"/"name"），会让 T4 的"命名策略可区分"防假阳性断言
/// （<c>viaOptionsSlot.Should().NotBe(viaOptions)</c>）恒失败——原夹具即如此。
/// </remarks>
public sealed class AotFastPathDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>多词属性：CamelCase → displayName；SnakeCaseLower → display_name（用于区分两种命名策略）。</summary>
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>快车道测试用源生成上下文（CamelCase）。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AotFastPathDto))]
internal partial class AotFastPathJsonContext : JsonSerializerContext;

/// <summary>
/// SnakeCase 命名策略的独立 Context，用于与 CamelCase 的 _options 区分，
/// 防止"恰好相等"假阳性。
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AotFastPathDto))]
internal partial class SnakeCaseJsonContext : JsonSerializerContext;

/// <summary>
/// <see cref="IAotJsonContentSerializer"/>（AOT 快车道）行为测试。
/// </summary>
/// <remarks>
/// 该接口是"调用方以显式 <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo{T}"/>
/// 绕过 resolver 配置"的可选能力入口。此处验证：同一 DTO 经 options 路径与
/// <c>JsonTypeInfo&lt;T&gt;</c> 路径产出的 JSON 等价，且 HTTP 内容往返一致。
/// </remarks>
public class AotJsonContentSerializerTests
{
    [Fact]
    public void AotFastPath_Serialize_MatchesOptionsPath()
    {
        var dto = new AotFastPathDto { Id = 42, Name = "mud" };

        var serializer = new SystemTextJsonContentSerializer(
            HttpContentSerializerFactory.BuildOptions(null, AotFastPathJsonContext.Default));

        var viaOptions = serializer.Serialize(dto);
        var viaTypeInfo = ((IAotJsonContentSerializer)serializer).Serialize(dto, AotFastPathJsonContext.Default.AotFastPathDto);

        viaTypeInfo.Should().Be(viaOptions);
        viaOptions.Should().Contain("\"id\"");
    }

    [Fact]
    public async Task AotFastPath_HttpContent_RoundTrip()
    {
        var dto = new AotFastPathDto { Id = 7, Name = "round-trip" };

        var serializer = new SystemTextJsonContentSerializer(
            HttpContentSerializerFactory.BuildOptions(null, AotFastPathJsonContext.Default));

        var fast = (IAotJsonContentSerializer)serializer;

        using var content = fast.ToHttpContent(dto, AotFastPathJsonContext.Default.AotFastPathDto);
        content.Should().NotBeNull();
        content!.Headers.ContentType!.MediaType.Should().Be("application/json");

        var back = await fast.FromHttpContentAsync(content, AotFastPathJsonContext.Default.AotFastPathDto);
        back.Should().NotBeNull();
        back!.Id.Should().Be(7);
        back.Name.Should().Be("round-trip");

        // 字符串路径往返
        var json = fast.Serialize(dto, AotFastPathJsonContext.Default.AotFastPathDto);
        fast.Deserialize<AotFastPathDto>(json, AotFastPathJsonContext.Default.AotFastPathDto)!.Id.Should().Be(7);
    }

    // ───────────────────────── T4：options 槽位对称（JsonTypeInfo<T>） ─────────────────────────

    [Fact]
    public void T4_OptionsSlot_Serialize_WithJsonTypeInfo_MatchesFastPath()
    {
        var dto = new AotFastPathDto { Id = 99, Name = "options-slot" };

        // _options 使用 CamelCase，JsonTypeInfo 来自 SnakeCase Context（命名策略不同，可区分）
        var serializer = new SystemTextJsonContentSerializer(
            HttpContentSerializerFactory.BuildOptions(null, AotFastPathJsonContext.Default));

        // 经 options 槽位传入 JsonTypeInfo<T>（SnakeCase）
        var viaOptionsSlot = serializer.Serialize(dto, (object)SnakeCaseJsonContext.Default.AotFastPathDto);
        // 经快车道（IAotJsonContentSerializer）传入同一 JsonTypeInfo<T>
        var viaFastPath = ((IAotJsonContentSerializer)serializer).Serialize(dto, SnakeCaseJsonContext.Default.AotFastPathDto);

        viaOptionsSlot.Should().Be(viaFastPath, "options 槽位传入 JsonTypeInfo<T> 应与快车道输出一致");
        viaOptionsSlot.Should().Contain("\"id\"", "SnakeCase 对短单词仍输出小写");
        // 验证 SnakeCase Context 确实与 CamelCase _options 不同
        var viaOptions = serializer.Serialize(dto);
        viaOptionsSlot.Should().NotBe(viaOptions, "SnakeCase 与 CamelCase 输出必须不同，否则测试无区分力");
    }

    [Fact]
    public async Task T4_OptionsSlot_ToHttpContent_WithJsonTypeInfo_MatchesFastPath()
    {
        var dto = new AotFastPathDto { Id = 55, Name = "http-content" };

        var serializer = new SystemTextJsonContentSerializer(
            HttpContentSerializerFactory.BuildOptions(null, AotFastPathJsonContext.Default));

        // 经 options 槽位传入 JsonTypeInfo<T>
        using var contentViaOptions = serializer.ToHttpContent(dto, (object)SnakeCaseJsonContext.Default.AotFastPathDto);
        contentViaOptions.Should().NotBeNull();
        contentViaOptions!.Headers.ContentType!.MediaType.Should().Be("application/json");

        // 经快车道传入同一 JsonTypeInfo<T>
        using var contentViaFastPath = ((IAotJsonContentSerializer)serializer).ToHttpContent(dto, SnakeCaseJsonContext.Default.AotFastPathDto);
        contentViaFastPath.Should().NotBeNull();

        var jsonOptions = await contentViaOptions!.ReadAsStringAsync();
        var jsonFastPath = await contentViaFastPath!.ReadAsStringAsync();

        jsonOptions.Should().Be(jsonFastPath, "options 槽位 ToHttpContent 应与快车道逐字节一致");
        jsonOptions.Should().Contain("\"id\"");
    }

    [Fact]
    public void T4_OptionsSlot_Serialize_WithJsonTypeInfo_ProducesUtf8Bytes()
    {
        var dto = new AotFastPathDto { Id = 77, Name = "utf8" };

        var serializer = new SystemTextJsonContentSerializer(
            HttpContentSerializerFactory.BuildOptions(null, AotFastPathJsonContext.Default));

        // options 槽位传入 JsonTypeInfo<T> 应走 SerializeToUtf8Bytes → ByteArrayContent 路径
        using var content = serializer.ToHttpContent(dto, (object)SnakeCaseJsonContext.Default.AotFastPathDto);
        content.Should().NotBeNull();
        content!.Headers.ContentType!.MediaType.Should().Be("application/json");

        // ByteArrayContent（UTF-8 直出） vs StringContent（字符串编码） 的 Content 类型不同
        content.GetType().Name.Should().Be("ByteArrayContent",
            "JsonTypeInfo<T> 路径应走 SerializeToUtf8Bytes → ByteArrayContent");
    }

    [Fact]
    public void T4_OptionsSlot_Deserialize_WithJsonTypeInfo_AlreadySupported()
    {
        // 验证 Deserialize 路径已有 JsonTypeInfo<T> 支持（T4 前提：对称性已存在于 Deserialize）
        var dto = new AotFastPathDto { Id = 33, Name = "deserialize" };
        var serializer = new SystemTextJsonContentSerializer(
            HttpContentSerializerFactory.BuildOptions(null, AotFastPathJsonContext.Default));

        var json = serializer.Serialize(dto);
        var back = serializer.Deserialize<AotFastPathDto>(json, (object)AotFastPathJsonContext.Default.AotFastPathDto);

        back.Should().NotBeNull();
        back!.Id.Should().Be(33);
        back.Name.Should().Be("deserialize");
    }
}
#endif
