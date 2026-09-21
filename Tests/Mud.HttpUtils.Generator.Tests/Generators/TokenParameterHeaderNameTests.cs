// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-20：同时标注 <c>[Token]</c> 与 <c>[Header]</c> 的参数，其默认头名必须取「令牌头名」，
/// 而非参数名或注入协议名。
/// </summary>
/// <remarks>
/// <para>
/// <b>背景</b>：<c>HeaderParameterBinder</c> 中有一段「令牌参数的头名」专用回退逻辑，但其实现
/// ①读的是<b>接口级</b> <c>InterfaceTokenName</c>（缺方法级回退，与 G8-01/G8-19 同族）；
/// ②把 <c>Name</c> 当<b>注入协议名</b>比对 —— 只有字面量 <c>"Bearer"</c>/<c>"Basic"</c> 才映射为
/// <c>Authorization</c>，而这两个值在 <c>[Token]</c> 上是 <b>Scheme</b> 的取值，不是 <c>Name</c> 的取值
/// ⇒ 分支实际不可达；③未命中时头名落回<b>参数名</b>（如 <c>token</c>）。
/// </para>
/// <para>
/// <b>真实影响</b>：<c>[Header]</c> 未显式给出头名时（<c>[Token(Name = "X-Trace-Token")][Header] string token</c>），
/// 请求会带上 <c>token: &lt;值&gt;</c> 而不是 <c>X-Trace-Token: &lt;值&gt;</c>；服务端按预期头名取值必然 401。
/// 仓库内既有用法（<c>Demos/**</c> 的 <c>[Token][Header("x-token")]</c>）全部显式传名，因而长期掩盖了该缺陷。
/// </para>
/// <para>
/// <b>修复口径（单一事实源）</b>：与令牌注入路径共用同一解析 ——
/// 「有效级 <c>Name</c>（方法级 &gt; 接口级），仅 <c>Header</c>/<c>ApiKey</c> 模式消费；未声明时回退 <c>Authorization</c>」。
/// </para>
/// </remarks>
public class TokenParameterHeaderNameTests
{
    private const string Usings = """
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;
        """;

    private static string GetGeneratedCode(string source)
    {
        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含 [Token]+[Header] 参数的接口");
        return string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
    }

    /// <summary>
    /// 方法级 <c>[Token(Name = "X-Method-Token")]</c> 覆盖接口级 Name ⇒
    /// 参数绑定必须使用 <c>X-Method-Token</c>（修复前 binder 只读接口级字段 ⇒ 落回 <c>X-Interface-Token</c>）。
    /// </summary>
    /// <remarks>
    /// 注意：参数级 <c>[Token(InjectionMode = …)]</c> <b>不</b>参与「有效级」解析
    /// （有效级只取「方法级 &gt; 接口级」；参数级 <c>[Token]</c> 的语义是「该参数的值即令牌」，
    /// 由 <c>TokenParameterName</c> 承载），故此处用<b>方法级</b> <c>[Token]</c> 声明 Name。
    /// </remarks>
    [Fact]
    public void MethodLevelTokenName_OverridesInterfaceName()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Header, Name = "X-Interface-Token")]
                public interface ITestApi
                {
                    [Get("/data")]
                    [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Header, Name = "X-Method-Token")]
                    Task<string> GetAsync([Token][Header] string token);
                }
            }
            """;

        var generated = GetGeneratedCode(source);

        generated.Should().Contain("__httpRequest.Headers.Add(\"X-Method-Token\", token)",
            "G8-20：令牌参数的默认头名必须取「有效级」Name（方法级 > 接口级）");
        generated.Should().NotContain("__httpRequest.Headers.Add(\"X-Interface-Token\", token)",
            "G8-20：接口级 Name 不得覆盖方法级（修复前 binder 直读接口级字段）");
        generated.Should().NotContain("__httpRequest.Headers.Add(\"token\", token)",
            "G8-20：不得把参数名当作头名（修复前的缺陷形态）");
    }

    /// <summary>
    /// 接口级 <c>[Token(Name = "X-Interface-Token")]</c> + 无名 <c>[Header]</c> ⇒ 同样取令牌头名。
    /// </summary>
    [Fact]
    public void InterfaceLevelTokenName_WithUnnamedHeader_BindsToTokenHeaderName()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Header, Name = "X-Interface-Token")]
                public interface ITestApi
                {
                    [Get("/data")]
                    Task<string> GetAsync([Token][Header] string token);
                }
            }
            """;

        var generated = GetGeneratedCode(source);

        generated.Should().Contain("__httpRequest.Headers.Add(\"X-Interface-Token\", token)",
            "G8-20：接口级 Name 同样生效（与注入路径同源）");
        generated.Should().NotContain("__httpRequest.Headers.Add(\"token\", token)");
    }

    /// <summary>显式头名仍然优先（GEN-05 契约不得回退）。</summary>
    [Fact]
    public void ExplicitHeaderName_StillWins()
    {
        var source = Usings + """

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
                    Task<string> GetAsync(
                        [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Header, Name = "X-Trace-Token")]
                        [Header("x-token")] string token);
                }
            }
            """;

        var generated = GetGeneratedCode(source);

        generated.Should().Contain("__httpRequest.Headers.Add(\"x-token\", token)",
            "显式 [Header(Name)] 必须优先于令牌头名（GEN-05 契约）");
        generated.Should().NotContain("__httpRequest.Headers.Add(\"X-Trace-Token\", token)");
    }

    /// <summary>
    /// 令牌模式未声明 <c>Name</c> 时回退 <c>Authorization</c>（而非参数名）——
    /// Header 模式的默认注入头即 <c>Authorization</c>。
    /// </summary>
    [Fact]
    public void HeaderModeWithoutName_FallsBackToAuthorization()
    {
        var source = Usings + """

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
                    Task<string> GetAsync(
                        [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Header)]
                        [Header] string token);
                }
            }
            """;

        var generated = GetGeneratedCode(source);

        generated.Should().NotContain("__httpRequest.Headers.Add(\"token\", token)",
            "G8-20：未声明 Name 的 Header 模式令牌参数不得把参数名当头名");
        generated.Should().Contain("Authorization",
            "G8-20：Header 模式的默认头名是 Authorization");
    }

    /// <summary>
    /// 非 Header/ApiKey 令牌模式（接口级 Query）+ 无名 <c>[Header]</c> 参数：保持 GEN-05 的参数名回退，
    /// 不因本次修复改变既有语义（避免扩大改动面）。
    /// </summary>
    [Fact]
    public void NonHeaderTokenMode_WithUnnamedHeader_KeepsParameterNameFallback()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Query, Name = "access_token")]
                public interface ITestApi
                {
                    [Get("/data")]
                    Task<string> GetAsync([Token][Header] string token);
                }
            }
            """;

        var generated = GetGeneratedCode(source);

        generated.Should().Contain("__httpRequest.Headers.Add(\"token\", token)",
            "非 Header/ApiKey 令牌模式下，[Header] 无名参数仍按 GEN-05 回退参数名");
        generated.Should().NotContain("__httpRequest.Headers.Add(\"Authorization\", token)",
            "Query 模式不消费 Name 作为头名，也不得强行落到 Authorization");
    }
}
