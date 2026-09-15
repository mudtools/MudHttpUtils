// -----------------------------------------------------------------------
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// CFG-28 / CFG-32 / 不变量 I-8、I-9：特性参数「读取口径 → 生成代码」的端到端断言。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>GeneratorSnapshotTests</c> 的全量快照互补：本文件只断言<b>关键数值</b>，
/// 因此当读取口径回归时能直接指出「哪个参数取错了值」，而不必逐行比对快照差异。
/// </para>
/// <para>
/// <b>为何既有快照测不出 CFG-28</b>：<c>GeneratorSnapshotTests.cs:370,396</c> 使用的是
/// <c>[Retry(3, 1000)]</c> —— 位置参数恰好等于默认值 1000，缺陷被完全掩盖。
/// 本文件补齐「位置参数不等于默认值」的用例。
/// </para>
/// </remarks>
public class RetryAttributeParameterPropagationTests
{
    private static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedCode) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generatorType = TestHelper.GetType("Mud.HttpUtils.HttpInvokeClassSourceGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;

        var runResult = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(compilation)
            .GetRunResult();

        var generatedCode = string.Join(
            "\n",
            runResult.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

        return (runResult.Diagnostics, generatedCode);
    }

    private static string InterfaceWith(string methodAttributes) => $$"""
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface ITestApi
            {
                [Get("/data")]
                {{methodAttributes}}
                Task<string> GetDataAsync();
            }
        }
        """;

    #region I-8：位置参数必须生效

    [Fact]
    public void T1_Retry_TwoArgConstructor_PositionalDelayIsEmitted()
    {
        var (diagnostics, code) = RunGenerator(InterfaceWith("[Retry(5, 250)]"));

        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        code.Should().Contain("MaxRetries = 5,");
        code.Should().Contain("DelayMilliseconds = 250,",
            "CFG-28：双参构造函数的位置参数 DelayMilliseconds 必须真正生效（修复前恒为 1000）");
    }

    [Fact]
    public void Retry_SingleArgConstructor_FallsBackToDefaultDelay()
    {
        var (_, code) = RunGenerator(InterfaceWith("[Retry(4)]"));

        code.Should().Contain("MaxRetries = 4,");
        code.Should().Contain("DelayMilliseconds = 1000,");
    }

    [Fact]
    public void Retry_DefaultAttribute_UsesAttributeDefaults()
    {
        var (_, code) = RunGenerator(InterfaceWith("[Retry]"));

        code.Should().Contain("MaxRetries = 3,");
        code.Should().Contain("DelayMilliseconds = 1000,");
        code.Should().Contain("UseExponentialBackoff = true,");
    }

    #endregion

    #region I-9：命名参数优先（覆盖位置参数）

    [Fact]
    public void T2_Retry_NamedDelayWinsOverPositionalDelay()
    {
        var (_, code) = RunGenerator(InterfaceWith("[Retry(5, 250, DelayMilliseconds = 700)]"));

        code.Should().Contain("DelayMilliseconds = 700,");
        code.Should().Contain("MaxRetries = 5,");
        code.Should().NotContain("DelayMilliseconds = 250,");
    }

    [Fact]
    public void CircuitBreaker_NamedFailureThresholdWinsOverPositional()
    {
        var (diagnostics, code) = RunGenerator(
            InterfaceWith("[CircuitBreaker(5, FailureThreshold = 7)]"));

        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        code.Should().Contain("FailureThreshold = 7,");
    }

    [Fact]
    public void Timeout_NamedTimeoutWinsOverPositional()
    {
        var (diagnostics, code) = RunGenerator(
            InterfaceWith("[Timeout(1000, TimeoutMilliseconds = 2000)]"));

        diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        code.Should().Contain("TimeoutEnabled = true,");
        code.Should().Contain("TimeoutMilliseconds = 2000,");
    }

    [Fact]
    public void Cache_NamedDurationWinsOverPositional()
    {
        var (_, code) = RunGenerator(InterfaceWith("[Cache(600, DurationSeconds = 120)]"));

        code.Should().Contain("DurationSeconds = 120,");
    }

    #endregion

    #region 既有语义不得被破坏

    [Fact]
    public void Timeout_NotDeclared_KeepsTimeoutDisabled()
    {
        var (diagnostics, code) = RunGenerator(InterfaceWith("[Retry(3)]"));

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT027");
        code.Should().Contain("TimeoutEnabled = false,");
        code.Should().Contain("TimeoutMilliseconds = 0,");
    }

    [Fact]
    public void Retry_ExistingSnapshotShape_Unchanged()
    {
        // 与 GeneratorSnapshotTests 使用的 [Retry(3, 1000)] 同形：参数等于默认值 ⇒ 生成结果必须与修复前一致
        // （本用例是「快照零漂移」的轻量守卫；全量快照仍在 GeneratorSnapshotTests 中）。
        var (_, code) = RunGenerator(InterfaceWith("[Retry(3, 1000)]"));

        code.Should().Contain("MaxRetries = 3,");
        code.Should().Contain("DelayMilliseconds = 1000,");
    }

    #endregion
}
