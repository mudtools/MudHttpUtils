// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-04：继承组合矩阵（基类运行模式 × 派生运行模式 = 9 组）。
/// </summary>
/// <remarks>
/// <para>
/// <b>背景</b>：<c>base(...)</c> 的位置实参此前只按<b>派生侧</b>模式生成，从不读取基类模式；
/// 而 <c>BaseClassValidator</c> 对「无命名空间点的生成类名」会短路跳过校验 ⇒ 5 种组合产出
/// 与基类构造函数形参不匹配的 <c>base(...)</c>（CS1503/CS1729）且<b>无任何生成器诊断</b>。
/// </para>
/// <para>
/// <b>支持的组合（4 组）</b>：基/派生同为 <c>Default</c>、同为 <c>TokenManage</c>、同为 <c>HttpClient</c>，
/// 以及「基 <c>Default</c> × 派生 <c>TokenManage</c>」（派生持有令牌源，可向基类回传默认应用）。
/// <b>不支持的组合（5 组）</b>须报 <c>HTTPCLIENT035</c>（Error）且<b>不产出派生实现类</b>
/// （避免留下不可编译产物）。
/// </para>
/// </remarks>
public class InheritedFromModeMatrixTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;
        """;

    /// <summary>三种运行模式（与 <c>[HttpClientApi]</c> 的术语一致）。</summary>
    public enum RuntimeMode
    {
        Default,
        TokenManager,
        HttpClient,
    }

    public static TheoryData<RuntimeMode, RuntimeMode> Matrix()
    {
        var data = new TheoryData<RuntimeMode, RuntimeMode>();
        foreach (RuntimeMode baseMode in Enum.GetValues<RuntimeMode>())
        {
            foreach (RuntimeMode derivedMode in Enum.GetValues<RuntimeMode>())
            {
                data.Add(baseMode, derivedMode);
            }
        }

        return data;
    }

    /// <summary>
    /// 9 组合：（X,X）与（Default,TokenManager）通过；其余 5 组报 <c>HTTPCLIENT035</c> 且不产出派生实现类。
    /// </summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public void Matrix_BaseModeByDerivedMode_BehavesAsDeclared(RuntimeMode baseMode, RuntimeMode derivedMode)
    {
        var source = BuildSource(baseMode, derivedMode);
        var (_, output, diagnostics) = RunGenerator(source);

        var isSupported = baseMode == derivedMode
            || (baseMode == RuntimeMode.Default && derivedMode == RuntimeMode.TokenManager);

        var mismatch = diagnostics.Where(d => d.Id == "HTTPCLIENT035").ToList();

        if (isSupported)
        {
            mismatch.Should().BeEmpty(
                $"基 {baseMode} × 派生 {derivedMode} 属受支持组合，不应报 HTTPCLIENT035");

            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(
                $"基 {baseMode} × 派生 {derivedMode} 的生成产物必须可编译；" +
                $"错误：{string.Join("\n", output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))}");
        }
        else
        {
            mismatch.Should().ContainSingle(
                $"基 {baseMode} × 派生 {derivedMode} 的 base(...) 实参必然类型错位，必须显式报 HTTPCLIENT035");

            // 派生接口不产出实现类：只应生成「基接口」的实现类（输入树 1 + 生成树 1）。
            output.SyntaxTrees.Count().Should().Be(2,
                "报 HTTPCLIENT035 后不得产出派生实现类（避免留下不可编译产物）");
        }
    }

    /// <summary>
    /// 反例：<c>InheritedFrom</c> 指向<b>宿主自维护基类</b>（非生成器产出的抽象类）时，
    /// 无法推知基类契约，<b>不得</b>报 <c>HTTPCLIENT035</c>（否则误伤合法用法）。
    /// </summary>
    [Fact]
    public void UserMaintainedBaseClass_IsNotFlagged()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public abstract class MyHandWrittenBase
                {
                    protected MyHandWrittenBase(IMudAppContext appContext, IAppContextHolder holder)
                    {
                    }
                }

                [HttpClientApi(InheritedFrom = "MyHandWrittenBase")]
                public interface ITestApi
                {
                    [Get("/data")]
                    Task<string> GetDataAsync();
                }
            }
            """;

        var (_, _, diagnostics) = RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT035",
            "InheritedFromInterfaceName 为空（用户自维护基类）时不得判错");
    }

    private static string BuildSource(RuntimeMode baseMode, RuntimeMode derivedMode)
        => Usings + $$"""

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi({{BaseArgs(baseMode)}})]
                public interface IBaseApi
                {
                    [Get("/base")]
                    Task<string> GetBaseAsync();
                }

                [HttpClientApi({{ModeArgs(derivedMode)}})]
                public interface ITestApi : IBaseApi
                {
                    [Get("/derived")]
                    Task<string> GetDerivedAsync();
                }
            }
            """;

    /// <summary>基接口必须是抽象（<c>IsAbstract = true</c>）才会被识别为继承目标。</summary>
    private static string BaseArgs(RuntimeMode mode)
        => mode == RuntimeMode.Default
            ? "IsAbstract = true"
            : $"IsAbstract = true, {ModeArgs(mode)}";

    /// <summary>按模式生成 <c>[HttpClientApi]</c> 的命名参数片段（Default 模式无参数）。</summary>
    private static string ModeArgs(RuntimeMode mode) => mode switch
    {
        RuntimeMode.TokenManager => "TokenManage = \"ITestTokenManager\"",
        RuntimeMode.HttpClient => "HttpClient = \"IEnhancedHttpClient\"",
        _ => string.Empty,
    };

    private static (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "InheritedFromModeMatrixTests",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (driver, output, diagnostics);
    }
}
