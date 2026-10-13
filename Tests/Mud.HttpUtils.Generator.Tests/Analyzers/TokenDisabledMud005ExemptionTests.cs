using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// F-1（G-1）：MUD005（Query / Path 令牌注入模式存在泄露面）对<b>免令牌方法</b>
///（<c>[Token(Disabled = true)]</c>，有效级：方法级 &gt; 接口级）的豁免回归护栏。
/// </summary>
/// <remarks>
/// 免令牌方法不取令牌、不注入 —— URL 承载面不存在，MUD005 必须豁免（否则安全告警误报噪声）。
/// 反向钉死：未 Disabled 的 Query/Path 方法仍照常告警（安全告警不得默认静音），
/// 与 <see cref="TokenManagerQueryInjectionExemptionTests"/> 的 Justification 豁免面互不干扰。
/// </remarks>
public class TokenDisabledMud005ExemptionTests
{
    /// <summary>构造带 [HttpClientApi] 接口的测试源；<paramref name="methodTokenAttribute"/> 为空表示方法不标注 [Token]。</summary>
    private static string BuildSource(string methodTokenAttribute, string interfaceTokenAttribute = "")
        => $$"""
            using Mud.HttpUtils.Attributes;
            using Mud.HttpUtils;
            using System.Threading.Tasks;

            namespace TestApp
            {
                [HttpClientApi]
                {{interfaceTokenAttribute}}
                public interface IFoo
                {
                    {{methodTokenAttribute}}
                    Task<string> GetAsync();
                }
            }
            """;

    private static ImmutableArray<Diagnostic> RunAnalyzer(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new TokenManagerLifetimeAnalyzer();
        return compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    // ── ① 免令牌豁免（F-1 核心场景） ───────────────────────────────────

    /// <summary>方法级 <c>[Token(Disabled = true, InjectionMode = Query)]</c>：免令牌 → 无 URL 承载面 → 豁免 MUD005。</summary>
    [Fact]
    public void MethodLevelDisabledWithQueryMode_ShouldNotReportMUD005()
    {
        RunAnalyzer(BuildSource("""[Token(Disabled = true, InjectionMode = TokenInjectionMode.Query)]"""))
            .Should().NotContain(d => d.Id == "MUD005",
                "免令牌方法不取令牌，Query 承载面不存在，必须豁免");
    }

    /// <summary>Path 模式同理豁免。</summary>
    [Fact]
    public void MethodLevelDisabledWithPathMode_ShouldNotReportMUD005()
    {
        RunAnalyzer(BuildSource("""[Token(Disabled = true, InjectionMode = TokenInjectionMode.Path)]"""))
            .Should().NotContain(d => d.Id == "MUD005");
    }

    /// <summary>接口级 <c>[Token(Disabled = true, InjectionMode = Query)]</c> 对全部方法生效 → 豁免。</summary>
    [Fact]
    public void InterfaceLevelDisabledWithQueryMode_ShouldNotReportMUD005()
    {
        RunAnalyzer(BuildSource(
            methodTokenAttribute: string.Empty,
            interfaceTokenAttribute: """[Token(Disabled = true, InjectionMode = TokenInjectionMode.Query)]"""))
            .Should().NotContain(d => d.Id == "MUD005",
                "接口级 Disabled 继承阻断后全部方法免令牌，应豁免");
    }

    /// <summary>
    /// 接口级 Query + 方法级 <c>[Token(Disabled = true)]</c>（下游 clear_quota 逃生端点形态）：
    /// 方法被显式免令牌 → 豁免；其余方法不受影响。
    /// </summary>
    [Fact]
    public void MethodLevelDisabledOverInterfaceQuery_ShouldNotReportMUD005()
    {
        RunAnalyzer(BuildSource(
            methodTokenAttribute: """[Token(Disabled = true)]""",
            interfaceTokenAttribute: """[Token(InjectionMode = TokenInjectionMode.Query)]"""))
            .Should().NotContain(d => d.Id == "MUD005",
                "逃生方法显式 opt-out 后不取令牌，应豁免");
    }

    /// <summary>Disabled + Justification 共用：Disabled 豁免已足够，不冲突、不重复告警。</summary>
    [Fact]
    public void DisabledWithJustification_ShouldNotReportMUD005()
    {
        RunAnalyzer(BuildSource("""[Token(Disabled = true, InjectionMode = TokenInjectionMode.Query, Justification = "免令牌应急端点")]"""))
            .Should().NotContain(d => d.Id == "MUD005");
    }

    // ── ② 反向：未 Disabled 照常告警（安全告警不得默认静音） ────────────

    /// <summary>方法级 <c>[Token(Disabled = false, InjectionMode = Query)]</c> 显式未禁用 → 照常告警。</summary>
    [Fact]
    public void DisabledFalseWithQueryMode_ShouldStillReportMUD005()
    {
        RunAnalyzer(BuildSource("""[Token(Disabled = false, InjectionMode = TokenInjectionMode.Query)]"""))
            .Should().Contain(d => d.Id == "MUD005",
                "Disabled = false 是显式未禁用，URL 承载令牌照常告警");
    }

    /// <summary>未标注 Disabled（有效级默认 false）→ 照常告警（防豁免面过宽的假阳性）。</summary>
    [Fact]
    public void WithoutDisabledWithQueryMode_ShouldStillReportMUD005()
    {
        RunAnalyzer(BuildSource("""[Token(InjectionMode = TokenInjectionMode.Query)]"""))
            .Should().Contain(d => d.Id == "MUD005");
    }
}
