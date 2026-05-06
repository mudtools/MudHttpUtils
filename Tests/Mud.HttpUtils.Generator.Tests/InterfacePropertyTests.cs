using Mud.HttpUtils.Analyzers;
using Mud.HttpUtils.Models.Analysis;

namespace Mud.HttpUtils.Generator.Tests;

public class InterfacePropertyTests
{
    private Compilation CreateCompilation(string source)
    {
        var references = BasicReferenceAssemblies.GetReferences();
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void AnalyzeInterfaceProperties_WithQueryProperty_ReturnsProperty()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Query]
        string Version { get; set; }

        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var interfaceDecl = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().First();
        var model = compilation.GetSemanticModel(tree);

        var result = MethodAnalyzer.AnalyzeInterfaceProperties(interfaceDecl, compilation, model);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Version");
        result[0].AttributeType.Should().Be("Query");
        result[0].ParameterName.Should().Be("Version");
    }

    [Fact]
    public void AnalyzeInterfaceProperties_WithPathProperty_ReturnsProperty()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Path]
        string TenantId { get; set; }

        [Get(""/api/{TenantId}/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var interfaceDecl = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().First();
        var model = compilation.GetSemanticModel(tree);

        var result = MethodAnalyzer.AnalyzeInterfaceProperties(interfaceDecl, compilation, model);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("TenantId");
        result[0].AttributeType.Should().Be("Path");
    }

    [Fact]
    public void AnalyzeInterfaceProperties_WithMultipleProperties_ReturnsAll()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Query]
        string Version { get; set; }

        [Path]
        string TenantId { get; set; }

        [Query(""api_key"")]
        string ApiKey { get; set; }

        [Get(""/api/{TenantId}/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var interfaceDecl = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().First();
        var model = compilation.GetSemanticModel(tree);

        var result = MethodAnalyzer.AnalyzeInterfaceProperties(interfaceDecl, compilation, model);

        result.Should().HaveCount(3);
        result.Count(p => p.AttributeType == "Query").Should().Be(2);
        result.Count(p => p.AttributeType == "Path").Should().Be(1);
    }

    [Fact]
    public void AnalyzeInterfaceProperties_WithNamedParameter_SetsParameterName()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Query(""api_version"")]
        string Version { get; set; }

        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var interfaceDecl = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().First();
        var model = compilation.GetSemanticModel(tree);

        var result = MethodAnalyzer.AnalyzeInterfaceProperties(interfaceDecl, compilation, model);

        result.Should().HaveCount(1);
        result[0].ParameterName.Should().Be("api_version");
        result[0].Name.Should().Be("Version");
    }

    [Fact]
    public void AnalyzeInterfaceProperties_NoProperties_ReturnsEmptyList()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync();
    }
}";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var interfaceDecl = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().First();
        var model = compilation.GetSemanticModel(tree);

        var result = MethodAnalyzer.AnalyzeInterfaceProperties(interfaceDecl, compilation, model);

        result.Should().BeEmpty();
    }

    [Fact]
    public void InterfacePropertyInfo_DefaultValues_AreCorrect()
    {
        var info = new InterfacePropertyInfo();

        info.Name.Should().BeEmpty();
        info.Type.Should().BeEmpty();
        info.AttributeType.Should().BeEmpty();
        info.ParameterName.Should().BeNull();
        info.Format.Should().BeNull();
        info.UrlEncode.Should().BeTrue();
        info.DefaultValue.Should().BeNull();
    }
}
