namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// [Phase2 修复 1.8 / §3.7] 生成源码转义回归守卫（动态口径）。
/// </summary>
/// <remarks>
/// <para>
/// 生成器把 <c>[Token]</c> 配置（TokenManagerKey / Scopes / Name）以裸插值写入字符串字面量。
/// 历史缺陷：<c>MethodGenerator</c> 的 Scopes / ApiKey / TokenManagerKey 三处未调用
/// <c>StringEscapeHelper.EscapeString</c>，配置含 <c>"</c> 或 <c>\</c> 时会产出不可编译的 C#。
/// </para>
/// <para>
/// 采用<strong>动态</strong>口径（方案 A.5 修订 4）：直接编译含特殊字符配置的接口，
/// 断言「输入 + 生成产物」整体无 Error 级诊断。相比静态扫描生成器源码，
/// 动态口径不会误伤 <c>string.Join("{{ … }}")</c> 这类合法花括号转义，且对未来的新插值点同样有效。
/// </para>
/// </remarks>
public class GeneratedCodeEscapeTests
{
    /// <summary>
    /// TokenManagerKey 含双引号 → 生成代码必须转义为 <c>\"</c> 而不是提前闭合字符串字面量。
    /// </summary>
    [Fact]
    public void TokenManagerKeyWithQuotes_GeneratesCompilableCode()
    {
        const string source = """
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
                [Token(TokenManagerKey = "key\"with\"quotes", Scopes = "read\"only,write")]
                public interface ITokenApi
                {
                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "TokenManagerKey/Scopes 含双引号时生成代码必须可编译（Phase2 1.8）");
    }

    /// <summary>
    /// TokenManagerKey 含反斜杠 → 生成代码必须转义为 <c>\\</c>。
    /// </summary>
    [Fact]
    public void TokenManagerKeyWithBackslash_GeneratesCompilableCode()
    {
        const string source = """
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
                [Token(TokenManagerKey = "domain\\key", Scopes = "a\\b")]
                public interface ITokenApi
                {
                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "TokenManagerKey/Scopes 含反斜杠时生成代码必须可编译（Phase2 1.8）");
    }

    /// <summary>
    /// ApiKey 注入模式下的令牌名含双引号（<c>InterfaceTokenName</c> → <c>GetApiKeyAsync("…")</c>）。
    /// </summary>
    [Fact]
    public void ApiKeyTokenNameWithQuotes_GeneratesCompilableCode()
    {
        const string source = """
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
                [Token(Name = "X-Api\"Key", InjectionMode = TokenInjectionMode.ApiKey)]
                public interface IApiKeyApi
                {
                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "ApiKey 模式令牌名含双引号时生成代码必须可编译（Phase2 1.8）");
    }

    /// <summary>
    /// [GEN-09] 方法级 <c>[Token(Name = "…")]</c> 的 Name 含双引号 → 生成代码必须转义为
    /// <c>\"</c>，并作为 ApiKey 名在两处（注入 + 恢复上下文）正确落地。
    /// </summary>
    [Fact]
    public void MethodLevelTokenNameWithQuotes_GeneratesCompilableCode()
    {
        const string source = """
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
                [Token(Name = "X-Interface-Token", InjectionMode = TokenInjectionMode.ApiKey)]
                public interface IApiKeyApi
                {
                    [Get("/data")]
                    [Token(Name = "X-Method\"Key", InjectionMode = TokenInjectionMode.ApiKey)]
                    Task<string> GetAsync();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "方法级 ApiKey 令牌名含双引号时生成代码必须可编译（GEN-09）");
    }

    /// <summary>
    /// [GEN-18][§8.6] 含 [Header] 参数时，生成代码必须发射运行期 CR/LF 校验守卫
    /// （net4x/netstandard2.0 编译的 HttpClient 不校验头值，存在头部注入风险）。
    /// </summary>
    [Fact]
    public void HeaderValue_WithCrLf_Throws()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IHeaderApi
                {
                    [Get("/data")]
                    Task<string> GetAsync([Header("X-Token")] string token);
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "含 [Header] 参数时生成代码必须可编译（GEN-18）");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generated.Should().Contain("HttpHeaderValueValidator.IsValid(token)",
            "生成代码必须发射运行期 CR/LF 校验守卫（GEN-18）");
        generated.Should().Contain("ArgumentException",
            "守卫失败时应抛出带参数名提示的 ArgumentException（GEN-18）");
    }

    /// <summary>
    /// [GEN-01] CacheKeyTemplate 含花括号（含未配对的 <c>{bad</c>）→ 必须转义为 <c>{{…}}</c>，
    /// 否则 <c>{bad</c> 会成为插值孔导致生成的 CacheKey 插值串不可编译（CS1733/CS1006）。
    /// </summary>
    [Fact]
    public void CacheKeyTemplate_WithBraces_Compiles()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ICacheApi
                {
                    [Get("/products")]
                    [Cache(60, CacheKeyTemplate = "/u/{id}?{bad", UseSlidingExpiration = true)]
                    Task<string> GetAsync([Path] int id);
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "CacheKeyTemplate 含花括号（含未配对 {bad）时必须转义，生成的 CacheKey 插值串才可编译（GEN-01）");
    }
}
