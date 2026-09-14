// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  ObjectToInferredTypesConverter 往返一致性测试（P0-6 验收）
// -----------------------------------------------------------------------

using System.Text.Json;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// <see cref="ObjectToInferredTypesConverter"/> 的序列化/反序列化往返测试。
/// </summary>
/// <remarks>
/// <para>
/// 该转换器用于 <see cref="ProblemDetails.Extensions"/>（<c>Dictionary&lt;string, object?&gt;</c>）。
/// P0-6 修复把 <c>Write</c> 改为 <c>Utf8JsonWriter</c> 原生分支（零反射 / 零动态代码）。
/// </para>
/// <para>
/// 本测试锁死"<c>Read</c> 产出集合 ⊆ <c>Write</c> 可处理集合"这一对称契约：
/// 依次覆盖 string / number / bool / null / 数组（<see cref="JsonElement"/>），
/// 若 <c>Write</c> 再次退回反射重载，本测试在 Native AOT 下将直接失败。
/// </para>
/// </remarks>
public class ObjectToInferredTypesConverterTests
{
    [Fact]
    public void Extensions_RoundTrip_PreservesStringNumberBoolNullAndArray()
    {
        var details = new ProblemDetails
        {
            Type = "https://example.com/probs/out-of-credit",
            Title = "余额不足",
            Status = 400,
            Extensions = new Dictionary<string, object?>
            {
                ["str"] = "hello",
                ["num"] = 42m,
                ["flag"] = true,
                ["nothing"] = null,
                ["arr"] = JsonDocument.Parse("[1,2,3]").RootElement.Clone()
            }
        };

        var json = JsonSerializer.Serialize(details, ProblemDetailsJsonContext.Default.ProblemDetails);
        var back = JsonSerializer.Deserialize(json, ProblemDetailsJsonContext.Default.ProblemDetails);

        back.Should().NotBeNull();
        back!.Status.Should().Be(400);
        back.Extensions.Should().NotBeNull();

        var extensions = back.Extensions!;
        extensions["str"].Should().Be("hello");
        extensions["num"].Should().Be(42m);
        extensions["flag"].Should().Be(true);
        extensions["nothing"].Should().BeNull();
        extensions["arr"].Should().BeOfType<JsonElement>();

        // 数组元素类型按 Read 的类型推断规则回到 decimal
        var array = (JsonElement)extensions["arr"]!;
        array.ValueKind.Should().Be(JsonValueKind.Array);
        array.GetArrayLength().Should().Be(3);
        array[0].GetDecimal().Should().Be(1m);
    }

    [Fact]
    public void Extensions_SerializedJson_UsesInferredLiterals()
    {
        var details = new ProblemDetails
        {
            Extensions = new Dictionary<string, object?>
            {
                ["str"] = "hello",
                ["num"] = 42m,
                ["flag"] = true,
                ["nothing"] = null
            }
        };

        var json = JsonSerializer.Serialize(details, ProblemDetailsJsonContext.Default.ProblemDetails);

        // 原生 writer 分支：字符串带引号、数字/布尔为字面量、null 为 JSON null
        json.Should().Contain("\"str\":\"hello\"");
        json.Should().Contain("\"num\":42");
        json.Should().Contain("\"flag\":true");
        json.Should().Contain("\"nothing\":null");
    }
}
