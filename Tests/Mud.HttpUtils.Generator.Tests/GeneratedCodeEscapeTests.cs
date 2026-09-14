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
}
