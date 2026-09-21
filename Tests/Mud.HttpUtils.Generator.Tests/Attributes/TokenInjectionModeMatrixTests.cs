// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-03：令牌注入模式矩阵（7 种模式 × 2 个声明级 = 14 组）。
/// </summary>
/// <remarks>
/// <para>
/// <b>背景（G8-01）</b>：令牌注入的「模式判定」与「实际发射」此前使用**不同层级**的字段
/// —— <c>ShouldInjectToken</c>/<c>GenerateTokenInjection</c> 用「有效级」（方法级 &gt; 接口级），
/// 而 <c>ShouldGenerateTokenQuery</c>/<c>BuildUrlWithPlaceholders</c>/<c>GetTokenHeaderName</c>
/// 只用「接口级」。后果：方法级 <c>[Token(InjectionMode = Query)]</c> 取到 <c>access_token</c> 后
/// <b>无处注入而被静默丢弃</b>（请求无令牌）；方法级 <c>Header</c> 的自定义头名被落回 <c>Authorization</c>。
/// </para>
/// <para>
/// <b>断言 A（产物形状）</b>：每种模式必须发射其**特征注入语句**。
/// </para>
/// <para>
/// <b>断言 B（无静默丢弃 · 根因级）</b>：生成代码中 <c>access_token</c> 必须出现在
/// 「取令牌声明语句」<b>之外</b>的至少一处 —— 这正是能拦住 G8-01 这类缺陷的断言：
/// 修复前 Query/Path 的产物里 <c>access_token</c> 只出现在 <c>GetTokenAsync</c> 声明中。
/// </para>
/// </remarks>
public class TokenInjectionModeMatrixTests
{
    /// <summary>单条矩阵用例：模式名 + <c>[Token]</c> 命名参数 + 期望的特征注入片段 + 是否需要取令牌值。</summary>
    private sealed record ModeCase(string Mode, string TokenArgs, string ExpectedInjection, bool DeclaresAccessToken = true);

    private static readonly ModeCase[] Cases =
    [
        new("Header", "Name = \"Authorization\", Scheme = \"Bearer\"",
            "__httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("),
        new("Query", "Name = \"access_token\"",
            "__queryParams.Add(\"access_token\", access_token)"),
        new("Path", "Name = \"access_token\"",
            "System.Uri.EscapeDataString(access_token)"),
        new("ApiKey", "Name = \"X-Api-Key\"",
            "__httpRequest.Headers.Add(\"X-Api-Key\", access_token)"),
        // HmacSignature 不取令牌值（签名在请求组装完成后统一施加），故无「取后丢弃」风险。
        new("HmacSignature", "Name = \"access_token\"",
            "ApplyHmacSignatureAsync(__httpRequest)", DeclaresAccessToken: false),
        new("BasicAuth", "Name = \"Authorization\"",
            "__httpRequest.Headers.Add(\"Authorization\", $\"Basic {__basicCredentials}\")"),
        new("Cookie", "Name = \"session\"",
            "__httpRequest.Headers.Add(\"Cookie\", \"session=\" + System.Uri.EscapeDataString(access_token))"),
    ];

    public static TheoryData<string, bool> Matrix()
    {
        var data = new TheoryData<string, bool>();
        foreach (var c in Cases)
        {
            data.Add(c.Mode, true);   // 接口级声明
            data.Add(c.Mode, false);  // 方法级声明
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void TokenInjection_EveryModeAndLevel_InjectsToken(string mode, bool onInterface)
    {
        var c = Cases.First(x => x.Mode == mode);
        var level = onInterface ? "接口级" : "方法级";

        // Path 模式的令牌占位符必须出现在 URL 模板中（Token 名即占位符名）。
        var url = mode == "Path" ? "/token/{access_token}" : "/data";
        var tokenAttr = $"[Token(TokenType = \"AccessToken\", InjectionMode = TokenInjectionMode.{mode}, {c.TokenArgs})]";

        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                {{(onInterface ? tokenAttr : string.Empty)}}
                public interface ITestApi
                {
                    {{(onInterface ? string.Empty : tokenAttr)}}
                    [Get("{{url}}")]
                    Task<string> GetDataAsync();
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: $"{level} {mode} 注入模式的接口");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        generated.Should().Contain(c.ExpectedInjection,
            $"{level} {mode} 模式必须发射其特征注入语句（G8-01：模式判定与发射必须同为有效级）");

        AssertTokenIsNotDiscarded(generated, c, level);
    }

    /// <summary>
    /// 断言 B（根因级）：<c>access_token</c> 不得只出现在「取令牌声明语句」中。
    /// </summary>
    private static void AssertTokenIsNotDiscarded(string generated, ModeCase c, string level)
    {
        if (!c.DeclaresAccessToken)
            return;

        var injectionLines = generated
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains("access_token", StringComparison.Ordinal))
            .Where(l => !l.StartsWith("var access_token = await GetTokenAsync(", StringComparison.Ordinal))
            .Where(l => !l.StartsWith("var access_token = await GetApiKeyAsync(", StringComparison.Ordinal))
            .Where(l => !l.StartsWith("var access_token = !string.IsNullOrWhiteSpace(", StringComparison.Ordinal))
            .ToList();

        injectionLines.Should().NotBeEmpty(
            $"{level} {c.Mode} 模式下 access_token 必须出现在注入语句中；" +
            "仅出现在 GetTokenAsync 声明中意味着令牌被取出后丢弃（G8-01 的核心缺陷形态）");
    }

    /// <summary>
    /// G8-01（第 4 处修复点）：方法级 <c>[Token(InjectionMode = Header, Name = "X-Custom")]</c> 必须
    /// 使用自定义头名，而不是被落回 <c>Authorization</c> 兜底。
    /// </summary>
    /// <remarks>
    /// 既有 Header 快照恰好使用 <c>Name = "Authorization"</c>（与兜底值同名），掩盖了该缺陷；
    /// 此处以文本断言固定正确形态（含恢复上下文中的 <c>HeaderName</c>）。
    /// </remarks>
    [Fact]
    public void MethodLevelCustomHeaderName_IsHonored()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                public interface ITestApi
                {
                    [Get("/data")]
                    [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Header, Name = "X-Custom")]
                    Task<string> GetDataAsync();
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "方法级 Header 自定义头名");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        generated.Should().Contain("__httpRequest.Headers.Add(\"X-Custom\", access_token)",
            "方法级 [Token(Name = …)] 必须生效（修复前门控用接口级模式，头名被落回 Authorization）");
        generated.Should().NotContain("Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(",
            "自定义头名不得走 Authorization 专用注入分支");
        generated.Should().Contain("HeaderName = \"X-Custom\"",
            "TokenRecoveryContext 必须携带同一头名，否则 401 恢复会写错头");
    }
}
