using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.Generator.Tests;

public class ArrayQueryAttributeTests
{
    [Fact]
    public void Constructor_Default_CreatesInstance()
    {
        var attr = new ArrayQueryAttribute();

        attr.Name.Should().BeNull();
        attr.Separator.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithName_SetsName()
    {
        var attr = new ArrayQueryAttribute("ids");

        attr.Name.Should().Be("ids");
        attr.Separator.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithNameAndSeparator_SetsBoth()
    {
        var attr = new ArrayQueryAttribute("ids", ",");

        attr.Name.Should().Be("ids");
        attr.Separator.Should().Be(",");
    }

    [Fact]
    public void Constructor_WithNameAndNullSeparator_SetsNameOnly()
    {
        var attr = new ArrayQueryAttribute("tags", null);

        attr.Name.Should().Be("tags");
        attr.Separator.Should().BeNull();
    }

    [Fact]
    public void Separator_WithComma_AllowsCommaSeparatedValues()
    {
        var attr = new ArrayQueryAttribute("ids", ",");

        attr.Separator.Should().Be(",");
    }

    [Fact]
    public void Separator_WithSemicolon_AllowsSemicolonSeparatedValues()
    {
        var attr = new ArrayQueryAttribute("ids", ";");

        attr.Separator.Should().Be(";");
    }

    [Fact]
    public void AttributeUsage_AllowsMethodTarget()
    {
        var usage = typeof(ArrayQueryAttribute).GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>().FirstOrDefault();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().Be(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Parameter);
        usage.AllowMultiple.Should().BeTrue();
    }

    [Fact]
    public void Generator_WithArrayQueryNoSeparator_ShouldGenerateCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/items"")]
        Task<string> GetItemsAsync([ArrayQuery] int[] ids);
    }
}";

        var compilation = CreateCompilation(source);
        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString();
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetItemsAsync");
    }

    [Fact]
    public void Generator_WithArrayQueryWithSeparator_ShouldGenerateCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/items"")]
        Task<string> GetItemsAsync([ArrayQuery(""ids"", "","")] int[] itemIds);
    }
}";

        var compilation = CreateCompilation(source);
        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString();
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("GetItemsAsync");
    }

    [Fact]
    public void Generator_WithArrayQueryListType_ShouldGenerateCode()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;
using System.Collections.Generic;

namespace TestNamespace
{
    [HttpClientApi(""https://api.example.com"")]
    public interface ITestApi
    {
        [Get(""/search"")]
        Task<string> SearchAsync([ArrayQuery(""tags"")] List<string> tags);
    }
}";

        var compilation = CreateCompilation(source);
        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString();
        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("SearchAsync");
    }

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
}
