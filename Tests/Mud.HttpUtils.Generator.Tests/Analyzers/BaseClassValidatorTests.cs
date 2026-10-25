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

    /// <summary>
    /// P2-2：短名归属判定改为「能解析到本编译内类型 ⇒ 手写基类（完整校验）」。
    /// </summary>
    /// <remarks>
    /// 未解析到的短名仍视为生成类（生成代码执行期不可见：自动检测抽象基 / 显式 InheritedFrom
    /// 指向生成实现），否则必然误报「基类不存在」。
    /// </remarks>
    [Theory]
    [InlineData("TestWrap", true)]
    [InlineData("MyClassWrap", true)]
    [InlineData("SomeWrapClass", true)]
    [InlineData("SimpleClass", true)]
    [InlineData("Namespace.Internal.MyClass", true)]
    [InlineData("System.Object", false)]
    [InlineData("MyNamespace.MyClass", false)]
    [InlineData("MyClass", false)]
    public void IsGeneratedClass_WithVariousClassNames_ShouldReturnExpectedResult(string className, bool expected)
    {
        // 编译内含全局 MyClass 与 MyNamespace.MyClass：前者让短名可解析（⇒ 手写宿主基类）
        var compilation = CreateCompilation(
            "public class MyClass { }",
            "namespace MyNamespace { public class MyClass { } }");

        var result = (bool)_isGeneratedClassMethod.Invoke(null, new object?[] { className, compilation, null })!;

        result.Should().Be(expected);
    }

    private static CSharpCompilation CreateCompilation(params string[] declarations)
        => CSharpCompilation.Create(
            "TestAssembly",
            declarations.Select(static d => CSharpSyntaxTree.ParseText(d)).ToArray(),
            BasicReferenceAssemblies.GetReferences());

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

    /// <summary>
    /// P2-2 正向：短名手写宿主基类（构造函数与真实 base(...) 契约一致）⇒ 校验通过。
    /// 修复前短名一律被当作生成类跳过，本用例无回归价值。
    /// </summary>
    [Fact]
    public void ValidateBaseClass_ShortNameHostBase_WithCompatibleCtor_Succeeds()
    {
        var compilation = CreateMudCompilation("""
            using Microsoft.Extensions.Logging;
            using Mud.HttpUtils;

            public abstract class HostBase
            {
                protected HostBase(IMudAppContext appContext, IAppContextHolder appContextHolder, IHttpRequestExecutor executor,
                    ILogger? logger = null, IHttpContentSerializer? contentSerializer = null)
                {
                }
            }
            """);

        IsValid(_validateBaseClassMethod.Invoke(null, new object?[] { compilation, "HostBase", false, null }))
            .Should().BeTrue("构造函数与 base(appContext, appContextHolder, executor, logger:, contentSerializer:) 契约一致");
    }

    /// <summary>
    /// P2-2 反向：短名手写宿主基类构造函数不兼容（缺 logger/contentSerializer 命名形参）⇒ 必须报错。
    /// 修复前会被「无点号即生成类」放行，缺陷延迟到生成期 CS1729。
    /// </summary>
    [Fact]
    public void ValidateBaseClass_ShortNameHostBase_WithIncompatibleCtor_ReportsError()
    {
        var compilation = CreateMudCompilation("""
            public abstract class HostBase
            {
                protected HostBase()
                {
                }
            }
            """);

        IsValid(_validateBaseClassMethod.Invoke(null, new object?[] { compilation, "HostBase", false, null }))
            .Should().BeFalse("无参构造无法接受 base(...) 的位置实参与命名实参，必须在编译期报出");
    }

    private static bool IsValid(object? result)
    {
        result.Should().NotBeNull();
        var isValidProperty = result!.GetType().GetProperty("IsValid");
        isValidProperty.Should().NotBeNull("ValidationResult 必须暴露 IsValid");
        return (bool)isValidProperty!.GetValue(result)!;
    }

    /// <summary>含 Mud.HttpUtils 引用的编译（宿主基类契约用例需要真实的 IMudAppContext 等类型）。</summary>
    private static CSharpCompilation CreateMudCompilation(string declaration)
    {
        var references = BasicReferenceAssemblies.GetReferences();
        references.Add(MetadataReference.CreateFromFile(typeof(IMudAppContext).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(IHttpRequestExecutor).Assembly.Location));

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(declaration) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    #endregion
}
