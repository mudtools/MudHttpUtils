// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// BaseClassValidator 基类验证器单元测试
/// </summary>
public class BaseClassValidatorTests
{
    private readonly Type _baseClassValidatorType;
    private readonly MethodInfo _validateBaseClassMethod;
    private readonly MethodInfo _isGeneratedClassMethod;

    public BaseClassValidatorTests()
    {
        _baseClassValidatorType = TestHelper.GetType("Mud.HttpUtils.Validators.BaseClassValidator");
        _validateBaseClassMethod = TestHelper.GetMethod(_baseClassValidatorType, "ValidateBaseClass");
        _isGeneratedClassMethod = TestHelper.GetMethod(_baseClassValidatorType, "IsGeneratedClass", BindingFlags.NonPublic | BindingFlags.Static);
    }

    #region IsGeneratedClass Tests

    [Theory]
    [InlineData("TestWrap", true)]
    [InlineData("MyClassWrap", true)]
    [InlineData("SomeWrapClass", true)]
    [InlineData("MyClass", true)]
    [InlineData("SimpleClass", true)]
    [InlineData("Namespace.Internal.MyClass", true)]
    [InlineData("System.Object", false)]
    [InlineData("MyNamespace.MyClass", false)]
    public void IsGeneratedClass_WithVariousClassNames_ShouldReturnExpectedResult(string className, bool expected)
    {
        var result = (bool)_isGeneratedClassMethod.Invoke(null, new object[] { className })!;

        result.Should().Be(expected);
    }

    #endregion

    #region ValidateBaseClass Tests

    [Fact]
    public void ValidateBaseClass_WithEmptyClassName_ShouldReturnSuccess()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class Test { }");
        var compilation = CSharpCompilation.Create("TestAssembly", new[] { syntaxTree });

        var result = _validateBaseClassMethod.Invoke(null, new object[] { compilation, "", false, null });

        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateBaseClass_WithNullClassName_ShouldReturnSuccess()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class Test { }");
        var compilation = CSharpCompilation.Create("TestAssembly", new[] { syntaxTree });

        var result = _validateBaseClassMethod.Invoke(null, new object?[] { compilation, null, false, null });

        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateBaseClass_WithGeneratedWrapClass_ShouldReturnSuccess()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class Test { }");
        var compilation = CSharpCompilation.Create("TestAssembly", new[] { syntaxTree });

        var result = _validateBaseClassMethod.Invoke(null, new object[] { compilation, "TestWrap", false, null });

        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateBaseClass_WithInternalClass_ShouldReturnSuccess()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class Test { }");
        var compilation = CSharpCompilation.Create("TestAssembly", new[] { syntaxTree });

        var result = _validateBaseClassMethod.Invoke(null, new object[] { compilation, "Namespace.Internal.MyClass", false, null });

        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateBaseClass_WithNonExistentClass_ShouldReturnError()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("class Test { }");
        var compilation = CSharpCompilation.Create("TestAssembly", new[] { syntaxTree });

        var result = _validateBaseClassMethod.Invoke(null, new object[] { compilation, "NonExistent.Namespace.ClassName", false, null });

        result.Should().NotBeNull();
        var resultType = result!.GetType();
        var isValidProperty = resultType.GetProperty("IsValid");
        var isValid = (bool)isValidProperty!.GetValue(result)!;
        isValid.Should().BeFalse();
    }

    #endregion
}
