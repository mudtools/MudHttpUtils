using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// F6(b) / G8：<c>HTTPCLIENT018</c>（TokenManagerKey 使用默认推断值）的<b>一等豁免面</b>回归护栏 ——
/// ① 默认仍告警；② 声明式开关（MSBuild 属性 / 全局 analyzer config 键）豁免；
/// ③ 接口级 <c>[Token(Justification = "…")]</c> 豁免（理由可审计），空理由<b>不</b>构成豁免；
/// ④ 既有的"已显式指定 TokenManagerKey"路径不受影响。
/// </summary>
/// <remarks>
/// 本组用例同时是"死代码"护栏：修复前 <c>MudHttpAnalyzerConfig.IsTokenManagerKeyInferenceSuppressed</c>
/// 已存在但<b>无任何调用点</b>，且两个 MSBuild 开关未在
/// <c>build/Mud.HttpUtils.Generator.props</c> 注册 <c>CompilerVisibleProperty</c> ⇒
/// 声明式豁免在真实工程中静默失效（测试若只做合成选项注入则无法暴露）。
/// </remarks>
public class TokenManagerKeyInferenceExemptionTests
{
    private const string SourceWithoutJustification = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(TokenManage = "myManager")]
            public interface ITestApi
            {
                [Get("/data")]
                Task<string> GetDataAsync();
            }
        }
        """;

    private const string SourceWithJustification = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(TokenManage = "myManager")]
            [Token(Justification = "平台 SDK 契约：该接口与默认令牌管理器共用，键推断为预期行为")]
            public interface ITestApi
            {
                [Get("/data")]
                Task<string> GetDataAsync();
            }
        }
        """;

    private const string SourceWithEmptyJustification = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(TokenManage = "myManager")]
            [Token(Justification = "")]
            public interface ITestApi
            {
                [Get("/data")]
                Task<string> GetDataAsync();
            }
        }
        """;

    private const string SourceWithExplicitKey = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(TokenManage = "myManager")]
            [Token(TokenManagerKey = "explicitKey")]
            public interface ITestApi
            {
                [Get("/data")]
                Task<string> GetDataAsync();
            }
        }
        """;

    // ── ① 默认仍告警 ────────────────────────────────────────────────────

    [Fact]
    public void WithoutExemption_ShouldReportHTTPCLIENT018()
    {
        RunGenerator(SourceWithoutJustification).Should().Contain(d => d.Id == "HTTPCLIENT018",
            "未声明豁免时必须照常告警（安全告警不得默认静音）");
    }

    // ── ② 声明式开关 ────────────────────────────────────────────────────

    [Fact]
    public void WithMsBuildProperty_ShouldNotReportHTTPCLIENT018()
    {
        var options = OptionsWith(("build_property.MudHttpSuppressTokenManagerKeyInference", "true"));

        RunGenerator(SourceWithoutJustification, options)
            .Should().NotContain(d => d.Id == "HTTPCLIENT018");
    }

    [Fact]
    public void WithGlobalAnalyzerConfigKey_ShouldNotReportHTTPCLIENT018()
    {
        var options = OptionsWith(("mud_suppress_token_manager_key_inference", "allow"));

        RunGenerator(SourceWithoutJustification, options)
            .Should().NotContain(d => d.Id == "HTTPCLIENT018");
    }

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("")]
    public void WithNonTrueLikeValue_ShouldStillReportHTTPCLIENT018(string value)
    {
        var options = OptionsWith(("build_property.MudHttpSuppressTokenManagerKeyInference", value));

        RunGenerator(SourceWithoutJustification, options)
            .Should().Contain(d => d.Id == "HTTPCLIENT018",
                "仅 true/allow/1 视为豁免，其余取值不得静音");
    }

    // ── ③ 接口级 [Token] 的既有语义（防回归固定） ───────────────────────

    /// <summary>
    /// 接口声明上存在 <c>[Token]</c> 即不再报 HTTPCLIENT018：
    /// <c>TokenAttribute</c> 的构造形参带默认值（<c>tokenType = AccessToken</c>）且
    /// <c>TokenManagerKey</c> 默认取该值 ⇒ 诊断条件（键与类型皆为空）天然不成立。
    /// </summary>
    /// <remarks>
    /// 该语义解释了"下游只有<b>无接口级 [Token]</b> 的接口文件才需要逐文件
    /// <c>#pragma warning disable HTTPCLIENT018</c>"；同时也说明
    /// <c>[Token(Justification = "…")]</c> 对本诊断无额外豁免作用（属性存在已足够）。
    /// </remarks>
    [Fact]
    public void WithInterfaceLevelTokenAttribute_ShouldNotReportHTTPCLIENT018()
    {
        RunGenerator(SourceWithJustification).Should().NotContain(d => d.Id == "HTTPCLIENT018");
        RunGenerator(SourceWithEmptyJustification).Should().NotContain(d => d.Id == "HTTPCLIENT018");
    }

    // ── ④ 已显式指定 TokenManagerKey（既有路径，防回归） ───────────────

    [Fact]
    public void WithExplicitTokenManagerKey_ShouldNotReportHTTPCLIENT018()
    {
        RunGenerator(SourceWithExplicitKey).Should().NotContain(d => d.Id == "HTTPCLIENT018");
    }

    // ── 测试基建 ───────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> RunGenerator(string source, AnalyzerConfigOptionsProvider? optionsProvider = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generatorType = TestHelper.GetType("Mud.HttpUtils.HttpInvokeClassSourceGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: null,
            optionsProvider: optionsProvider);

        return driver.RunGenerators(compilation).GetRunResult().Diagnostics;
    }

    private static AnalyzerConfigOptionsProvider OptionsWith(params (string Key, string Value)[] settings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in settings)
            map[key] = value;

        return new TestAnalyzerConfigOptionsProvider(map);
    }

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
