// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Linq;
using System.Reflection;
using Mud.HttpUtils.Generator.Consts;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// I-24（SW-11）：应用切换成员名的单一事实源守卫。
/// </summary>
/// <remarks>
/// <para>
/// <c>AppSwitchMemberNames</c> 是"生成器发射了哪些切换成员"的唯一清单：
/// 登记表（<c>RegisterInfrastructureMembers</c>）与发射点（<c>ConstructorGenerator</c>）必须一致，
/// 否则出现「登记了没发射」（CS0535）或「发射了没登记」（契约补全重复发射 ⇒ CS0111/CS0102）。
/// G8-15 的 <c>UseAppScope</c> 漏登记即为真实案例。
/// </para>
/// <para>
/// 本用例用<b>真编译 + 反射</b>取代 v1.0 设想的"源文本扫描"：反射取生成类的 public 成员名集合，
/// 断言其 ⊇ 常量表 —— 只锁"结果"，不锁"写法"（后者受注释、格式、字符串拼接干扰）。
/// </para>
/// </remarks>
public class AppSwitchMemberNamesTests
{
    private const string DefaultModeSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface IMemberNamesProbeApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    /// <summary>
    /// 混合模式继承（基类 Default + 派生 TokenManager）⇒ 派生类以 <c>public new</c> 隐藏基类切换成员。
    /// </summary>
    private const string MixedModeInheritanceSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            public interface ITestTokenManager
            {
                IMudAppContext GetDefaultApp();
                IMudAppContext GetApp(string appKey);
            }

            [HttpClientApi(IsAbstract = true)]
            public interface IBaseDefaultProbeApi
            {
                [Get("/base")]
                System.Threading.Tasks.Task<string> GetBaseAsync();
            }

            [HttpClientApi(TokenManage = "ITestTokenManager", InheritedFrom = "BaseDefaultProbeApi")]
            public interface IDerivedTokenProbeApi : IBaseDefaultProbeApi
            {
                [Get("/derived")]
                System.Threading.Tasks.Task<string> GetDerivedAsync();
            }
        }
        """;

    /// <summary>
    /// 使用方接口**自行声明**三个旧入口（`BC-27` 豁免场景）。
    /// </summary>
    private const string DeclaresLegacyMembersSource = """
        using System;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface ILegacyDeclaringApi
            {
                IMudAppContext UseApp(string appKey);

                IMudAppContext UseDefaultApp();

                IDisposable BeginScope(string appKey);

                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    [Fact]
    public void GeneratedClass_PublicMembers_SupersetOfSsotConstants()
    {
        var assembly = GeneratorCompileAssert.EmitAndLoad(
            DefaultModeSource,
            description: "Default 模式生成类（成员名 SSOT 守卫）");

        var generated = assembly.GetTypes()
            .FirstOrDefault(t => t is { IsClass: true, IsAbstract: false }
                                 && t.GetMethods().Any(m => m.Name == AppSwitchMemberNames.UseAppScope));

        generated.Should().NotBeNull("应产出含 UseAppScope 的生成类");

        var publicMemberNames = generated!.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        publicMemberNames.Should().Contain(AppSwitchMemberNames.Unconditional,
            "I-24：非 HttpClient 模式生成类的 public 成员必须覆盖**无条件**清单 —— "
            + "缺项意味着「登记了但没发射」（契约补全不会补，直接 CS0535），或清单与发射点已漂移");
        publicMemberNames.Should().NotContain(AppSwitchMemberNames.Conditional,
            "BC-27：接口未声明旧入口（UseApp / UseDefaultApp）时不得发射它们 —— 唯一推荐入口是 UseAppScope / UseDefaultAppScope");
    }

    /// <summary>
    /// `BC-27` 豁免：接口**自行声明**旧入口时必须继续发射实现。
    /// </summary>
    /// <remarks>
    /// 这是 `BC-27` 最关键的安全阀：若无条件删除发射点，"接口自行声明 <c>UseApp</c> 并由生成器实现"的既有合法写法
    /// 会落入契约补全（<c>NotSupportedException</c> 占位 + <c>HTTPCLIENT024</c>（Error））⇒ 从"可用"变为"编译失败"。
    /// </remarks>
    [Fact]
    public void DeclaringLegacyMembers_StillEmitsThem_WithoutPlaceholderDiagnostic()
    {
        var compilation = CreateCompilation(DeclaresLegacyMembersSource);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var generatedText = string.Join(
            "\n",
            output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        generatedText.Should().Contain("IMudAppContext UseApp(string appKey)",
            "BC-27 豁免：接口自行声明 UseApp 时生成器必须实现它");
        generatedText.Should().Contain("IMudAppContext UseDefaultApp()",
            "BC-27 豁免：接口自行声明 UseDefaultApp 时生成器必须实现它");
        generatedText.Should().Contain("IDisposable BeginScope(string appKey)",
            "BC-27 豁免：接口自行声明 BeginScope(string) 时生成器必须实现它（按**签名**判定，不误判 Holder 面重载）");

        diagnostics.Where(d => d.Id == "HTTPCLIENT024").Should().BeEmpty(
            "BC-27 豁免生效时不得出现占位诊断 —— 出现即说明该成员落入了契约补全（运行期会抛 NotSupportedException）");
    }

    [Fact]
    public void HiddenAppMember_EmitsCallPathNotice()
    {
        // 混合模式会同时产出基类与派生类两个实现文件，必须精确定位派生类（只有它走 `public new`）。
        var code = GenerateImplementation(MixedModeInheritanceSource, "DerivedTokenProbeApi");

        code.Should().Contain("public new ",
            "前置条件：基类 Default + 派生 TokenManager 时必须走 `public new` 分支（否则本用例无意义）");
        code.Should().Contain("HTTPCLIENT028",
            "SW-09：`public new` 隐藏基类成员属**静默调用路径分叉**（经基类引用会走到基类实现），"
            + "必须把该警示发射进 XML 文档注释，使 IDE 悬停即见（此前仅存在于生成器源码注释里，产物中从未发射）");
        code.Should().Contain("基类引用",
            "SW-09：提示必须点明「经基类引用调用」这一具体触发方式，否则用户无法据此自查");
    }

    [Fact]
    public void NonHiddenScenario_DoesNotEmitCallPathNotice()
    {
        var code = GenerateImplementation(DefaultModeSource);

        code.Should().NotContain("HTTPCLIENT028",
            "SW-09：无继承（或一致模式继承）时不得发射该提示 —— 避免把只有混合模式才成立的警示扩散为常态噪音");
    }

    /// <summary>
    /// 构造编译单元（含 <see cref="BasicReferenceAssemblies"/>，但不注入 ImplicitUsings 头 —— 用例源码自带 using）。
    /// </summary>
    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "AppSwitchMemberNamesTests",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>
    /// 提取生成器产出的接口实现类文本（跳过 Preserve / Registration 等辅助文件）。
    /// </summary>
    /// <param name="source">被测接口源码。</param>
    /// <param name="typeNameContains">实现类文件名须包含的片段（多实现类时用于精确定位）。</param>
    private static string GenerateImplementation(string source, string? typeNameContains = null)
    {
        var (_, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);

        var implementation = outputCompilation.SyntaxTrees
            .Skip(1)
            .Select(t => (Name: Path.GetFileName(t.FilePath), Text: t.ToString()))
            .FirstOrDefault(x => !x.Name.Contains("Preserve")
                                 && !x.Name.Contains("Registration")
                                 && !x.Name.Contains("EventHandler")
                                 && !x.Name.Contains("FormContent")
                                 && (typeNameContains == null || x.Name.Contains(typeNameContains)));

        implementation.Text.Should().NotBeNullOrEmpty(
            $"应产出接口实现类（文件名含 '{typeNameContains ?? "*"}'）");
        return implementation.Text;
    }
}
