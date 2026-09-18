// -----------------------------------------------------------------------
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// CFG-29 / CFG-32：<c>HTTPCLIENT026</c>（<c>[CircuitBreaker]</c> 值域）与 <c>HTTPCLIENT027</c>（<c>[Timeout]</c> 非正）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为何必须在生成器侧校验</b>：Roslyn 从不实例化 Attribute（读到的是 <c>AttributeData</c>），
/// 写在 Attribute <c>setter</c> 里的校验在任何编译路径下都不会执行。
/// </para>
/// <para>
/// 每个「报错」用例都配一条 <b>反例</b>（合法取值 ⇒ 无诊断），防止把正常配置误判为非法。
/// </para>
/// </remarks>
public class ResilienceAttributeRangeDiagnosticTests
{
    private static ImmutableArray<Diagnostic> RunGenerator(string methodAttributes)
    {
        var source = $$"""
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

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generatorType = TestHelper.GetType("Mud.HttpUtils.HttpInvokeClassSourceGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;

        return CSharpGeneratorDriver.Create(generator).RunGenerators(compilation).GetRunResult().Diagnostics;
    }

    private static void AssertReports(string methodAttributes, string diagnosticId)
    {
        var diagnostics = RunGenerator(methodAttributes);

        var diagnostic = diagnostics.FirstOrDefault(d => d.Id == diagnosticId);
        diagnostic.Should().NotBeNull(
            $"期望 {methodAttributes} 触发 {diagnosticId}；实际诊断：{string.Join(", ", diagnostics.Select(d => d.Id))}");
        diagnostic!.Severity.Should().Be(DiagnosticSeverity.Error);
    }

    private static void AssertNoDiagnostic(string methodAttributes, string diagnosticId)
    {
        var diagnostics = RunGenerator(methodAttributes);

        diagnostics.Should().NotContain(d => d.Id == diagnosticId,
            $"{methodAttributes} 为合法取值，不应触发 {diagnosticId}");
    }

    #region HTTPCLIENT026 —— [CircuitBreaker]

    [Fact]
    public void T3_FailureThresholdOver100_WithSampling_ReportsHttpClient026()
    {
        // 高级熔断模式下 FailureThreshold 是失败率百分比；200 会在运行时被静默压成 100%
        AssertReports("[CircuitBreaker(200, SamplingDurationSeconds = 30)]", "HTTPCLIENT026");
    }

    [Fact]
    public void T4_MinimumThroughputLessThanTwo_WithSampling_ReportsHttpClient026()
    {
        AssertReports("[CircuitBreaker(5, SamplingDurationSeconds = 30, MinimumThroughput = 1)]", "HTTPCLIENT026");
    }

    [Fact]
    public void T5_FailureThresholdOver100_WithoutSampling_NoDiagnostic()
    {
        // 反例：SamplingDurationSeconds = 0 时 FailureThreshold 语义是「连续失败次数」，
        // 200 次才熔断虽不常见但合法（运行时按原值使用，不做 clamp）。
        AssertNoDiagnostic("[CircuitBreaker(200)]", "HTTPCLIENT026");
    }

    [Fact]
    public void FailureThresholdLessThanOne_WithoutSampling_ReportsHttpClient026()
    {
        // 简单熔断模式下该值直传 CircuitBreakerAsync(exceptionsAllowedBeforeBreaking:)，
        // 0/负值会让熔断策略构建失败 —— 必须无条件校验（v3.1 复核补强的条件）。
        AssertReports("[CircuitBreaker(0)]", "HTTPCLIENT026");
        AssertReports("[CircuitBreaker(-1)]", "HTTPCLIENT026");
    }

    [Fact]
    public void BreakDurationSecondsNonPositive_ReportsHttpClient026()
    {
        AssertReports("[CircuitBreaker(5, BreakDurationSeconds = 0)]", "HTTPCLIENT026");
    }

    [Fact]
    public void MinimumThroughputLessThanTwo_WithoutSampling_NoDiagnostic()
    {
        // 反例：MinimumThroughput 仅在 SamplingDurationSeconds > 0 时被消费，
        // 无条件校验会产生误报（v3.1 复核修正项）。
        AssertNoDiagnostic("[CircuitBreaker(5, MinimumThroughput = 1)]", "HTTPCLIENT026");
    }

    [Fact]
    public void CircuitBreaker_ValidValues_NoDiagnostic()
    {
        AssertNoDiagnostic("[CircuitBreaker(5)]", "HTTPCLIENT026");
        AssertNoDiagnostic("[CircuitBreaker(5, BreakDurationSeconds = 30, SamplingDurationSeconds = 60, MinimumThroughput = 10)]",
            "HTTPCLIENT026");
        AssertNoDiagnostic("[CircuitBreaker(100, SamplingDurationSeconds = 30, MinimumThroughput = 2)]", "HTTPCLIENT026");
    }

    [Fact]
    public void CircuitBreaker_NotDeclared_NoDiagnostic()
    {
        AssertNoDiagnostic("[Retry(3)]", "HTTPCLIENT026");
    }

    #endregion

    #region HTTPCLIENT027 —— [Timeout]

    [Fact]
    public void T8_TimeoutZero_ReportsHttpClient027()
    {
        AssertReports("[Timeout(0)]", "HTTPCLIENT027");
    }

    [Fact]
    public void TimeoutNegative_ReportsHttpClient027()
    {
        AssertReports("[Timeout(-1)]", "HTTPCLIENT027");
    }

    [Fact]
    public void T9_TimeoutNotDeclared_NoDiagnostic()
    {
        // 反例：未声明 [Timeout] 表示 MethodTimeoutEnabled = false（既有语义），不得误报
        AssertNoDiagnostic("[Retry(3)]", "HTTPCLIENT027");
    }

    [Fact]
    public void Timeout_Positive_NoDiagnostic()
    {
        AssertNoDiagnostic("[Timeout(1)]", "HTTPCLIENT027");
        AssertNoDiagnostic("[Timeout(5000)]", "HTTPCLIENT027");
    }

    [Fact]
    public void Timeout_NamedArgumentOverridesIllegalPositional_NoDiagnostic()
    {
        // I-9：命名参数优先 ⇒ 有效值为 1000，不应报错（修复前「位置优先」会读到 -1 而漏判/误判）
        AssertNoDiagnostic("[Timeout(-1, TimeoutMilliseconds = 1000)]", "HTTPCLIENT027");
    }

    #endregion
}
