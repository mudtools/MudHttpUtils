// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  AOT 快车道（IAotJsonContentSerializer）往返一致性测试
// -----------------------------------------------------------------------

#if NET8_0_OR_GREATER
using System.Text.Json.Serialization;

namespace Mud.HttpUtils.Tests;

/// <summary>快车道测试用 DTO。</summary>
public sealed class AotFastPathDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>快车道测试用源生成上下文。</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AotFastPathDto))]
internal partial class AotFastPathJsonContext : JsonSerializerContext;

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
}
#endif
