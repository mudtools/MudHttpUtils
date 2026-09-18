using FluentAssertions;
using Mud.HttpUtils.Attributes;
using System.Text.Json;

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

    [Fact]
    public void MaskObject_CircularReference_ReturnsCycleMarker()
    {
        var obj = new CircularObject { Name = "parent" };
        obj.Child = obj;

        var result = _masker.MaskObject(obj);

        result.Should().Contain("[循环引用]");
    }

    [Fact]
    public void MaskObject_DeepNesting_ReturnsDepthLimitMarker()
    {
        var root = new NestedObject { Name = "level0" };
        var current = root;
        for (int i = 1; i <= 10; i++)
        {
            current.Child = new NestedObject { Name = $"level{i}" };
            current = current.Child;
        }

        var result = _masker.MaskObject(root);

        result.Should().Contain("[深度超限]");
    }

    [Fact]
    public void MaskObject_MultipleCircularReferences_HandledCorrectly()
    {
        var a = new CircularObject { Name = "A" };
        var b = new CircularObject { Name = "B" };
        a.Child = b;
        b.Child = a;

        var result = _masker.MaskObject(a);

        result.Should().Contain("A");
        result.Should().Contain("B");
        result.Should().Contain("[循环引用]");
    }

    [Fact]
    public void MaskObject_GuidType_ReturnsToString()
    {
        var guid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var result = _masker.MaskObject(guid);

        result.Should().Contain("550e8400");
    }

    [Fact]
    public void MaskObject_DateTimeOffsetType_ReturnsToString()
    {
        var dto = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = _masker.MaskObject(dto);

        result.Should().Contain("2024");
    }

    [Fact]
    public void MaskObject_EnumType_ReturnsToString()
    {
        var result = _masker.MaskObject(SensitiveDataMaskMode.Mask);

        result.Should().Be("Mask");
    }

    [Fact]
    public void MaskObject_ConcurrentCalls_NoException()
    {
        var obj = new TestSensitiveObject
        {
            Name = "Concurrent",
            Secret = "SecretValue",
            Public = "PublicValue"
        };

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => _masker.MaskObject(obj)));

        var results = Task.WhenAll(tasks).GetAwaiter().GetResult();

        foreach (var result in results)
        {
            result.Should().NotBeNull();
            result.Should().Contain("Concurrent");
            result.Should().NotContain("SecretValue");
        }
    }

    [Fact]
    public void MaskObject_SimpleTypes_ReturnsDirectly()
    {
        _masker.MaskObject(TimeSpan.FromMinutes(5)).Should().Contain("5");
        _masker.MaskObject(123.45m).Should().Contain("123");
    }

    private class TestSensitiveObject
    {
        public string Name { get; set; } = "";
        [SensitiveData(MaskMode = SensitiveDataMaskMode.Hide)]
        public string Secret { get; set; } = "";
        public string Public { get; set; } = "";
    }

    private class CircularObject
    {
        public string Name { get; set; } = "";
        public CircularObject? Child { get; set; }
    }

    private class NestedObject
    {
        public string Name { get; set; } = "";
        public NestedObject? Child { get; set; }
    }
}
