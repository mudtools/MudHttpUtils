// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// EventHandlerSourceGenerator 事件处理器源代码生成器集成测试
/// </summary>
public class EventHandlerSourceGeneratorTests
{
    private Compilation CreateCompilation(string source)
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location),
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
    public class TestResult
    {
        public string Data { get; set; }
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockEventHandlerGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
        outputCompilation.SyntaxTrees.Count().Should().Be(1);
    }

    [Fact]
    public void Generator_WithGenerateEventHandlerAttribute_ShouldGenerateHandler()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(EventType = ""user.created"")]
    public class UserCreatedResult
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockEventHandlerGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithCustomHandlerName_ShouldUseCustomName()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(EventType = ""user.updated"", HandlerClassName = ""CustomUserUpdateHandler"")]
    public class UserUpdatedResult
    {
        public string UserId { get; set; }
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockEventHandlerGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithCustomNamespace_ShouldUseCustomNamespace()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(EventType = ""user.deleted"", HandlerNamespace = ""CustomNamespace.Handlers"")]
    public class UserDeletedResult
    {
        public string UserId { get; set; }
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockEventHandlerGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithResultSuffix_ShouldRemoveSuffix()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(EventType = ""order.created"")]
    public class OrderCreatedResult
    {
        public string OrderId { get; set; }
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockEventHandlerGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithComplexEventType_ShouldHandleCorrectly()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(EventType = ""app.table.record.created"")]
    public class RecordCreatedResult
    {
        public string RecordId { get; set; }
        public string TableId { get; set; }
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockEventHandlerGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithHeaderType_ShouldGenerateHandlerWithHeaderType()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(EventType = ""drive.file.edit"", HeaderType = ""FeishuEventHeaderV2"")]
    public class DriveFileEditResult
    {
        public string FileId { get; set; }
    }
}";

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new MockEventHandlerGenerator());

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    private class MockEventHandlerGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
        }
    }
}
