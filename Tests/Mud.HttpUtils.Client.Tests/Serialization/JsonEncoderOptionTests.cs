// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// B7：<see cref="EnhancedHttpClientOptions.JsonEncoder"/> / <c>HttpContentSerializerFactory</c>
/// 的按客户端编码器开关，以及"默认不变"的安全默认回归。
/// </summary>
public class JsonEncoderOptionTests
{
    private sealed class ProbePayload
    {
        public string Name { get; set; } = "中文内容";
    }

    /// <summary>默认（未设编码器）必须保持 STJ 默认转义 —— 安全默认不得变更。</summary>
    [Fact]
    public void Default_ShouldEscapeNonAscii()
    {
        var serializer = HttpContentSerializerFactory.CreateDefault(null, null, null);

        var json = serializer.Serialize(new ProbePayload());

        json.Should().Contain("\\u", "未显式设置编码器时保持 STJ 默认转义（回归）");
    }

    /// <summary>显式设置宽松编码器后，非 ASCII 不得被全转义。</summary>
    [Fact]
    public void RelaxedEncoder_ShouldNotEscapeNonAscii()
    {
        var serializer = HttpContentSerializerFactory.CreateDefault(
            null, null, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

        var json = serializer.Serialize(new ProbePayload());

        json.Should().Contain("中文内容");
        json.Should().NotContain("\\u");
    }

    /// <summary>显式编码器优先级高于消费方注入的 <c>JsonSerializerOptions.Encoder</c>（在合并基座之后写入）。</summary>
    [Fact]
    public void ExplicitEncoder_ShouldOverrideInjectedOptions()
    {
        var injected = new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default };

        var serializer = HttpContentSerializerFactory.CreateDefault(
            injected, null, JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

        var json = serializer.Serialize(new ProbePayload());

        json.Should().Contain("中文内容", "显式编码器不得被消费方注入的 Encoder 反向覆盖");
    }

    /// <summary>未传编码器时，消费方注入的 <c>Encoder</c> 仍生效（既有路径不回归）。</summary>
    [Fact]
    public void InjectedEncoder_WithoutExplicit_ShouldStillApply()
    {
        var injected = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        var serializer = HttpContentSerializerFactory.CreateDefault(injected, null, null);

        var json = serializer.Serialize(new ProbePayload());

        json.Should().Contain("中文内容");
    }

    /// <summary>B7 与 F1 联动：<c>Clone()</c> 必须带走 <c>JsonEncoder</c>（否则按客户端克隆会丢配置）。</summary>
    [Fact]
    public void Clone_ShouldCarryJsonEncoder()
    {
        var options = new EnhancedHttpClientOptions { JsonEncoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        options.Clone().JsonEncoder.Should().BeSameAs(JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    }
}
