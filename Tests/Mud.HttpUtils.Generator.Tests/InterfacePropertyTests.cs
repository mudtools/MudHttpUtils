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

    [Fact]
    public void AnalyzeInterfaceProperties_WithHeaderProperty_ReturnsProperty()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Header(""X-Tenant-Id"")]
        string TenantId { get; set; }

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
        result[0].Name.Should().Be("TenantId");
        result[0].AttributeType.Should().Be("Header");
        result[0].ParameterName.Should().Be("X-Tenant-Id");
    }

    [Fact]
    public void AnalyzeInterfaceProperties_WithHeaderProperty_Replace_SetsReplaceFlag()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Header(""X-Auth-Token"", Replace = true)]
        string AuthToken { get; set; }

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
        result[0].AttributeType.Should().Be("Header");
        result[0].Replace.Should().BeTrue();
        result[0].ParameterName.Should().Be("X-Auth-Token");
    }

    [Fact]
    public void AnalyzeInterfaceProperties_WithHeaderProperty_AliasAs_SetsParameterName()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Header(AliasAs = ""X-Custom-Header"")]
        string CustomHeader { get; set; }

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
        result[0].AttributeType.Should().Be("Header");
        result[0].AliasAs.Should().Be("X-Custom-Header");
        result[0].ParameterName.Should().Be("X-Custom-Header");
    }

    [Fact]
    public void AnalyzeInterfaceProperties_WithHeaderProperty_NoName_UsesPropertyName()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Header]
        string XRequestId { get; set; }

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
        result[0].AttributeType.Should().Be("Header");
        result[0].ParameterName.Should().Be("XRequestId");
    }

    [Fact]
    public void AnalyzeInterfaceProperties_WithMixedQueryPathHeaderProperties_ReturnsAll()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Query(""version"")]
        string Version { get; set; }

        [Path(""tenantId"")]
        string TenantId { get; set; }

        [Header(""X-API-Key"")]
        string ApiKey { get; set; }

        [Header(""X-Request-Id"", FormatString = ""N"")]
        System.Guid RequestId { get; set; }

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

        result.Should().HaveCount(4);
        result.Count(p => p.AttributeType == "Query").Should().Be(1);
        result.Count(p => p.AttributeType == "Path").Should().Be(1);
        result.Count(p => p.AttributeType == "Header").Should().Be(2);

        var guidHeader = result.First(p => p.Name == "RequestId");
        guidHeader.AttributeType.Should().Be("Header");
        guidHeader.Format.Should().Be("N");
        guidHeader.ParameterName.Should().Be("X-Request-Id");
    }
}
