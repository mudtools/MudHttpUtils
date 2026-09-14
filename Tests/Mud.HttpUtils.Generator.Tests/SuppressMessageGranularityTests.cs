namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// [UnconditionalSuppressMessage] 压制粒度回归测试（Phase 19.2 / D6/D14/D20）。
/// </summary>
/// <remarks>
/// 验证类级 [UnconditionalSuppressMessage] 已移除，
/// 改为方法级精准压制（WriteMethodLevelSuppressMessage），仅覆盖经执行器间接 JSON 序列化的生成方法。
/// </remarks>
public class SuppressMessageGranularityTests
{
    private static (ImmutableArray<Diagnostic> diagnostics, string? generatedCode) RunGenerator(string source)
    {
        var references = BasicReferenceAssemblies.GetReferences();
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedCode = outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString();
        return (diagnostics, generatedCode);
    }

    private const string InterfaceSource = """
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi("https://api.example.com")]
            public interface ITestApi
            {
                [Get("/users")]
                Task<string> GetUsersAsync();
            }
        }
        """;

    /// <summary>
    /// 验证生成代码中类级 [UnconditionalSuppressMessage] 已移除（Phase 19.2）。
    /// </summary>
    [Fact]
    public void GeneratedCode_ClassLevelSuppressMessage_Removed()
    {
        var (_, generatedCode) = RunGenerator(InterfaceSource);

        generatedCode.Should().NotBeNullOrEmpty("应生成实现代码");

        // 类级压制应已移除：类声明行之前不应有 UnconditionalSuppressMessage
        // 类声明形如 "internal class TestApiImplementation : ITestApi"
        var classDeclarationLine = generatedCode!
            .Split('\n')
            .FirstOrDefault(l => l.Contains("internal") && l.Contains("class") && l.Contains("ITestApi"));

        classDeclarationLine.Should().NotBeNull("应找到类声明行");

        // 类声明行本身不应含 UnconditionalSuppressMessage
        classDeclarationLine!.Should().NotContain("UnconditionalSuppressMessage",
            "类级压制应已移除（Phase 19.2）");
    }

    /// <summary>
    /// 验证生成代码中方法级 [UnconditionalSuppressMessage] 已添加（Phase 19.2 / D20）。
    /// </summary>
    [Fact]
    public void GeneratedCode_MethodLevelSuppressMessage_Present()
    {
        var (_, generatedCode) = RunGenerator(InterfaceSource);

        generatedCode.Should().NotBeNullOrEmpty("应生成实现代码");

        // 方法级压制应存在：生成代码中应含 UnconditionalSuppressMessage（在方法级别）
        generatedCode.Should().Contain("UnconditionalSuppressMessage",
            "方法级压制应存在（WriteMethodLevelSuppressMessage）");

        // 应含 IL2026 和 IL3050 两个压制
        generatedCode.Should().Contain("IL2026");
        generatedCode.Should().Contain("IL3050");
    }

    /// <summary>
    /// 验证方法级压制仅在 NET6_0_OR_GREATER 条件下生成。
    /// </summary>
    [Fact]
    public void GeneratedCode_MethodLevelSuppressMessage_WrappedInNet6Condition()
    {
        var (_, generatedCode) = RunGenerator(InterfaceSource);

        generatedCode.Should().NotBeNullOrEmpty("应生成实现代码");

        // 应含 #if NET6_0_OR_GREATER 条件编译指令
        generatedCode.Should().Contain("#if NET6_0_OR_GREATER");
        generatedCode.Should().Contain("#endif");
    }
}
