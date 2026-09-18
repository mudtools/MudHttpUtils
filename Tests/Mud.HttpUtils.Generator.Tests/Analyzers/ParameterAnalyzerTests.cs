using System.Reflection;
using Mud.HttpUtils.Models;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils.Generator.Tests;

using AnalysisParameterInfo = Mud.HttpUtils.Models.Analysis.ParameterInfo;

public class ParameterAnalyzerTests
{
    private static readonly Type ParameterAnalyzerType = TestHelper.GetType("Mud.HttpUtils.Analyzers.ParameterAnalyzer");
    private static readonly MethodInfo AnalyzeParametersMethod = TestHelper.GetMethod(ParameterAnalyzerType, "AnalyzeParameters");
    private static readonly MethodInfo AnalyzeParameterMethod = TestHelper.GetMethod(ParameterAnalyzerType, "AnalyzeParameter");

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BasicReferenceAssemblies.GetReferences();

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private IMethodSymbol GetMethodSymbol(Compilation compilation, string typeName, string methodName)
    {
        var typeSymbol = compilation.GetTypeByMetadataName(typeName);
        if (typeSymbol != null)
        {
            return typeSymbol.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault()!;
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var methodDecls = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var methodDecl in methodDecls)
            {
                var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                if (symbol?.Name == methodName)
                    return symbol;
            }
        }

        throw new InvalidOperationException($"Method '{methodName}' not found in compilation");
    }

    #region AnalyzeParameters Tests

    [Fact]
    public void AnalyzeParameters_WithNoParameters_ReturnsEmptyList()
    {
        var source = @"
public interface ITestApi
{
    void NoParamMethod();
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "NoParamMethod");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeParameters_WithSimpleParameters_ReturnsParameterInfos()
    {
        var source = @"
public interface ITestApi
{
    void Method(string name, int age);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("name");
        result[0].Type.Should().Be("string");
        result[1].Name.Should().Be("age");
        result[1].Type.Should().Be("int");
    }

    [Fact]
    public void AnalyzeParameters_WithParameterWithAttribute_CapturesAttribute()
    {
        var source = @"
public class QueryAttribute : System.Attribute { }
public interface ITestApi
{
    void Method([Query] string keyword);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(1);
        result[0].Attributes.Should().ContainSingle(a => a.Name == "QueryAttribute");
    }

    [Fact]
    public void AnalyzeParameters_WithParameterWithDefaultValue_SetsHasDefaultValue()
    {
        var source = @"
public interface ITestApi
{
    void Method(string name = ""default"", int page = 1);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(2);
        result[0].HasDefaultValue.Should().BeTrue();
        result[0].DefaultValue.Should().Be("default");
        result[1].HasDefaultValue.Should().BeTrue();
        result[1].DefaultValue.Should().Be(1);
    }

    [Fact]
    public void AnalyzeParameters_WithCancellationTokenParameter_CapturesType()
    {
        var source = @"
using System.Threading;
public interface ITestApi
{
    void Method(CancellationToken cancellationToken);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("cancellationToken");
        result[0].Type.Should().Contain("CancellationToken");
    }

    [Fact]
    public void AnalyzeParameters_WithMultipleAttributes_CapturesAllAttributes()
    {
        var source = @"
public class HeaderAttribute : System.Attribute { public string Name { get; set; } }
public class QueryAttribute : System.Attribute { }
public interface ITestApi
{
    void Method([Header(Name = ""X-Custom"")] string value, [Query] string keyword);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(2);
        result[0].Attributes.Should().ContainSingle(a => a.Name == "HeaderAttribute");
        result[0].Attributes[0].NamedArguments.Should().ContainKey("Name");
        result[1].Attributes.Should().ContainSingle(a => a.Name == "QueryAttribute");
    }

    [Fact]
    public void AnalyzeParameters_WithNullableType_CapturesNullableAnnotation()
    {
        var source = @"
#nullable enable
public interface ITestApi
{
    void Method(string? name, string requiredName);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(2);
        result[0].Type.Should().Contain("string");
        result[1].Type.Should().Contain("string");
    }

    #endregion

    #region AnalyzeParameter Tests

    [Fact]
    public void AnalyzeParameter_WithBodyAttribute_CapturesAttribute()
    {
        var source = @"
public class BodyAttribute : System.Attribute { }
public interface ITestApi
{
    void Method([Body] object data);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(1);
        result[0].Attributes.Should().ContainSingle(a => a.Name == "BodyAttribute");
    }

    [Fact]
    public void AnalyzeParameter_WithPathAttribute_CapturesAttribute()
    {
        var source = @"
public class PathAttribute : System.Attribute { }
public interface ITestApi
{
    void Method([Path] string userId);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(1);
        result[0].Attributes.Should().ContainSingle(a => a.Name == "PathAttribute");
    }

    [Fact]
    public void AnalyzeParameter_WithFormAttribute_CapturesAttribute()
    {
        var source = @"
public class FormAttribute : System.Attribute { }
public interface ITestApi
{
    void Method([Form] string field);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(1);
        result[0].Attributes.Should().ContainSingle(a => a.Name == "FormAttribute");
    }

    [Fact]
    public void AnalyzeParameter_WithUploadAttribute_CapturesAttribute()
    {
        var source = @"
public class UploadAttribute : System.Attribute { }
public interface ITestApi
{
    void Method([Upload] string filePath);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(1);
        result[0].Attributes.Should().ContainSingle(a => a.Name == "UploadAttribute");
    }

    [Fact]
    public void AnalyzeParameter_WithTokenAttribute_ContainsTokenAttributeWithTokenType()
    {
        var source = @"
using Mud.HttpUtils.Attributes;

public interface ITestApi
{
    void Method([Token(TokenType = ""Bearer"")] string token);
}";
        var compilation = CreateCompilation(source);
        var methodSymbol = GetMethodSymbol(compilation, "ITestApi", "Method");

        var result = (List<AnalysisParameterInfo>)AnalyzeParametersMethod.Invoke(null, new object[] { methodSymbol })!;

        result.Should().HaveCount(1);
        result[0].Attributes.Should().ContainSingle(a => a.Name == "TokenAttribute");
        var tokenAttr = result[0].Attributes.First(a => a.Name == "TokenAttribute");
        tokenAttr.NamedArguments.Should().ContainKey("TokenType");
        tokenAttr.NamedArguments["TokenType"]?.ToString().Should().Be("Bearer");
    }

    #endregion
}
