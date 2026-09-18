// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

// 测试代码调用 Mud.HttpUtils.Xml 中标注 [RequiresUnreferencedCode]/[RequiresDynamicCode]
// 的成员（XmlSerializer 按设计即为非 AOT），此处豁免 IL2026/IL3050 属已知且预期。
#pragma warning disable IL2026, IL3050

using System.Reflection;
using System.Text;
using System.Xml.Serialization;
using Mud.HttpUtils;

namespace Mud.HttpUtils.Client.Tests;

public class XmlContentSerializerTests
{
    [XmlRoot("order")]
    public class Order
    {
        [XmlElement("id")]
        public int Id { get; set; }

        [XmlElement("name")]
        public string Name { get; set; } = string.Empty;

        // 无 XML 标注属性，用于验证 GetFieldNameForProperty 返回 null
        public string? Remark { get; set; }
    }

    public class AliasedItem
    {
        [XmlElement(ElementName = "sku")]
        public string ProductCode { get; set; } = string.Empty;
    }

    private static XmlContentSerializer CreateSerializer(Action<XmlContentSerializerSettings>? configure = null)
    {
        var settings = new XmlContentSerializerSettings();
        configure?.Invoke(settings);
        return new XmlContentSerializer(settings);
    }

    [Fact]
    public void SerializeDeserialize_RoundTrip_PreservesValues()
    {
        var serializer = new XmlContentSerializer();
        var source = new Order { Id = 42, Name = "测试订单", Remark = null };

        string xml = serializer.Serialize(source);
        var restored = serializer.Deserialize<Order>(xml);

        restored.Should().NotBeNull();
        restored!.Id.Should().Be(42);
        restored.Name.Should().Be("测试订单");
        restored.Remark.Should().BeNull();
    }

    [Fact]
    public void ToHttpContent_NullItem_ReturnsNull()
    {
        var serializer = new XmlContentSerializer();
        serializer.ToHttpContent<Order>(null!).Should().BeNull();
    }

    [Fact]
    public async Task ToHttpContent_UsesConfiguredMediaType_AndRoundTrips()
    {
        var serializer = CreateSerializer(s => s.MediaType = "text/xml");
        var source = new Order { Id = 7, Name = "content-test" };

        using var content = serializer.ToHttpContent(source);
        content.Should().NotBeNull();
        content!.Headers.ContentType!.MediaType.Should().Be("text/xml");

        string body = await content.ReadAsStringAsync();
        var restored = serializer.Deserialize<Order>(body);
        restored!.Id.Should().Be(7);
        restored.Name.Should().Be("content-test");
    }

    [Fact]
    public async Task FromHttpContentAsync_DeserializesStreamContent()
    {
        var serializer = new XmlContentSerializer();
        string xml = serializer.Serialize(new Order { Id = 9, Name = "stream-test" });
        using var content = new StringContent(xml, Encoding.UTF8, "application/xml");

        var restored = await serializer.FromHttpContentAsync<Order>(content);

        restored.Should().NotBeNull();
        restored!.Id.Should().Be(9);
        restored.Name.Should().Be("stream-test");
    }

    [Fact]
    public void Serialize_WithTypeOverload_MatchesGenericOverload()
    {
        var serializer = new XmlContentSerializer();
        var source = new Order { Id = 3, Name = "overload" };

        string generic = serializer.Serialize(source);
        string typed = serializer.Serialize((object?)source, typeof(Order));

        typed.Should().Be(generic);
    }

    [Fact]
    public void WriterSettings_Indent_True_IsApplied()
    {
        var serializer = CreateSerializer(s =>
        {
            s.WriterSettings.Indent = true;
            s.WriterSettings.OmitXmlDeclaration = true;
        });

        string xml = serializer.Serialize(new Order { Id = 1, Name = "indented" });

        xml.Should().Contain("\r\n").And.NotContain("<?xml");
    }

    [Fact]
    public void Settings_DefaultValues()
    {
        var settings = new XmlContentSerializerSettings();

        settings.MediaType.Should().Be("application/xml");
        settings.WriterSettings.Encoding.Should().Be(Encoding.UTF8);
        settings.WriterSettings.Indent.Should().BeFalse();
        settings.WriterSettings.OmitXmlDeclaration.Should().BeFalse();
    }

    [Fact]
    public void NullSettings_FallsBackToDefaults()
    {
        var serializer = new XmlContentSerializer(settings: null);

        serializer.Settings.Should().NotBeNull();
        serializer.Settings.MediaType.Should().Be("application/xml");
    }

    [Fact]
    public void GetFieldNameForProperty_ReturnsXmlElementName()
    {
        var serializer = new XmlContentSerializer();
        var property = typeof(AliasedItem).GetProperty(nameof(AliasedItem.ProductCode))!;

        serializer.GetFieldNameForProperty(property).Should().Be("sku");
    }

    [Fact]
    public void GetFieldNameForProperty_WithoutAttribute_ReturnsNull()
    {
        var serializer = new XmlContentSerializer();
        var property = typeof(Order).GetProperty(nameof(Order.Remark))!;

        serializer.GetFieldNameForProperty(property).Should().BeNull();
    }
}
