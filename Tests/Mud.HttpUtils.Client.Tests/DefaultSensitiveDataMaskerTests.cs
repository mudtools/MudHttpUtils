using FluentAssertions;
using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.Client.Tests;

public class DefaultSensitiveDataMaskerTests
{
    private readonly DefaultSensitiveDataMasker _masker = new();

    [Fact]
    public void Mask_HideMode_ReturnsMaskString()
    {
        var result = _masker.Mask("HelloWorld", SensitiveDataMaskMode.Hide);

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_MaskMode_ReturnsMaskedValue()
    {
        var result = _masker.Mask("HelloWorld", SensitiveDataMaskMode.Mask, 2, 3);

        result.Should().Be("He***rld");
    }

    [Fact]
    public void Mask_MaskMode_ShortValue_ReturnsMaskString()
    {
        var result = _masker.Mask("ab", SensitiveDataMaskMode.Mask, 2, 2);

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_TypeOnlyMode_ReturnsTypeAndLength()
    {
        var result = _masker.Mask("HelloWorld", SensitiveDataMaskMode.TypeOnly);

        result.Should().Be("[String, Length=10]");
    }

    [Fact]
    public void Mask_NullValue_ReturnsMaskString()
    {
        var result = _masker.Mask(null!, SensitiveDataMaskMode.Mask);

        result.Should().Be("***");
    }

    [Fact]
    public void Mask_EmptyValue_ReturnsMaskString()
    {
        var result = _masker.Mask("", SensitiveDataMaskMode.Mask);

        result.Should().Be("***");
    }

    [Fact]
    public void MaskObject_Null_ReturnsNullString()
    {
        var result = _masker.MaskObject(null!);

        result.Should().Be("null");
    }

    [Fact]
    public void MaskObject_PrimitiveType_ReturnsToString()
    {
        var result = _masker.MaskObject(42);

        result.Should().Be("42");
    }

    [Fact]
    public void MaskObject_StringType_ReturnsValue()
    {
        var result = _masker.MaskObject("hello");

        result.Should().Be("hello");
    }

    [Fact]
    public void MaskObject_WithSensitiveProperty_MasksProperty()
    {
        var obj = new TestSensitiveObject
        {
            Name = "Zhang",
            Secret = "MySecret123",
            Public = "visible"
        };

        var result = _masker.MaskObject(obj);

        result.Should().Contain("Zhang");
        result.Should().NotContain("MySecret123");
        result.Should().Contain("visible");
    }

    private class TestSensitiveObject
    {
        public string Name { get; set; } = "";
        [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
        public string Secret { get; set; } = "";
        public string Public { get; set; } = "";
    }
}
