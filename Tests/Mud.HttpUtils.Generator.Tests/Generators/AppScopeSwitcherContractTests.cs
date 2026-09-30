// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Linq;
using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// SW-01 / SW-08 / I-22 / I-23：生成类对 <c>IAppScopeSwitcher</c> 的附加实现契约。
/// </summary>
/// <remarks>
/// <para>
/// 背景：<c>UseAppScope</c> / <c>UseDefaultAppScope</c> 由生成器<b>无条件</b>发射（非 HttpClient 模式），
/// 但此前<b>未声明在任何抽象接口上</b> ⇒ 按接口编程的调用方拿不到这两个安全入口（SW-01）。
/// 修复方式为新增 <c>IAppScopeSwitcher</c> 接口，并在"接口已继承旧切换契约"时把该接口追加到生成类的继承列表。
/// </para>
/// <para>
/// 本组用例钉死三门控条件（DP-3）：
/// ① 普通接口（无切换契约）<b>不得</b>被追加（防止"无条件挂接口"的接口膨胀）；
/// ② 接口已（直接或间接）继承 <c>IAppScopeSwitcher</c> 时<b>不得</b>重复追加（传递实现已足够）；
/// ③ HttpClient 模式<b>不得</b>追加（该模式不发射任何切换成员，追加会导致编译失败）。
/// 并要求"追加后生成类确实实现该契约"由 <b>真编译 + 反射</b>证明（非仅文本断言）。
/// </para>
/// </remarks>
public class AppScopeSwitcherContractTests
{
    private const string PlainApiSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface IPlainApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    private const string ScopeSurfaceApiSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface IScopeSurfaceApi : IAppScopeSwitcher
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    private const string TokenManagerLegacySwitcherSource = """
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
            public interface ITokenApi : IAppContextSwitcher
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    private const string DefaultModeLegacySwitcherSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface IDefaultLegacyApi : IAppContextSwitcher
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    private const string HttpClientModeLegacySwitcherSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
            public interface IHttpClientLegacyApi : IAppContextSwitcher
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    private const string AddedScopeSwitcher = "global::Mud.HttpUtils.IAppScopeSwitcher";

    [Fact]
    public void PlainInterface_DoesNotDeclareAppScopeSwitcher()
    {
        var code = GenerateImplementation(PlainApiSource);

        code.Should().NotContain(AddedScopeSwitcher,
            "DP-3：不因『接口未声明 UseAppScope』就给所有生成类挂接口，否则会制造新的接口膨胀");
    }

    [Fact]
    public void InterfaceDeclaringScopeSurface_DoesNotDuplicateAppScopeSwitcher()
    {
        var code = GenerateImplementation(ScopeSurfaceApiSource);

        code.Should().NotContain(AddedScopeSwitcher,
            "接口已显式继承 IAppScopeSwitcher 时生成类经接口传递即获得该契约，重复列出属冗余");
    }

    [Fact]
    public void HttpClientModeInterfaceWithLegacySwitcher_DoesNotDeclareAppScopeSwitcher()
    {
        var code = GenerateImplementation(HttpClientModeLegacySwitcherSource);

        code.Should().NotContain(AddedScopeSwitcher,
            "HttpClient 模式不发射任何切换成员，追加 IAppScopeSwitcher 会导致生成类无法实现契约（编译失败）");
    }

    [Fact]
    public void TokenManagerInterfaceWithLegacySwitcher_DeclaresAppScopeSwitcher()
    {
        var code = GenerateImplementation(TokenManagerLegacySwitcherSource);

        code.Should().Contain(AddedScopeSwitcher,
            "SW-01：接口继承旧切换契约时，生成类必须附加实现 IAppScopeSwitcher，使按接口编程可拿到 UseAppScope");
    }

    [Fact]
    public void TokenManagerGeneratedClass_Implements_IAppScopeSwitcher()
    {
        var assembly = GeneratorCompileAssert.EmitAndLoad(
            TokenManagerLegacySwitcherSource,
            description: "TokenManager 模式 + IAppContextSwitcher 的生成类");

        ImplementsScopeSwitcher(assembly).Should().BeTrue(
            "SW-01/I-22：IAppScopeSwitcher 的每个成员都必须在生成类上存在且签名一致（抽象面 ⊆ 生成面）");
    }

    [Fact]
    public void DefaultModeGeneratedClass_Implements_IAppScopeSwitcher_WhenInterfaceDeclaresIt()
    {
        var assembly = GeneratorCompileAssert.EmitAndLoad(
            ScopeSurfaceApiSource,
            description: "Default 模式 + 显式继承 IAppScopeSwitcher 的生成类");

        ImplementsScopeSwitcher(assembly).Should().BeTrue(
            "SW-01：Default 模式（无 TokenManage）必须能通过 IAppScopeSwitcher 使用作用域式切换，" +
            "这是 IAppContextSwitcher（含 GetTokenAsync）在默认模式下不可用的替代面");
    }

    [Fact]
    public void IAppScopeSwitcher_DeclaresExactlyTwoScopeMembers()
    {
        var declared = typeof(IAppScopeSwitcher)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.MemberType is MemberTypes.Method or MemberTypes.Property)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        declared.Should().Equal(["UseAppScope", "UseDefaultAppScope"],
            "I-23：作用域面只承载『返回 IDisposable 的 appKey 入口』，不得含 BeginScope 同名成员，也不得复刻无作用域的 UseApp/UseDefaultApp");

        typeof(IAppScopeSwitcher).GetInterfaces().Should().Contain(typeof(IAppContextHolder),
            "作用域面与受信实例面平行：共同扩展 IAppContextHolder，而不是继承含 GetTokenAsync 的 IAppContextSwitcher");
    }

    [Fact]
    public void DefaultModeInterfaceWithLegacySwitcher_ReportsHttpClient024WithScopeSwitcherHint()
    {
        // 注意：GeneratorDriver 是不可变对象，RunGeneratorsAndUpdateCompilation 不会更新其内部运行结果，
        // 因此必须使用 out 参数取生成器诊断（driver.GetRunResult() 在该调用后仍为空）。
        var compilation = CSharpCompilation.Create(
            "AppScopeSwitcherContractTests.Diagnostics",
            [CSharpSyntaxTree.ParseText(DefaultModeLegacySwitcherSource)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var placeholder = diagnostics.FirstOrDefault(d => d.Id == "HTTPCLIENT024");

        placeholder.Should().NotBeNull(
            "SW-08：Default 模式接口继承 IAppContextSwitcher 时 GetTokenAsync 无法生成 ⇒ 必须由既有 HTTPCLIENT024（Error）拦截。"
            + $"\n[GEN]={string.Join(",", diagnostics.Select(d => d.Id + ":" + d.Severity))}");

        placeholder!.Severity.Should().Be(DiagnosticSeverity.Error);
        placeholder.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Should().Contain("IAppScopeSwitcher",
            "SW-08：失败原因必须给出精确迁移指引（复用 HTTPCLIENT024 的 reason，不新增 HTTPCLIENT038 以免同处双 Error）");
    }

    /// <summary>
    /// 提取生成器产出的接口实现类文本（跳过 Preserve / Registration 等辅助文件）。
    /// </summary>
    private static string GenerateImplementation(string source)
    {
        var (_, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);

        var implementation = outputCompilation.SyntaxTrees
            .Skip(1)
            .Select(t => (Name: Path.GetFileName(t.FilePath), Text: t.ToString()))
            .FirstOrDefault(x => !x.Name.Contains("Preserve")
                                 && !x.Name.Contains("Registration")
                                 && !x.Name.Contains("EventHandler")
                                 && !x.Name.Contains("FormContent"));

        implementation.Text.Should().NotBeNullOrEmpty("应产出接口实现类");
        return implementation.Text;
    }

    /// <summary>
    /// 断言程序集中存在实现了 <see cref="IAppScopeSwitcher"/> 的具体类。
    /// </summary>
    private static bool ImplementsScopeSwitcher(Assembly assembly)
        => assembly.GetTypes()
            .Any(t => t is { IsClass: true, IsAbstract: false }
                      && typeof(IAppScopeSwitcher).IsAssignableFrom(t));
}
