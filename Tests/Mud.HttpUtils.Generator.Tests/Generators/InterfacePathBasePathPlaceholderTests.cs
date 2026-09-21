// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-18：<c>HTTPCLIENT013</c> 的「多余占位符」判定不得把**接口级 Path 来源**算在内。
/// </summary>
/// <remarks>
/// <para>
/// <b>背景（构建红基线）</b>：接口级 <c>[Path("tenantId")]</c> 属性 / <c>[InterfacePath]</c> 的典型用法（也是
/// <c>Demos/HttpClientApiDemo.Share/NewFeatureTests/BasePathTestApi.cs:67-94</c> 的用法）是
/// 为接口级 <c>[BasePath("{tenantId}/api/v1")]</c> 提供占位符值，而 <c>HTTPCLIENT013</c> 只比对**方法级</b>
/// <c>UrlTemplate</c>（不含 BasePath）。
/// </para>
/// <para>
/// FIX-05 将接口级来源并入 <c>pathParams</c>（同时供「缺失」与「多余」两个判定使用），导致
/// 合法用法被判「方法参数中的 [Path] 参数 tenantId 在 URL 模板中找不到对应的占位符」并报 Error，
/// 阻断构建（实测 24 个 error：6 个 Demo 用例 × 4 TFM）。
/// </para>
/// <para>
/// <b>防止「修成恒真」</b>：本文件同时提供反例 —— 方法参数上的 <c>[Path] ghost</c> 确实不在模板中时，
/// <c>HTTPCLIENT013</c> 仍须命中（证明只做了「集合职责分离」，未削弱校验）。
/// </para>
/// </remarks>
public class InterfacePathBasePathPlaceholderTests
{
    private const string Usings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;
        """;

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "InterfacePathBasePathPlaceholderTests",
            new[] { CSharpSyntaxTree.ParseText(source) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (output, diagnostics);
    }

    /// <summary>
    /// 正例：接口 <c>[Path]</c> 属性为 <c>[BasePath]</c> 占位符提供值 ⇒ 不得报 <c>HTTPCLIENT013</c>。
    /// </summary>
    [Fact]
    public void InterfacePathProperty_ServingBasePathPlaceholder_ReportsNoDiagnostic()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                public class UserInfo
                {
                    public int Id { get; set; }
                }

                [HttpClientApi]
                [BasePath("{tenantId}/api/v1")]
                public interface ITenantBasePathApi
                {
                    [Path("tenantId")]
                    string TenantId { get; set; }

                    [Get("users/{id}")]
                    Task<UserInfo> GetUserAsync([Path] int id, CancellationToken cancellationToken = default);
                }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT013",
            "接口级 [Path] 属性服务于 [BasePath] 占位符，不是「方法参数多余占位符」（G8-18）");
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    /// <summary>
    /// 正例（G8-01 第 3 处）：<b>方法级</b> <c>[Token(InjectionMode = Path, Name = …)]</c> 的令牌占位符
    /// 必须被识别为「由令牌机制替换」，不得误报 <c>HTTPCLIENT013</c>。
    /// </summary>
    [Fact]
    public void MethodLevelPathTokenPlaceholder_ReportsNoDiagnostic()
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
                public interface IPathTokenApi
                {
                    [Get("/token/{access_token}")]
                    [Token(TokenType = "AccessToken", InjectionMode = TokenInjectionMode.Path, Name = "access_token")]
                    Task<string> GetDataAsync();
                }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT013",
            "方法级 Path 令牌占位符由令牌机制替换，不应被当作缺失的 [Path] 参数");
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    /// <summary>
    /// 反例：<b>方法参数</b>上的 <c>[Path]</c> 名不在模板中时，<c>HTTPCLIENT013</c> 仍须命中
    /// （证明 G8-18 的修复只做了集合职责分离，没有把校验削弱成恒真）。
    /// </summary>
    [Fact]
    public void MethodPathParameter_NotInTemplate_StillReportsDiagnostic()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                public class UserInfo
                {
                    public int Id { get; set; }
                }

                [HttpClientApi]
                public interface IGhostPathApi
                {
                    [Get("users/{id}")]
                    Task<UserInfo> GetUserAsync([Path] int id, [Path] string ghost, CancellationToken cancellationToken = default);
                }
            }
            """;

        var (_, diagnostics) = RunGenerator(source);

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT013",
            "方法参数上的 [Path] 未出现在 URL 模板中是真实用户错误，必须继续报 HTTPCLIENT013");
    }
}
