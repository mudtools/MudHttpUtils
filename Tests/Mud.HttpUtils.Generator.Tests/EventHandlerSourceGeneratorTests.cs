namespace Mud.HttpUtils.Generator.Tests;

public class EventHandlerSourceGeneratorTests
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

    private (ImmutableArray<Diagnostic> diagnostics, Compilation outputCompilation) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var generatorType = TestHelper.GetType("Mud.HttpUtils.EventHandlerSourceGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (diagnostics, outputCompilation);
    }

    private string? GetGeneratedCode(Compilation outputCompilation)
    {
        return outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString();
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

        var (diagnostics, outputCompilation) = RunGenerator(source);

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

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty("带有 [GenerateEventHandler] 特性的类应生成事件处理器代码");
        generatedCode.Should().Contain("UserCreatedEventHandler", "类名以 Result 结尾时应移除 Result 后缀并添加 EventHandler");
        generatedCode.Should().Contain("user.created", "生成的代码应包含 EventType 值");
        generatedCode.Should().Contain("SupportedEventType", "生成的代码应包含 SupportedEventType 属性");
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

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("CustomUserUpdateHandler", "应使用自定义 HandlerClassName");
        generatedCode.Should().Contain("user.updated");
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

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("CustomNamespace.Handlers", "应使用自定义 HandlerNamespace");
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

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("OrderCreatedEventHandler", "类名以 Result 结尾时应移除 Result 后缀");
        generatedCode.Should().NotContain("OrderCreatedResultEventHandler", "不应保留 Result 后缀");
    }

    [Fact]
    public void Generator_WithNoResultSuffix_ShouldAppendEventHandler()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(EventType = ""payment.processed"")]
    public class PaymentProcessed
    {
        public string PaymentId { get; set; }
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("PaymentProcessedEventHandler", "类名不以 Result 结尾时应直接添加 EventHandler 后缀");
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

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("app.table.record.created");
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

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("FeishuEventHeaderV2", "应包含自定义 HeaderType");
        generatedCode.Should().Contain("DriveFileEditResult, FeishuEventHeaderV2", "基类应使用双泛型参数");
    }

    [Fact]
    public void Generator_GeneratesAbstractPartialClass()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(EventType = ""test.event"")]
    public class TestEventResult
    {
        public string Data { get; set; }
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("abstract partial class");
        generatedCode.Should().Contain("TestEventEventHandler");
    }

    [Fact]
    public void Generator_GeneratesConstructorWithParameters()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(
        EventType = ""user.login"",
        ConstructorParameters = ""IFeishuEventDeduplicator deduplicator, ILogger logger"",
        ConstructorBaseCall = ""deduplicator, logger"")]
    public class UserLoginResult
    {
        public string UserId { get; set; }
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("IFeishuEventDeduplicator deduplicator", "构造函数应包含自定义参数");
        generatedCode.Should().Contain("ILogger logger");
        generatedCode.Should().Contain(": base(deduplicator, logger)", "构造函数应调用基类构造函数");
    }

    [Fact]
    public void Generator_UsesDefaultNamespaceWhenNotSpecified()
    {
        var source = @"
using Mud.HttpUtils;

namespace MyApp.Events
{
    [GenerateEventHandler(EventType = ""my.event"")]
    public class MyEventResult
    {
        public string Data { get; set; }
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("MyApp.Events", "未指定 HandlerNamespace 时应使用原始类所在命名空间");
    }

    [Fact]
    public void Generator_GeneratesCompilerGeneratedAttribute()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(EventType = ""attr.test"")]
    public class AttrTestResult
    {
        public string Data { get; set; }
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("CompilerGenerated");
    }

    [Fact]
    public void Generator_WithInheritedFrom_ShouldUseCustomBaseClass()
    {
        var source = @"
using Mud.HttpUtils;

namespace TestNamespace
{
    [GenerateEventHandler(
        EventType = ""custom.event"",
        InheritedFrom = ""CustomBaseHandler"")]
    public class CustomEventResult
    {
        public string Data { get; set; }
    }
}";

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        generatedCode.Should().NotBeNullOrEmpty();
        generatedCode.Should().Contain("CustomBaseHandler<CustomEventResult>", "应使用自定义基类名");
    }
}
