// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Xml.Serialization;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// XmlSerialize XML序列化工具单元测试
/// </summary>
public class XmlSerializeTests
{
    #region Test Classes

    [XmlRoot("Person")]
    public class Person
    {
        [XmlElement("Name")]
        public string Name { get; set; } = string.Empty;

        [XmlElement("Age")]
        public int Age { get; set; }

        [XmlElement("Email")]
        public string Email { get; set; } = string.Empty;
    }

    [XmlRoot("Product")]
    public class Product
    {
        [XmlElement("Id")]
        public int Id { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; } = string.Empty;

        [XmlElement("Price")]
        public decimal Price { get; set; }

        [XmlArray("Tags")]
        [XmlArrayItem("Tag")]
        public List<string> Tags { get; set; } = new();
    }

    public class ClassWithoutParameterlessConstructor
    {
        public string Value { get; }

        public ClassWithoutParameterlessConstructor(string value)
        {
            Value = value;
        }
    }

    #endregion

    #region Serialize Tests

    [Fact]
    public void Serialize_WithValidObject_ShouldReturnXmlString()
    {
        var person = new Person
        {
            Name = "张三",
            Age = 30,
            Email = "zhangsan@example.com"
        };

        var result = XmlSerialize.Serialize(person);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("<Person");
        result.Should().Contain("<Name>张三</Name>");
        result.Should().Contain("<Age>30</Age>");
        result.Should().Contain("<Email>zhangsan@example.com</Email>");
    }

    [Fact]
    public void Serialize_WithNullObject_ShouldThrowArgumentNullException()
    {
        Person? person = null;

        var act = () => XmlSerialize.Serialize(person!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("obj");
    }

    [Fact]
    public void Serialize_WithCustomEncoding_ShouldUseSpecifiedEncoding()
    {
        var person = new Person { Name = "李四", Age = 25 };
        var encoding = Encoding.UTF8;

        var result = XmlSerialize.Serialize(person, encoding);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("utf-8");
    }

    [Fact]
    public void Serialize_WithNullEncoding_ShouldThrowArgumentNullException()
    {
        var person = new Person { Name = "王五", Age = 28 };

        var act = () => XmlSerialize.Serialize(person, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("encoding");
    }

    [Fact]
    public void Serialize_WithComplexObject_ShouldSerializeCorrectly()
    {
        var product = new Product
        {
            Id = 1,
            Name = "测试产品",
            Price = 99.99m,
            Tags = new List<string> { "电子", "数码" }
        };

        var result = XmlSerialize.Serialize(product);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("<Product");
        result.Should().Contain("<Id>1</Id>");
        result.Should().Contain("<Name>测试产品</Name>");
        result.Should().Contain("<Price>99.99</Price>");
        result.Should().Contain("<Tags>");
        result.Should().Contain("<Tag>电子</Tag>");
        result.Should().Contain("<Tag>数码</Tag>");
    }

    [Fact]
    public void Serialize_WithUnicodeCharacters_ShouldEncodeCorrectly()
    {
        var person = new Person
        {
            Name = "日本語テスト",
            Age = 20,
            Email = "test@example.com"
        };

        var result = XmlSerialize.Serialize(person);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("日本語テスト");
    }

    [Fact]
    public void Serialize_WithSpecialCharacters_ShouldEscapeCorrectly()
    {
        var person = new Person
        {
            Name = "Test & <Special> \"Characters\"",
            Age = 30,
            Email = "test@example.com"
        };

        var result = XmlSerialize.Serialize(person);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("&amp;");
        result.Should().Contain("&lt;");
        result.Should().Contain("&gt;");
    }

    #endregion

    #region Deserialize Tests

    [Fact]
    public void Deserialize_WithValidXml_ShouldReturnObject()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Person>
  <Name>张三</Name>
  <Age>30</Age>
  <Email>zhangsan@example.com</Email>
</Person>";

        var result = XmlSerialize.Deserialize<Person>(xml);

        result.Should().NotBeNull();
        result.Name.Should().Be("张三");
        result.Age.Should().Be(30);
        result.Email.Should().Be("zhangsan@example.com");
    }

    [Fact]
    public void Deserialize_WithNullXml_ShouldThrowArgumentNullException()
    {
        string? xml = null;

        var act = () => XmlSerialize.Deserialize<Person>(xml!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("xml");
    }

    [Fact]
    public void Deserialize_WithEmptyXml_ShouldThrowArgumentNullException()
    {
        var act = () => XmlSerialize.Deserialize<Person>(string.Empty);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("xml");
    }

    [Fact]
    public void Deserialize_WithInvalidXml_ShouldThrowInvalidOperationException()
    {
        var invalidXml = "This is not valid XML";

        var act = () => XmlSerialize.Deserialize<Person>(invalidXml);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*XML反序列化失败*");
    }

    [Fact]
    public void Deserialize_WithMismatchedXml_ShouldThrowInvalidOperationException()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Product>
  <Id>1</Id>
  <Name>Test</Name>
  <Price>99.99</Price>
</Product>";

        var act = () => XmlSerialize.Deserialize<Person>(xml);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Deserialize_WithCustomEncoding_ShouldUseSpecifiedEncoding()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Person>
  <Name>李四</Name>
  <Age>25</Age>
  <Email>lisi@example.com</Email>
</Person>";
        var encoding = Encoding.UTF8;

        var result = XmlSerialize.Deserialize<Person>(xml, encoding);

        result.Should().NotBeNull();
        result.Name.Should().Be("李四");
    }

    [Fact]
    public void Deserialize_WithNullEncoding_ShouldThrowArgumentNullException()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Person>
  <Name>王五</Name>
  <Age>28</Age>
</Person>";

        var act = () => XmlSerialize.Deserialize<Person>(xml, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("encoding");
    }

    [Fact]
    public void Deserialize_WithComplexXml_ShouldDeserializeCorrectly()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Product>
  <Id>1</Id>
  <Name>测试产品</Name>
  <Price>99.99</Price>
  <Tags>
    <Tag>电子</Tag>
    <Tag>数码</Tag>
  </Tags>
</Product>";

        var result = XmlSerialize.Deserialize<Product>(xml);

        result.Should().NotBeNull();
        result.Id.Should().Be(1);
        result.Name.Should().Be("测试产品");
        result.Price.Should().Be(99.99m);
        result.Tags.Should().HaveCount(2);
        result.Tags.Should().Contain("电子");
        result.Tags.Should().Contain("数码");
    }

    #endregion

    #region Round-trip Tests

    [Fact]
    public void SerializeDeserialize_RoundTrip_ShouldPreserveData()
    {
        var original = new Person
        {
            Name = "测试用户",
            Age = 35,
            Email = "test@example.com"
        };

        var xml = XmlSerialize.Serialize(original);
        var result = XmlSerialize.Deserialize<Person>(xml);

        result.Name.Should().Be(original.Name);
        result.Age.Should().Be(original.Age);
        result.Email.Should().Be(original.Email);
    }

    [Fact]
    public void SerializeDeserialize_WithComplexObject_ShouldPreserveData()
    {
        var original = new Product
        {
            Id = 100,
            Name = "复杂产品",
            Price = 199.99m,
            Tags = new List<string> { "标签1", "标签2", "标签3" }
        };

        var xml = XmlSerialize.Serialize(original);
        var result = XmlSerialize.Deserialize<Product>(xml);

        result.Id.Should().Be(original.Id);
        result.Name.Should().Be(original.Name);
        result.Price.Should().Be(original.Price);
        result.Tags.Should().BeEquivalentTo(original.Tags);
    }

    #endregion
}
