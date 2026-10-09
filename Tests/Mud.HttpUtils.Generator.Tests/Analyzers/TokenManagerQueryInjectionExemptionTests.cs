using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// F6(b)：MUD005（Query/Path 令牌注入）的<b>一等豁免面</b>回归护栏 ——
/// ① 默认仍告警；② <c>[Token(Justification = "…")]</c> 成员级豁免（理由可审计）；
/// ③ 空理由<b>不</b>构成豁免；④ MSBuild 属性 / .editorconfig 键的声明式豁免。
/// </summary>
public class TokenManagerQueryInjectionExemptionTests
{
    private static string BuildSource(string injectionMode = "TokenInjectionMode.Query", string? justificationLiteral = null)
    {
        var justification = justificationLiteral is null
            ? string.Empty
            : $", Justification = {justificationLiteral}";

        return $$"""
            using Mud.HttpUtils.Attributes;
            using Mud.HttpUtils;
            using System.Threading.Tasks;

            namespace TestApp
            {
                [HttpClientApi]
                public interface IFoo
                {
                    [Token(TokenType = "TenantAccessToken", InjectionMode = {{injectionMode}}{{justification}})]
                    Task<string> GetAsync();
                }
            }
            """;
    }

    private static ImmutableArray<Diagnostic> RunAnalyzer(string source, AnalyzerOptions? options = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new TokenManagerLifetimeAnalyzer();
        var withAnalyzers = options is null
            ? compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            : compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer), options);

        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static AnalyzerOptions OptionsWith(params (string Key, string Value)[] settings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in settings)
            map[key] = value;

        return new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(map));
    }

    // ── ① 默认仍告警 ────────────────────────────────────────────────────

    [Fact]
    public void QueryInjection_WithoutExemption_ShouldReportMUD005()
    {
        RunAnalyzer(BuildSource()).Should().Contain(d => d.Id == "MUD005",
            "未声明豁免时必须照常告警（安全告警不得默认静音）");
    }

    [Fact]
    public void PathInjection_WithoutExemption_ShouldReportMUD005()
    {
        RunAnalyzer(BuildSource("TokenInjectionMode.Path")).Should().Contain(d => d.Id == "MUD005");
    }

    [Fact]
    public void HeaderInjection_ShouldNotReportMUD005()
    {
        RunAnalyzer(BuildSource("TokenInjectionMode.Header")).Should().NotContain(d => d.Id == "MUD005");
    }

    // ── ② 成员级豁免（Justification） ───────────────────────────────────

    [Fact]
    public void QueryInjection_WithJustification_ShouldNotReportMUD005()
    {
        RunAnalyzer(BuildSource(justificationLiteral: "\"企业微信官方契约：凭据强制置于 Query\""))
            .Should().NotContain(d => d.Id == "MUD005",
                "显式理由即一等豁免（免去逐文件 #pragma）");
    }

    [Fact]
    public void QueryInjection_WithEmptyJustification_ShouldStillReportMUD005()
    {
        RunAnalyzer(BuildSource(justificationLiteral: "\"\""))
            .Should().Contain(d => d.Id == "MUD005",
                "空理由不构成豁免 —— 不允许用'空 Justification'静默规避");
    }

    // ── ③ 声明式豁免（MSBuild / .editorconfig） ─────────────────────────

    [Fact]
    public void QueryInjection_WithMsBuildProperty_ShouldNotReportMUD005()
    {
        var options = OptionsWith(("build_property.MudHttpSuppressQueryTokenInjection", "true"));

        RunAnalyzer(BuildSource(), options).Should().NotContain(d => d.Id == "MUD005");
    }

    [Fact]
    public void QueryInjection_WithEditorConfigKey_ShouldNotReportMUD005()
    {
        var options = OptionsWith(("mud_suppress_query_token_injection", "allow"));

        RunAnalyzer(BuildSource(), options).Should().NotContain(d => d.Id == "MUD005");
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("no")]
    public void QueryInjection_WithNonTrueLikeValue_ShouldStillReportMUD005(string value)
    {
        var options = OptionsWith(("build_property.MudHttpSuppressQueryTokenInjection", value));

        RunAnalyzer(BuildSource(), options).Should().Contain(d => d.Id == "MUD005",
            "仅 true/allow/1 视为豁免，其余取值不得静音");
    }

    // ── 测试替身：AnalyzerConfigOptionsProvider ────────────────────────

    private sealed class TestAnalyzerConfigOptionsProvider(Dictionary<string, string> values)
        : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class TestAnalyzerConfigOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
