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

    private static class BasicReferenceAssemblies
    {
        public static List<MetadataReference> GetReferences()
        {
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Task<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Net.Http.HttpClient).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Mud.HttpUtils.HttpClientUtils).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.IO.Stream).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.IEnumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IAsyncEnumerable<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.AsyncIteratorMethodBuilder).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Caching.Memory.IMemoryCache).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Options.IOptions<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Mud.HttpUtils.Attributes.HttpClientApiAttribute).Assembly.Location),
            };

            var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            var runtimeAssemblies = new[]
            {
                "System.Runtime.dll",
                "System.Collections.Concurrent.dll",
                "System.Threading.dll",
                "System.Memory.dll",
                "System.Threading.Tasks.dll",
                "System.Collections.dll",
                "System.Linq.dll",
                "System.Net.Http.dll",
                "System.IO.dll",
                "System.Text.Json.dll",
                "System.Private.CoreLib.dll",
                "netstandard.dll",
            };

            foreach (var asm in runtimeAssemblies)
            {
                var path = Path.Combine(runtimeDir, asm);
                if (File.Exists(path))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }

            return references;
        }
    }
}
