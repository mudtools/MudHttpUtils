// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// CSharpCodeValidator C#代码验证工具单元测试
/// </summary>
public class CSharpCodeValidatorTests
{
    private readonly Type _csharpCodeValidatorType;
    private readonly MethodInfo _isValidCSharpIdentifierMethod;
    private readonly MethodInfo _isValidUrlTemplateMethod;

    public CSharpCodeValidatorTests()
    {
        _csharpCodeValidatorType = TestHelper.GetType("Mud.CodeGenerator.CSharpCodeValidator");
        _isValidCSharpIdentifierMethod = TestHelper.GetMethod(_csharpCodeValidatorType, "IsValidCSharpIdentifier");
        _isValidUrlTemplateMethod = TestHelper.GetMethod(_csharpCodeValidatorType, "IsValidUrlTemplate");
    }

    [Theory]
    [InlineData("validIdentifier", true)]
    [InlineData("_validIdentifier", true)]
    [InlineData("ValidIdentifier123", true)]
    [InlineData("MyClass", true)]
    [InlineData("my_variable", true)]
    [InlineData("123invalid", false)]
    [InlineData("invalid-identifier", false)]
    [InlineData("invalid.identifier", false)]
    [InlineData("", false)]
    [InlineData("class", false)]
    [InlineData("int", false)]
    [InlineData("string", false)]
    [InlineData("void", false)]
    [InlineData("if", false)]
    [InlineData("else", false)]
    [InlineData("for", false)]
    [InlineData("while", false)]
    public void IsValidCSharpIdentifier_WithVariousInputs_ShouldReturnExpectedResult(string identifier, bool expected)
    {
        var result = _isValidCSharpIdentifierMethod.Invoke(null, new object?[] { identifier });

        result.Should().Be(expected);
    }

    [Fact]
    public void IsValidCSharpIdentifier_WithNullIdentifier_ShouldReturnFalse()
    {
        var result = _isValidCSharpIdentifierMethod.Invoke(null, new object?[] { null });

        result.Should().Be(false);
    }

    [Fact]
    public void IsValidUrlTemplate_WithValidTemplate_ShouldReturnTrue()
    {
        var parameters = new object[] { "https://api.example.com/users/{id}", null };
        var result = _isValidUrlTemplateMethod.Invoke(null, parameters);

        result.Should().Be(true);
        parameters[1].Should().BeNull();
    }

    [Fact]
    public void IsValidUrlTemplate_WithMultipleParameters_ShouldReturnTrue()
    {
        var parameters = new object[] { "https://api.example.com/users/{userId}/posts/{postId}", null };
        var result = _isValidUrlTemplateMethod.Invoke(null, parameters);

        result.Should().Be(true);
        parameters[1].Should().BeNull();
    }

    [Fact]
    public void IsValidUrlTemplate_WithEmptyTemplate_ShouldReturnTrue()
    {
        var parameters = new object[] { "", null };
        var result = _isValidUrlTemplateMethod.Invoke(null, parameters);

        result.Should().Be(true);
    }

    [Fact]
    public void IsValidUrlTemplate_WithNullTemplate_ShouldReturnTrue()
    {
        var parameters = new object?[] { null, null };
        var result = _isValidUrlTemplateMethod.Invoke(null, parameters);

        result.Should().Be(true);
    }

    [Fact]
    public void IsValidUrlTemplate_WithUnclosedBrace_ShouldReturnFalse()
    {
        var parameters = new object[] { "https://api.example.com/users/{id", null };
        var result = _isValidUrlTemplateMethod.Invoke(null, parameters);

        result.Should().Be(false);
        parameters[1].Should().NotBeNull();
    }

    [Fact]
    public void IsValidUrlTemplate_WithEmptyBraces_ShouldReturnFalse()
    {
        var parameters = new object[] { "https://api.example.com/users/{}", null };
        var result = _isValidUrlTemplateMethod.Invoke(null, parameters);

        result.Should().Be(false);
        parameters[1].Should().NotBeNull();
    }

    [Fact]
    public void IsValidUrlTemplate_WithInvalidParameterName_ShouldReturnFalse()
    {
        var parameters = new object[] { "https://api.example.com/users/{123invalid}", null };
        var result = _isValidUrlTemplateMethod.Invoke(null, parameters);

        result.Should().Be(false);
        parameters[1].Should().NotBeNull();
    }

    [Fact]
    public void IsValidUrlTemplate_WithExtraClosingBrace_ShouldReturnFalse()
    {
        var parameters = new object[] { "https://api.example.com/users/}", null };
        var result = _isValidUrlTemplateMethod.Invoke(null, parameters);

        result.Should().Be(false);
        parameters[1].Should().NotBeNull();
    }
}
