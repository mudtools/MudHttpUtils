namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// TypeDetectionHelper 单元测试
/// 验证各类型检测方法的正确性
/// </summary>
public class TypeDetectionHelperTests
{
    #region IsSimpleType

    [Theory]
    [InlineData("string", true)]
    [InlineData("int", true)]
    [InlineData("long", true)]
    [InlineData("float", true)]
    [InlineData("double", true)]
    [InlineData("decimal", true)]
    [InlineData("bool", true)]
    [InlineData("DateTime", true)]
    [InlineData("Guid", true)]
    [InlineData("byte", true)]
    [InlineData("sbyte", true)]
    [InlineData("short", true)]
    [InlineData("ushort", true)]
    [InlineData("uint", true)]
    [InlineData("ulong", true)]
    [InlineData("char", true)]
    [InlineData("DateTimeOffset", true)]
    [InlineData("TimeSpan", true)]
    [InlineData("System.DateTime", true)]
    [InlineData("System.Guid", true)]
    [InlineData("System.DateTimeOffset", true)]
    [InlineData("System.TimeSpan", true)]
    public void IsSimpleType_PrimitiveTypes_ReturnsExpected(string typeName, bool expected)
    {
        TypeDetectionHelper.IsSimpleType(typeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("string?", true)]
    [InlineData("int?", true)]
    [InlineData("DateTime?", true)]
    [InlineData("Guid?", true)]
    public void IsSimpleType_NullableSimpleTypes_ReturnsTrue(string typeName, bool expected)
    {
        TypeDetectionHelper.IsSimpleType(typeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("int[]", true)]
    [InlineData("string[]", true)]
    [InlineData("double[]", true)]
    public void IsSimpleType_SimpleArrays_ReturnsTrue(string typeName, bool expected)
    {
        TypeDetectionHelper.IsSimpleType(typeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("List<string>", false)]
    [InlineData("Dictionary<string, int>", false)]
    [InlineData("UserData", false)]
    [InlineData("Stream", false)]
    [InlineData("CancellationToken", false)]
    [InlineData("object[]", false)]
    public void IsSimpleType_ComplexTypes_ReturnsFalse(string typeName, bool expected)
    {
        TypeDetectionHelper.IsSimpleType(typeName).Should().Be(expected);
    }

    #endregion

    #region IsByteArrayType

    [Theory]
    [InlineData("byte[]", true)]
    [InlineData("System.Byte[]", true)]
    [InlineData("byte[]?", true)]
    public void IsByteArrayType_ValidByteArrays_ReturnsTrue(string typeName, bool expected)
    {
        TypeDetectionHelper.IsByteArrayType(typeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("int[]", false)]
    [InlineData("string", false)]
    [InlineData("byte", false)]
    public void IsByteArrayType_NonByteArrays_ReturnsFalse(string typeName, bool expected)
    {
        TypeDetectionHelper.IsByteArrayType(typeName).Should().Be(expected);
    }

    #endregion

    #region IsStringType

    [Theory]
    [InlineData("string", true)]
    [InlineData("string?", true)]
    [InlineData("String", true)]
    [InlineData("STRING", true)]
    public void IsStringType_ValidStrings_ReturnsTrue(string typeName, bool expected)
    {
        TypeDetectionHelper.IsStringType(typeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("int", false)]
    [InlineData("System.String", false)]
    [InlineData("StringBuilder", false)]
    public void IsStringType_NonStrings_ReturnsFalse(string typeName, bool expected)
    {
        TypeDetectionHelper.IsStringType(typeName).Should().Be(expected);
    }

    #endregion

    #region IsArrayType

    [Theory]
    [InlineData("int[]", true)]
    [InlineData("string[]", true)]
    [InlineData("byte[]", true)]
    [InlineData("int[]?", true)]
    public void IsArrayType_ValidArrays_ReturnsTrue(string typeName, bool expected)
    {
        TypeDetectionHelper.IsArrayType(typeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("List<int>", false)]
    [InlineData("string", false)]
    [InlineData("IEnumerable<string>", false)]
    public void IsArrayType_NonArrays_ReturnsFalse(string typeName, bool expected)
    {
        TypeDetectionHelper.IsArrayType(typeName).Should().Be(expected);
    }

    #endregion

    #region IsNullableType

    [Theory]
    [InlineData("int?", true)]
    [InlineData("string?", true)]
    [InlineData("Guid?", true)]
    [InlineData("Nullable<int>", true)]
    public void IsNullableType_NullableTypes_ReturnsTrue(string typeName, bool expected)
    {
        TypeDetectionHelper.IsNullableType(typeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("int", false)]
    [InlineData("string", false)]
    [InlineData("List<string>", false)]
    public void IsNullableType_NonNullableTypes_ReturnsFalse(string typeName, bool expected)
    {
        TypeDetectionHelper.IsNullableType(typeName).Should().Be(expected);
    }

    [Fact]
    public void IsNullableType_ContainsQuestionMarkAngleBracket_ReturnsTrue()
    {
        // Contains("?<") 检测像 SomeType?< 这样的泛型可空类型
        TypeDetectionHelper.IsNullableType("SomeType?<").Should().BeTrue();
    }

    #endregion

    #region IsCancellationToken

    [Theory]
    [InlineData("CancellationToken", true)]
    [InlineData("System.Threading.CancellationToken", true)]
    [InlineData("CancellationToken?", true)]
    public void IsCancellationToken_ValidTypes_ReturnsTrue(string typeName, bool expected)
    {
        TypeDetectionHelper.IsCancellationToken(typeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("Token", false)]
    [InlineData("System.Threading.Tasks.Task", false)]
    [InlineData("int", false)]
    public void IsCancellationToken_InvalidTypes_ReturnsFalse(string typeName, bool expected)
    {
        TypeDetectionHelper.IsCancellationToken(typeName).Should().Be(expected);
    }

    #endregion

    #region IsAsyncEnumerableType

    [Fact]
    public void IsAsyncEnumerableType_ValidType_ReturnsTrueAndExtractsElement()
    {
        var result = TypeDetectionHelper.IsAsyncEnumerableType("IAsyncEnumerable<string>", out var elementType);

        result.Should().BeTrue();
        elementType.Should().Be("string");
    }

    [Fact]
    public void IsAsyncEnumerableType_ComplexElementType_ExtractsCorrectly()
    {
        var result = TypeDetectionHelper.IsAsyncEnumerableType("IAsyncEnumerable<ChatMessage>", out var elementType);

        result.Should().BeTrue();
        elementType.Should().Be("ChatMessage");
    }

    [Theory]
    [InlineData("IEnumerable<string>")]
    [InlineData("Task<string>")]
    [InlineData("List<string>")]
    [InlineData("string")]
    public void IsAsyncEnumerableType_InvalidTypes_ReturnsFalse(string typeName)
    {
        TypeDetectionHelper.IsAsyncEnumerableType(typeName, out var elementType).Should().BeFalse();
        elementType.Should().BeNull();
    }

    #endregion

    #region IsDictionaryType

    [Theory]
    [InlineData("IDictionary<string, string>", true)]
    [InlineData("Dictionary<string, int>", true)]
    [InlineData("IReadOnlyDictionary<string, object>", true)]
    [InlineData("System.Collections.Generic.IDictionary<string, string>", true)]
    [InlineData("System.Collections.Generic.Dictionary<string, string>", true)]
    [InlineData("IDictionary<string, string>?", true)]
    public void IsDictionaryType_ValidDictionaries_ReturnsTrue(string typeName, bool expected)
    {
        TypeDetectionHelper.IsDictionaryType(typeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("List<string>", false)]
    [InlineData("string", false)]
    [InlineData("IReadOnlyList<string>", false)]
    public void IsDictionaryType_NonDictionaries_ReturnsFalse(string typeName, bool expected)
    {
        TypeDetectionHelper.IsDictionaryType(typeName).Should().Be(expected);
    }

    #endregion
}
