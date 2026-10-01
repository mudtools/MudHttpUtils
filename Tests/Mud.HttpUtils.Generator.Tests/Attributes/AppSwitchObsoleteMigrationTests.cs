// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// I-26 / BC-27：旧切换入口的移除与残留废弃面的守卫。
/// </summary>
/// <remarks>
/// <para>
/// <b>3.0.0（`BC-27`）</b>移除了 <c>IAppContextSwitcher</c> 的三个旧入口（<c>UseApp</c> / <c>UseDefaultApp</c> /
/// <c>BeginScope(string)</c>），因此"经接口调用旧入口"从 <c>CS0618</c>（警告）升级为<b>编译错误</b>——
/// 这正是本组用例要钉死的语义分水岭。
/// </para>
/// <para>
/// 同时断言迁移目标（<c>IAppScopeSwitcher.UseAppScope</c> / <c>UseDefaultAppScope</c>）<b>干净可用</b>：
/// 这是破坏性变更能够被接受的唯一前提 —— 必须有等价且无噪音的替代面。
/// </para>
/// <para>
/// 注：生成类成员**不**标 <c>[Obsolete]</c>（沿用 `GEN-02`）⇒ 经具体类型调用无 <c>CS0618</c>；
/// 唯一的残留废弃面是 <c>GetTokenAsync</c>（候选 `BC-28` 才迁出）。
/// </para>
/// </remarks>
public class AppSwitchObsoleteMigrationTests
{
    /// <summary>旧入口调用点：三个成员已移除（应为编译错误），<c>GetTokenAsync</c> 仍为警告。</summary>
    private const string LegacyProbeSource = """
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
            public interface IObsoleteProbeApi : IAppContextSwitcher
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }

            public static class LegacyCallSite
            {
                public static void Probe(IAppContextSwitcher api)
                {
                    api.UseApp("app-a");
                    api.UseDefaultApp();
                    using var scope = api.BeginScope("app-a");
                    _ = api.GetTokenAsync();
                }
            }
        }
        """;

    /// <summary>推荐入口调用点：经 <c>IAppScopeSwitcher</c> 调用两个新成员（应零诊断）。</summary>
    private const string RecommendedProbeSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface IRecommendedProbeApi : IAppScopeSwitcher
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }

            public static class RecommendedCallSite
            {
                public static void Probe(IAppScopeSwitcher api)
                {
                    using var scoped = api.UseAppScope("app-a");
                    using var defaultScoped = api.UseDefaultAppScope();
                }
            }
        }
        """;

    /// <summary>`BC-27` 附带根治：`BeginScope(null)` 的重载二义应消失。</summary>
    private const string NullScopeProbeSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface INullScopeProbeApi : IAppScopeSwitcher
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }

            public static class NullScopeCallSite
            {
                public static void Probe(IAppContextHolder holder)
                {
                    using var scope = holder.BeginScope(null);
                }
            }
        }
        """;

    [Fact]
    public void LegacyEntries_AreRemoved_AndReportCompileErrors()
    {
        var errors = RunGenerator(LegacyProbeSource)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        errors.Should().HaveCountGreaterThanOrEqualTo(3,
            "BC-27：三个旧入口经接口调用都必须产生**编译错误**（成员已移除）。实际：\n"
            + string.Join("\n", errors.Select(d => d.ToString())));

        var messages = errors
            .Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        messages.Should().Contain(m => m.Contains("UseApp"),
            "必须明确报出 UseApp 已不存在（否则调用方无从判断该改成什么）");
        messages.Should().Contain(m => m.Contains("UseDefaultApp"));
        messages.Should().Contain(m => m.Contains("IMudAppContext"),
            "BeginScope(\"app-a\") 的形态不同：方法名仍存在（Holder 面重载），因此报的是**参数类型不匹配**"
            + "（无法从 string 转换到 IMudAppContext）而非「成员不存在」—— 这也证明 string 重载确已移除");
    }

    [Fact]
    public void GetTokenAsync_StillReportsCs0618_WithMigrationTarget()
    {
        var cs0618 = RunGenerator(LegacyProbeSource)
            .Where(d => d.Id == "CS0618")
            .ToArray();

        cs0618.Should().HaveCount(1,
            "BC-27 不动 GetTokenAsync（候选 BC-28）⇒ 它应恰好保留一条 CS0618 迁移警告。实际：\n"
            + string.Join("\n", cs0618.Select(d => d.ToString())));

        cs0618[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture)
            .Should().Contain("ITokenProvider",
                "废弃提示必须给出可执行的迁移目标，不能只说『已过时』");
    }

    [Fact]
    public void RecommendedEntries_CalledViaIAppScopeSwitcher_ReportNoDiagnostics()
    {
        var diagnostics = RunGenerator(RecommendedProbeSource)
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToArray();

        diagnostics.Should().BeEmpty(
            "BC-27 能被接受的唯一前提：迁移目标（IAppScopeSwitcher.UseAppScope / UseDefaultAppScope）"
            + "必须干净可用、零诊断。实际：\n" + string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    /// <summary>
    /// `BC-27` 附带根治：移除 <c>BeginScope(string)</c> 后，<c>BeginScope(null)</c> 的重载二义应消失。
    /// </summary>
    /// <remarks>
    /// 2.0.11 阶段仅对 <c>BeginScope(string)</c> 标注 <c>[Obsolete]</c> —— 但 `<c>[Obsolete]</c>` **不参与重载决议**，
    /// 故 `BeginScope(null)` 当时仍报 <c>CS0121</c>。本用例钉死"真正删除重载"才是根治手段。
    /// </remarks>
    [Fact]
    public void BeginScopeNull_NoLongerAmbiguous_AfterLegacyOverloadRemoved()
    {
        var ambiguities = RunGenerator(NullScopeProbeSource)
            .Where(d => d.Id == "CS0121")
            .ToArray();

        ambiguities.Should().BeEmpty(
            "BC-27 附带根治：BeginScope(string) 移除后，BeginScope(null) 只剩 BeginScope(IMudAppContext) 一个候选，"
            + "不再产生 CS0121 二义。实际：\n" + string.Join("\n", ambiguities.Select(d => d.ToString())));
    }

    /// <summary>
    /// 跑生成器 + 编译，返回<b>输入源 + 生成产物</b>的全部编译诊断。
    /// </summary>
    private static IEnumerable<Diagnostic> RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "AppSwitchObsoleteMigrationTests",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        return output.GetDiagnostics();
    }
}
