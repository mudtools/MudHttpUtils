// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// TypeSymbolHelper 类型符号辅助工具单元测试
/// </summary>
public class TypeSymbolHelperTests
{
    private readonly Type _typeSymbolHelperType;
    private readonly MethodInfo _isAsyncTypeMethod;
    private readonly MethodInfo _extractAsyncInnerTypeMethod;
    private readonly MethodInfo _getTypeFullNameMethod;

    public TypeSymbolHelperTests()
    {
        _typeSymbolHelperType = TestHelper.GetType("Mud.CodeGenerator.TypeSymbolHelper");
        _isAsyncTypeMethod = TestHelper.GetMethod(_typeSymbolHelperType, "IsAsyncType");
        _extractAsyncInnerTypeMethod = TestHelper.GetMethod(_typeSymbolHelperType, "ExtractAsyncInnerType");
        _getTypeFullNameMethod = TestHelper.GetMethod(_typeSymbolHelperType, "GetTypeFullName");
    }

    private Compilation CreateCompilation(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location)
        };

        return CSharpCompilation.Create("TestAssembly", new[] { syntaxTree }, references);
    }

    private ITypeSymbol GetTypeSymbol(Compilation compilation, string typeName)
    {
        var type = compilation.GetTypeByMetadataName(typeName);
        if (type != null)
            return type;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var typeDecls = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>();
            foreach (var typeDecl in typeDecls)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
                if (symbol?.Name == typeName)
                    return symbol;
            }
        }

        throw new InvalidOperationException($"Type '{typeName}' not found in compilation");
    }

    #region IsAsyncType Tests

    [Fact]
    public void IsAsyncType_WithTaskType_ShouldReturnTrue()
    {
        var code = @"
using System.Threading.Tasks;
public class TestClass
{
    public Task Method() => Task.CompletedTask;
}";
        var compilation = CreateCompilation(code);
        var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");

        var result = (bool)_isAsyncTypeMethod.Invoke(null, new object[] { taskType })!;

        result.Should().BeTrue();
    }

    [Fact]
    public void IsAsyncType_WithTaskOfString_ShouldReturnTrue()
    {
        var code = @"
using System.Threading.Tasks;
public class TestClass
{
    public Task<string> Method() => Task.FromResult(""test"");
}";
        var compilation = CreateCompilation(code);
        var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");

        var result = (bool)_isAsyncTypeMethod.Invoke(null, new object[] { taskType })!;

        result.Should().BeTrue();
    }

    [Fact]
    public void IsAsyncType_WithNonTaskType_ShouldReturnFalse()
    {
        var code = @"
public class TestClass
{
    public string Method() => ""test"";
}";
        var compilation = CreateCompilation(code);
        var stringType = compilation.GetTypeByMetadataName("System.String");

        var result = (bool)_isAsyncTypeMethod.Invoke(null, new object[] { stringType })!;

        result.Should().BeFalse();
    }

    [Fact]
    public void IsAsyncType_WithVoidType_ShouldReturnFalse()
    {
        var code = @"
public class TestClass
{
    public void Method() { }
}";
        var compilation = CreateCompilation(code);
        var voidType = compilation.GetSpecialType(SpecialType.System_Void);

        var result = (bool)_isAsyncTypeMethod.Invoke(null, new object[] { voidType })!;

        result.Should().BeFalse();
    }

    #endregion

    #region GetTypeFullName Tests

    [Fact]
    public void GetTypeFullName_WithSimpleType_ShouldReturnTypeName()
    {
        var code = @"public class TestClass { }";
        var compilation = CreateCompilation(code);
        var typeSymbol = GetTypeSymbol(compilation, "TestClass");

        var result = (string)_getTypeFullNameMethod.Invoke(null, new object[] { typeSymbol })!;

        result.Should().Be("TestClass");
    }

    [Fact]
    public void GetTypeFullName_WithGenericList_ShouldReturnGenericName()
    {
        var code = @"using System.Collections.Generic; public class TestClass { public List<string> Items { get; set; } }";
        var compilation = CreateCompilation(code);
        var listType = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");

        var result = (string)_getTypeFullNameMethod.Invoke(null, new object[] { listType })!;

        result.Should().Contain("List");
    }

    #endregion
}
