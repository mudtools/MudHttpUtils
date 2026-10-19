using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// F6(b) / G8：<c>HTTPCLIENT018</c>（TokenManagerKey 使用默认推断值）的<b>一等豁免面</b>回归护栏 ——
/// ① 默认仍告警；② 声明式开关（MSBuild 属性 / 全局 analyzer config 键）豁免；
/// ③ 目录级 <c>.editorconfig</c> 键（树粒度）豁免；④ 接口级 <c>[Token(Justification = "…")]</c> 豁免
/// （理由可审计），空理由不构成额外豁免但属性存在已足够；⑤ 既有的"已显式指定 TokenManagerKey"路径不受影响；
/// ⑥ 未配置 TokenManage 的接口不报。
/// </summary>
/// <remarks>
/// <para>
/// 诊断载体为 <see cref="TokenManagerKeyInferenceAnalyzer"/>（自生成器迁移）：增量生成器管道的
/// per-interface 缓存输入不含目录级 <c>.editorconfig</c> 键，树粒度豁免在生成器路径无法生效；
/// 分析器随编译变化自然重算且天然持有 <c>GetOptions(语法树)</c>。
/// </para>
/// <para>
/// 本组用例同时是"死代码"护栏：修复前 <c>MudHttpAnalyzerConfig.IsTokenManagerKeyInferenceSuppressed</c>
/// 已存在但无任何调用点，且两个 MSBuild 开关未在 <c>build/Mud.HttpUtils.Generator.props</c> 注册
/// <c>CompilerVisibleProperty</c> ⇒ 声明式豁免在真实工程中静默失效（测试若只做合成选项注入则无法暴露）。
/// </para>
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

    private const string SourceWithoutTokenManage = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
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
        RunAnalyzer(SourceWithoutJustification).Should().Contain(d => d.Id == "HTTPCLIENT018",
            "未声明豁免时必须照常告警（安全告警不得默认静音）");
    }

    // ── ② 声明式开关 ────────────────────────────────────────────────────

    [Fact]
    public void WithMsBuildProperty_ShouldNotReportHTTPCLIENT018()
    {
        var options = GlobalOptionsWith(("build_property.MudHttpSuppressTokenManagerKeyInference", "true"));

        RunAnalyzer(SourceWithoutJustification, globalOptions: options)
            .Should().NotContain(d => d.Id == "HTTPCLIENT018");
    }

    [Fact]
    public void WithGlobalAnalyzerConfigKey_ShouldNotReportHTTPCLIENT018()
    {
        var options = GlobalOptionsWith(("mud_suppress_token_manager_key_inference", "allow"));

        RunAnalyzer(SourceWithoutJustification, globalOptions: options)
            .Should().NotContain(d => d.Id == "HTTPCLIENT018");
    }

    // ── ②′ 目录级 .editorconfig 键（树粒度，仅分析器路径可达） ──────────

    [Fact]
    public void WithTreeLevelEditorConfigKey_ShouldNotReportHTTPCLIENT018()
    {
        // 目录级键只出现在语法树粒度配置中（GlobalOptions 恒为空）——
        // 该用例在生成器路径下不可能成立（树键不进缓存输入），是迁移分析器的核心收益。
        var treeOptions = OptionsWith(("mud_suppress_token_manager_key_inference", "true"));

        RunAnalyzer(SourceWithoutJustification, treeOptions: treeOptions)
            .Should().NotContain(d => d.Id == "HTTPCLIENT018");
    }

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("")]
    public void WithNonTrueLikeValue_ShouldStillReportHTTPCLIENT018(string value)
    {
        var options = GlobalOptionsWith(("build_property.MudHttpSuppressTokenManagerKeyInference", value));

        RunAnalyzer(SourceWithoutJustification, globalOptions: options)
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
        RunAnalyzer(SourceWithJustification).Should().NotContain(d => d.Id == "HTTPCLIENT018");
        RunAnalyzer(SourceWithEmptyJustification).Should().NotContain(d => d.Id == "HTTPCLIENT018");
    }

    // ── ④ 已显式指定 TokenManagerKey（既有路径，防回归） ───────────────

    [Fact]
    public void WithExplicitTokenManagerKey_ShouldNotReportHTTPCLIENT018()
    {
        RunAnalyzer(SourceWithExplicitKey).Should().NotContain(d => d.Id == "HTTPCLIENT018");
    }

    // ── ⑤ 未配置 TokenManage 的接口不报 ────────────────────────────────

    [Fact]
    public void WithoutTokenManage_ShouldNotReportHTTPCLIENT018()
    {
        RunAnalyzer(SourceWithoutTokenManage).Should().NotContain(d => d.Id == "HTTPCLIENT018",
            "推断值仅对配置了令牌管理器的接口有意义");
    }

    // ---- 测试基建 ----

    private static ImmutableArray<Diagnostic> RunAnalyzer(
        string source,
        AnalyzerConfigOptions? globalOptions = null,
        AnalyzerConfigOptions? treeOptions = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new TokenManagerKeyInferenceAnalyzer()),
            new AnalyzerOptions(
                ImmutableArray<AdditionalText>.Empty,
                new TestAnalyzerConfigOptionsProvider(globalOptions, treeOptions, syntaxTree)));

        return compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static AnalyzerConfigOptions GlobalOptionsWith(params (string Key, string Value)[] settings)
        => new TestAnalyzerConfigOptions(ToMap(settings));

    private static AnalyzerConfigOptions OptionsWith(params (string Key, string Value)[] settings)
        => new TestAnalyzerConfigOptions(ToMap(settings));

    private static Dictionary<string, string> ToMap((string Key, string Value)[] settings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in settings)
            map[key] = value;
        return map;
    }

    /// <summary>
    /// 按「全局 / 语法树」两个粒度分别供电的假配置提供器 —— 树粒度供电是目录级
    /// <c>.editorconfig</c> 豁免用例的前提（旧实现的 <c>GetOptions(tree)</c> 恒返回全局值，
    /// 恰好会把树粒度失效掩盖掉）。
    /// </summary>
    private sealed class TestAnalyzerConfigOptionsProvider(
        AnalyzerConfigOptions? globalOptions,
        AnalyzerConfigOptions? treeOptions,
        SyntaxTree tree) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; }
            = globalOptions ?? new TestAnalyzerConfigOptions(new Dictionary<string, string>());

        public override AnalyzerConfigOptions GetOptions(SyntaxTree syntaxTree)
            => ReferenceEquals(syntaxTree, tree) && treeOptions != null
                ? treeOptions
                : new TestAnalyzerConfigOptions(new Dictionary<string, string>());

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class TestAnalyzerConfigOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
