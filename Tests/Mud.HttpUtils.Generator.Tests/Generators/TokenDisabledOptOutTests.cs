using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// F-1（G-1）：<c>[Token]</c> 方法级免令牌 opt-out（<c>Disabled</c>）的生成器侧回归护栏。
/// </summary>
/// <remarks>
/// 按评审修订（2026-10-13，架构师 + QA）的验收口径做<b>双向断言</b> —— 同一接口内
/// 「常规方法断言生成 <c>GetTokenAsync</c> 调用 + 逃生方法断言<b>零</b>令牌代码」，
/// 防「只测豁免不测未豁免」的假阳性。覆盖：
/// <list type="bullet">
///   <item>方法级 <c>[Token(Disabled = true)]</c> 阻断接口级 <c>[Token]</c> 继承（下游 clear_quota 拆接口 workaround 可合并回单接口）；</item>
///   <item>接口级 <c>[Token(Disabled = true)]</c> 对全部方法生效；方法级 <c>[Token(Disabled = false)]</c> 可重新启用（三态回退）；</item>
///   <item>HTTPCLIENT039：免令牌方法与注入参数同用 → 警告且参数被忽略（不写入生成代码）；</item>
///   <item>HTTPCLIENT040：参数级 <c>[Token(Disabled = true)]</c> → 警告并忽略（参数仍按令牌参数语义处理）；</item>
///   <item>Query/Path 注入模式下的零令牌代码（防悬空 <c>access_token</c> 引用）；</item>
///   <item>免令牌方法不建 <c>TokenRecoveryContext</c>；</item>
///   <item>生成代码整体可编译（含 DEBUG 语法校验同源的编译断言）。</item>
/// </list>
/// </remarks>
public class TokenDisabledOptOutTests
{
    // ───────────────────────── 测试基建 ─────────────────────────

    /// <summary>
    /// 构造 TokenManage 模式测试源：<c>ITestTokenManager</c> 桩 + <c>[HttpClientApi(TokenManage)]</c> 接口
    /// （令牌注入仅在 TokenManage 模式发射，与 <c>GeneratorSnapshotTests</c> 的 TokenManager 样本同构）。
    /// </summary>
    /// <param name="interfaceBody">接口成员声明（含缩进）。</param>
    /// <param name="interfaceTokenAttribute">接口级 [Token] 特性文本（默认 <c>[Token]</c>；传空串表示不标注）。</param>
    private static string BuildSource(string interfaceBody, string interfaceTokenAttribute = "[Token]")
        => $$"""
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                {{interfaceTokenAttribute}}
                public interface ITestApi
                {
            {{interfaceBody}}
                }
            }
            """;

    private static (ImmutableArray<Diagnostic> Diagnostics, Compilation OutputCompilation) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (diagnostics, outputCompilation);
    }

    private static string GetGeneratedCode(Compilation outputCompilation)
    {
        return outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString()
            ?? throw new InvalidOperationException("未生成实现类源码。");
    }

    /// <summary>
    /// 按方法名截取生成方法体（自签名处起、至生成器统一的 8 空格方法收尾大括号止），
    /// 使「零令牌代码」断言精确到方法粒度而非整份生成文本。
    /// </summary>
    private static string ExtractMethodBody(string generatedCode, string methodName)
    {
        var marker = $" {methodName}(";
        var start = generatedCode.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"生成代码应包含方法 {methodName} 的实现");

        var end = generatedCode.IndexOf("\n        }", start, StringComparison.Ordinal);
        end.Should().BeGreaterThanOrEqualTo(0, $"生成代码中方法 {methodName} 应有完整方法体");

        return generatedCode.Substring(start, end - start);
    }

    /// <summary>断言方法体为「零令牌代码」：不取令牌、不注入、不参与令牌恢复。</summary>
    private static void AssertZeroTokenCode(string methodBody, string methodName)
    {
        methodBody.Should().NotContain("GetTokenAsync(", $"{methodName} 是免令牌方法，不得取令牌");
        methodBody.Should().NotContain("GetApiKeyAsync(", $"{methodName} 是免令牌方法，不得取 API Key");
        methodBody.Should().NotContain("access_token", $"{methodName} 是免令牌方法，不得引用令牌变量");
        methodBody.Should().NotContain("__basicCredentials", $"{methodName} 是免令牌方法，不得生成 BasicAuth 凭据");
        methodBody.Should().NotContain("TokenRecoveryContext", $"{methodName} 是免令牌方法，不得参与令牌恢复");
        methodBody.Should().NotContain("ApplyHmacSignatureAsync", $"{methodName} 是免令牌方法，不得生成令牌签名");
    }

    // ───────────────────────── ① 方法级 Disabled 阻断接口级 [Token] 继承（双向断言） ─────────────────────────

    /// <summary>
    /// 验收场景复刻（下游 clear_quota）：同一接口内「常规端点取令牌 + 逃生端点免令牌」——
    /// 常规方法必须生成 <c>GetTokenAsync</c> 调用与注入，逃生方法必须<b>零</b>令牌代码。
    /// </summary>
    [Fact]
    public void MethodLevelDisabled_ShouldBlockInterfaceTokenInheritance_Bidirectional()
    {
        var source = BuildSource("""
                [Get("/cgi-bin/token")]
                Task<string> FetchDataAsync();

                [Get("/cgi-bin/clear_quota/v2")]
                [Token(Disabled = true)]
                Task<string> ClearQuotaAsync();
            """);

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        // 正向：常规方法继承接口级 [Token]，必须取令牌并注入
        var normalBody = ExtractMethodBody(generatedCode, "FetchDataAsync");
        normalBody.Should().Contain("await GetTokenAsync(", "常规方法必须生成 GetTokenAsync 调用");
        normalBody.Should().Contain("access_token", "常规方法必须注入令牌");

        // 反向：逃生方法显式 opt-out，零令牌代码
        AssertZeroTokenCode(ExtractMethodBody(generatedCode, "ClearQuotaAsync"), "ClearQuotaAsync");

        // 常规配置不产生免令牌误报
        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT039");
        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT040");
    }

    // ───────────────────────── ② 接口级 Disabled 继承 + 方法级重新启用（三态回退） ─────────────────────────

    /// <summary>接口级 <c>[Token(Disabled = true)]</c> 对全部方法生效（继承阻断的接口级形态）。</summary>
    [Fact]
    public void InterfaceLevelDisabled_ShouldApplyToAllMethods()
    {
        var source = BuildSource("""
                [Get("/a")]
                Task<string> AAsync();

                [Get("/b")]
                Task<string> BAsync();
            """, interfaceTokenAttribute: "[Token(Disabled = true)]");

        var generatedCode = GetGeneratedCode(RunGenerator(source).OutputCompilation);

        AssertZeroTokenCode(ExtractMethodBody(generatedCode, "AAsync"), "AAsync");
        AssertZeroTokenCode(ExtractMethodBody(generatedCode, "BAsync"), "BAsync");
    }

    /// <summary>
    /// 三态回退语义（「方法级覆盖接口级」MT-21）：接口级 Disabled = true 时，
    /// 方法级 <c>[Token(Disabled = false)]</c> 可重新启用令牌（双向断言）。
    /// </summary>
    [Fact]
    public void MethodLevelDisabledFalse_ShouldReEnableToken_WhenInterfaceDisabled()
    {
        var source = BuildSource("""
                [Get("/a")]
                [Token(Disabled = false)]
                Task<string> AAsync();

                [Get("/b")]
                Task<string> BAsync();
            """, interfaceTokenAttribute: "[Token(Disabled = true)]");

        var generatedCode = GetGeneratedCode(RunGenerator(source).OutputCompilation);

        ExtractMethodBody(generatedCode, "AAsync").Should().Contain("await GetTokenAsync(",
            "方法级 [Token(Disabled = false)] 显式覆盖接口级 Disabled = true，应重新启用令牌");
        AssertZeroTokenCode(ExtractMethodBody(generatedCode, "BAsync"), "BAsync");
    }

    // ───────────────────────── ③ HTTPCLIENT039：Disabled 与注入参数同用 → 警告且参数被忽略 ─────────────────────────

    /// <summary>方法级 <c>[Token(Disabled = true, 注入参数…)]</c> → HTTPCLIENT039（Warning），注入参数不写入生成代码。</summary>
    [Fact]
    public void DisabledWithInjectionParameters_ShouldReportHTTPCLIENT039_AndIgnoreThem()
    {
        var source = BuildSource("""
                [Get("/x")]
                [Token(Disabled = true, InjectionMode = TokenInjectionMode.Query, Name = "t", Scopes = "s")]
                Task<string> XAsync();
            """);

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        var d = diagnostics.Where(x => x.Id == "HTTPCLIENT039").ToList();
        d.Should().ContainSingle("免令牌方法与注入参数同用必须报 HTTPCLIENT039（参数被忽略）");
        d[0].Severity.Should().Be(DiagnosticSeverity.Warning);
        d[0].GetMessage().Should().Contain("InjectionMode", "消息应列出被忽略的注入参数");
        d[0].GetMessage().Should().Contain("Name");
        d[0].GetMessage().Should().Contain("Scopes");

        AssertZeroTokenCode(ExtractMethodBody(generatedCode, "XAsync"), "XAsync");
    }

    /// <summary>
    /// 反向：仅 <c>[Token(Disabled = true)]</c>（可与 Justification 共用）不构成「注入参数同用」，不报 HTTPCLIENT039。
    /// </summary>
    [Fact]
    public void DisabledWithoutInjectionParameters_ShouldNotReportHTTPCLIENT039()
    {
        var source = BuildSource("""
                [Get("/x")]
                [Token(Disabled = true)]
                Task<string> XAsync();

                [Get("/y")]
                [Token(Disabled = true, Justification = "免令牌应急端点")]
                Task<string> YAsync();
            """);

        var (diagnostics, _) = RunGenerator(source);

        diagnostics.Should().NotContain(x => x.Id == "HTTPCLIENT039",
            "Justification 不是注入参数，不构成「注入参数同用」");

        // 单独核对位置构造参数场景：[Token("UserAccessToken", Disabled = true)] 的 TokenType 属注入语义
        var sourceWithTokenType = BuildSource("""
                [Get("/z")]
                [Token("UserAccessToken", Disabled = true)]
                Task<string> ZAsync();
            """);
        RunGenerator(sourceWithTokenType).Diagnostics.Should().Contain(x => x.Id == "HTTPCLIENT039",
            "位置构造参数即显式 TokenType，属被忽略的注入语义，应报 HTTPCLIENT039");
    }

    /// <summary>反向：注入参数同用但未 Disabled（有效级 false）不报 HTTPCLIENT039。</summary>
    [Fact]
    public void InjectionParametersWithoutDisabled_ShouldNotReportHTTPCLIENT039()
    {
        var source = BuildSource("""
                [Get("/x")]
                [Token(InjectionMode = TokenInjectionMode.Query, Name = "t")]
                Task<string> XAsync();
            """);

        RunGenerator(source).Diagnostics.Should().NotContain(x => x.Id == "HTTPCLIENT039");
    }

    // ───────────────────────── ④ HTTPCLIENT040：参数级 Disabled → 警告并忽略 ─────────────────────────

    /// <summary>
    /// 参数级 <c>[Token(Disabled = true)]</c> 无意义：报 HTTPCLIENT040（Warning）且设置被忽略 ——
    /// 参数仍按「令牌参数」语义处理（优先用参数值，空时回退 GetTokenAsync）。
    /// </summary>
    [Fact]
    public void ParameterLevelDisabled_ShouldReportHTTPCLIENT040_AndKeepTokenParameterSemantics()
    {
        var source = BuildSource("""
                [Post("/t")]
                [Token]
                Task<string> PostAsync([Token(Disabled = true)] string token);
            """);

        var (diagnostics, outputCompilation) = RunGenerator(source);
        var generatedCode = GetGeneratedCode(outputCompilation);

        var d = diagnostics.Where(x => x.Id == "HTTPCLIENT040").ToList();
        d.Should().ContainSingle("参数级 [Token(Disabled = true)] 必须报 HTTPCLIENT040");
        d[0].Severity.Should().Be(DiagnosticSeverity.Warning);
        d[0].GetMessage().Should().Contain("token", "消息应点名参数");

        // 「忽略」断言：Disabled 不生效，参数仍按令牌参数语义生成（优先参数值 + GetTokenAsync 回退）
        var body = ExtractMethodBody(generatedCode, "PostAsync");
        body.Should().Contain("!string.IsNullOrWhiteSpace(token)", "参数级 Disabled 被忽略，参数仍为令牌参数");
        body.Should().Contain("await GetTokenAsync(", "参数值为空时仍回退取令牌");
    }

    /// <summary>反向：参数级 <c>[Token]</c> 未设 Disabled 不报 HTTPCLIENT040。</summary>
    [Fact]
    public void ParameterLevelTokenWithoutDisabled_ShouldNotReportHTTPCLIENT040()
    {
        var source = BuildSource("""
                [Post("/t")]
                [Token]
                Task<string> PostAsync([Token] string token);
            """);

        RunGenerator(source).Diagnostics.Should().NotContain(x => x.Id == "HTTPCLIENT040");
    }

    // ───────────────────────── ⑤ Query / Path 注入模式的零令牌代码（防悬空 access_token） ─────────────────────────

    /// <summary>
    /// Query 模式 + Disabled：<c>ShouldGenerateTokenQuery</c> 必须短路，
    /// 否则生成 <c>__queryParams.Add("…", access_token)</c> 引用未声明变量（CS0103）。
    /// </summary>
    [Fact]
    public void DisabledWithQueryInjectionMode_ShouldNotEmitTokenQuery()
    {
        var source = BuildSource("""
                [Get("/a")]
                [Token(InjectionMode = TokenInjectionMode.Query)]
                Task<string> AAsync();

                [Get("/b")]
                [Token(Disabled = true, InjectionMode = TokenInjectionMode.Query)]
                Task<string> BAsync();
            """);

        var generatedCode = GetGeneratedCode(RunGenerator(source).OutputCompilation);

        ExtractMethodBody(generatedCode, "AAsync").Should().Contain("access_token",
            "Query 模式常规方法必须把令牌写入查询参数（双向断言的正向面）");
        AssertZeroTokenCode(ExtractMethodBody(generatedCode, "BAsync"), "BAsync");
    }

    /// <summary>
    /// Path 模式 + Disabled：URL 中的令牌占位符不得替换为 <c>access_token</c>
    ///（免令牌方法无令牌变量，替换即悬空引用）。
    /// </summary>
    [Fact]
    public void DisabledWithPathInjectionMode_ShouldNotReplaceTokenPlaceholder()
    {
        var source = BuildSource("""
                [Get("/api/{token}/data")]
                [Token(Disabled = true, InjectionMode = TokenInjectionMode.Path, Name = "token")]
                Task<string> GetAsync();
            """);

        var generatedCode = GetGeneratedCode(RunGenerator(source).OutputCompilation);

        var body = ExtractMethodBody(generatedCode, "GetAsync");
        AssertZeroTokenCode(body, "GetAsync");
        body.Should().NotContain("EscapeDataString(access_token)", "Path 令牌占位符不得替换为 access_token");
    }

    // ───────────────────────── ⑥ 令牌恢复上下文 ─────────────────────────

    /// <summary>
    /// 免令牌方法不建 <c>TokenRecoveryContext</c>（不参与令牌恢复），
    /// 而同接口配置了 TokenManagerKey 的常规方法仍生成恢复上下文（双向断言）。
    /// </summary>
    [Fact]
    public void DisabledMethod_ShouldNotGenerateTokenRecoveryContext()
    {
        var source = BuildSource("""
                [Get("/a")]
                [Token(TokenManagerKey = "sharedKey")]
                Task<string> AAsync();

                [Get("/b")]
                [Token(Disabled = true)]
                Task<string> BAsync();
            """);

        var generatedCode = GetGeneratedCode(RunGenerator(source).OutputCompilation);

        ExtractMethodBody(generatedCode, "AAsync").Should().Contain("TokenRecoveryContext",
            "显式 TokenManagerKey 的常规方法应生成恢复上下文（双向断言的正向面）");
        AssertZeroTokenCode(ExtractMethodBody(generatedCode, "BAsync"), "BAsync");
    }

    // ───────────────────────── ⑦ 生成代码编译校验（含 DEBUG 语法校验同源断言） ─────────────────────────

    /// <summary>
    /// 「输入 + 生成产物」整体无 Error 且无 CS0219：免令牌场景生成代码必须可编译
    /// （拦截 Query/Path 短路缺失导致的悬空 <c>access_token</c> 引用等坏代码）。
    /// </summary>
    [Fact]
    public void DisabledMixedWithNormalMethods_GeneratedCodeShouldCompile()
    {
        var source = BuildSource("""
                [Get("/cgi-bin/token")]
                Task<string> FetchDataAsync();

                [Post("/cgi-bin/clear_quota/v2")]
                [Token(Disabled = true, InjectionMode = TokenInjectionMode.Query)]
                Task<string> ClearQuotaAsync();

                [Get("/api/{token}/data")]
                [Token(Disabled = true, InjectionMode = TokenInjectionMode.Path, Name = "token")]
                Task<string> PathAsync();
            """);

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(source, description: "F-1 免令牌混布场景生成代码");
        var generatedCode = output.SyntaxTrees.Skip(1).First().ToString();

        AssertZeroTokenCode(ExtractMethodBody(generatedCode, "ClearQuotaAsync"), "ClearQuotaAsync");
        AssertZeroTokenCode(ExtractMethodBody(generatedCode, "PathAsync"), "PathAsync");
        ExtractMethodBody(generatedCode, "FetchDataAsync").Should().Contain("await GetTokenAsync(",
            "常规方法不受免令牌影响（双向断言）");
    }
}
