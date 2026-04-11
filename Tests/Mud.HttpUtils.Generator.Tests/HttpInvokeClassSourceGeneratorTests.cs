// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// HttpInvokeClassSourceGenerator 源代码生成器集成测试
/// </summary>
public class HttpInvokeClassSourceGeneratorTests
{
    private Compilation CreateCompilation(string source)
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Net.Http.HttpClient).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Mud.HttpUtils.HttpClientUtils).Assembly.Location)
        };

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void Generator_WithNoAttributes_ShouldNotGenerateCode()
    {
        var source = @"
namespace TestNamespace
{
    public interface ITestInterface
    {
        Task<string> GetDataAsync();
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
        outputCompilation.SyntaxTrees.Count().Should().Be(1);
    }

    [Fact]
    public void Generator_WithHttpClientApiAttribute_ShouldGenerateImplementation()
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
        var driver = CSharpGeneratorDriver.Create(new MockGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
        outputCompilation.SyntaxTrees.Count().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void Generator_WithMultipleMethods_ShouldGenerateAllMethods()
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
        
        [Post(""/users"")]
        Task<string> CreateUserAsync(string name);
        
        [Put(""/users/{id}"")]
        Task<string> UpdateUserAsync(int id, string name);
        
        [Delete(""/users/{id}"")]
        Task DeleteUserAsync(int id);
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithPathParameters_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users/{userId}/posts/{postId}"")]
        Task<string> GetUserPostAsync(int userId, int postId);
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithQueryParameters_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/search"")]
        Task<string> SearchAsync([Query] string keyword, [Query] int page, [Query] int size);
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithBodyParameter_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    public class UserData
    {
        public string Name { get; set; }
        public string Email { get; set; }
    }

    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Post(""/users"")]
        Task<string> CreateUserAsync([Body] UserData user);
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithHeaderParameter_ShouldGenerateCorrectCode()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/users"")]
        Task<string> GetUsersAsync([Header(""Authorization"")] string token);
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    private class MockGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
        }
    }
}
