// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Linq;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// L-5：生成代码「应用切换守卫」的契约级断言。
/// </summary>
/// <remarks>
/// <para>
/// 快照测试只保证"形状未变"，不表达**为什么**要有这些分支 —— 一次"顺手简化"就能把安全分支删掉而快照同步更新后无人察觉。
/// 本组用例把 MT-02（授权器默认拒绝）与 MT-18（appKey 前置格式校验、异常消息不插值原始 appKey、
/// 防御日志注入）的**契约**显式钉死。
/// </para>
/// <para>
/// 对应的运行期行为由 <c>Mud.HttpUtils.Client.Tests/AppManagementStartupValidatorTests</c> 覆盖；
/// 二者互补：本文件守卫"生成什么代码"，后者守卫"接线缺失时的行为"。
/// </para>
/// </remarks>
public class ApplicationSwitchGuardContractTests
{
    private const string SwitchApiSource = """
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface IGuardApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    private static string Generate()
    {
        var (_, outputCompilation) = VerifyFixture.RunGeneratorDriver(SwitchApiSource);

        // 实现类产物（跳过 Preserve / Registration 等辅助文件）
        var implementation = outputCompilation.SyntaxTrees
            .Skip(1)
            .Select(t => (Name: System.IO.Path.GetFileName(t.FilePath), Text: t.ToString()))
            .FirstOrDefault(x => !x.Name.Contains("Preserve")
                                 && !x.Name.Contains("Registration")
                                 && !x.Name.Contains("EventHandler")
                                 && !x.Name.Contains("FormContent"));

        implementation.Text.Should().NotBeNullOrEmpty("应产出接口实现类");
        return implementation.Text;
    }

    [Fact]
    public void GeneratedCode_ShouldValidateAppKeyFormatBeforeAuthorization()
    {
        var code = Generate();

        code.Should().Contain("global::Mud.HttpUtils.AppKey.IsValid(appKey)",
            "MT-18：授权器不应承担格式校验职责，生成代码必须在授权判定之前做 appKey 格式前置校验");
    }

    [Fact]
    public void GeneratedCode_ShouldDenyWhenAuthorizerMissing()
    {
        var code = Generate();

        code.Should().Contain("_appAuthorizer == null",
            "MT-02（BC-18）：授权器未注册时必须显式拒绝（默认拒绝），而非静默放行");
        code.Should().Contain("AllowAllAppAccessAuthorizer",
            "异常消息必须给出显式放行逃生门，使「放行」成为需在代码中声明的意图");
        code.Should().Contain("InvalidOperationException",
            "未注册授权器应抛 InvalidOperationException（接线缺陷，非业务拒绝）");
    }

    [Fact]
    public void GeneratedCode_ShouldThrowUnauthorizedWhenCanSwitchToReturnsFalse()
    {
        var code = Generate();

        code.Should().Contain("CanSwitchTo(appKey)");
        code.Should().Contain("UnauthorizedAccessException",
            "授权判定失败应抛 UnauthorizedAccessException（业务拒绝）");
    }

    [Fact]
    public void GeneratedCode_ShouldNotInterpolateRawAppKeyIntoExceptionMessages()
    {
        var code = Generate();

        // MT-18：appKey 可能来自请求参数，插值进异常消息构成日志注入面。
        code.Should().NotContain("$\"当前调用主体无权切换到应用 '{appKey}'。\"",
            "MT-18 回归：异常消息不得直接插值原始 appKey");
        code.Should().NotContain("$\"当前调用主体无权切换到应用 '{appKey}",
            "MT-18 回归：异常消息不得直接插值原始 appKey");
    }

    [Fact]
    public void GeneratedCode_ShouldExposeScopeBasedAppSwitch()
    {
        var code = Generate();

        code.Should().Contain("UseAppScope(string appKey)",
            "MT-19：必须提供「切换 + 自动归还」的入口（UseApp 的无作用域切换在长生命周期宿主下会残留上下文）");
        code.Should().Contain("_appContextHolder.BeginScope(context)");
    }

    [Fact]
    public void GeneratedCode_ShouldGuardEveryAppSwitchEntryPoint()
    {
        var code = Generate();

        // UseApp / BeginScope / UseAppScope 三个入口共用同一守卫；数量 = 入口数
        var guardCount = System.Text.RegularExpressions.Regex
            .Matches(code, System.Text.RegularExpressions.Regex.Escape("_appAuthorizer == null")).Count;

        guardCount.Should().BeGreaterThanOrEqualTo(3,
            "三个应用切换入口（UseApp / BeginScope / UseAppScope）都必须带授权器守卫，" +
            $"实际守卫数 {guardCount} —— 漏掉任一个都会成为越权旁路");
    }

    #region G7-01 继承默认模式 appManager 透传契约

    private const string InheritedDefaultModeSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(IsAbstract = true)]
            public interface IBaseApi
            {
                [Get("/base")]
                System.Threading.Tasks.Task<string> GetBaseDataAsync();
            }

            [HttpClientApi(InheritedFrom = "BaseApi")]
            public interface IDerivedApi : IBaseApi
            {
                [Get("/derived")]
                System.Threading.Tasks.Task<string> GetDerivedDataAsync();
            }
        }
        """;

    private const string InheritedTokenManagerModeSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            public interface ITestTokenManager
            {
                IMudAppContext GetDefaultApp();
                IMudAppContext GetApp(string appKey);
            }

            [HttpClientApi(TokenManage = "ITestTokenManager", IsAbstract = true)]
            public interface IBaseApi
            {
                [Get("/base")]
                System.Threading.Tasks.Task<string> GetBaseDataAsync();
            }

            [HttpClientApi(TokenManage = "ITestTokenManager", InheritedFrom = "BaseApi")]
            public interface IDerivedApi : IBaseApi
            {
                [Get("/derived")]
                System.Threading.Tasks.Task<string> GetDerivedDataAsync();
            }
        }
        """;

    private static string ExtractImplementation(string source)
    {
        var (_, outputCompilation) = VerifyFixture.RunGeneratorDriver(source);

        // G7-01 修复：基类与派生类是两个独立生成文件（BaseApi.g.cs / DerivedApi.g.cs），
        // 而 appManager/appAuthorizer 的「基类声明 + 派生类透传」横跨两者 —— 只取单个文件
        // 会漏掉透传断言（此前实现取第一个文件 = 基类，导致 ShouldForwardAppManager 误报失败）。
        // 因此与 VerifyGenerator 同口径：过滤辅助文件后拼接全部实现类文件再断言。
        var implementations = outputCompilation.SyntaxTrees
            .Skip(1)
            .Select(t => (Name: System.IO.Path.GetFileName(t.FilePath), Text: t.ToString()))
            .Where(x => !x.Name.Contains("Preserve")
                        && !x.Name.Contains("Registration")
                        && !x.Name.Contains("EventHandler")
                        && !x.Name.Contains("FormContent")
                        && !x.Name.Contains("TokenProvider"))
            .Select(x => x.Text)
            .ToArray();

        implementations.Should().NotBeEmpty("应产出接口实现类");
        return string.Join("\n// ===== 分隔 =====\n", implementations);
    }

    /// <summary>
    /// G7-01：继承默认模式（基类既无 HttpClient 亦无 TokenManage）时，派生类构造函数必须在
    /// <c>base(...)</c> 中透传 <c>appManager: appManager</c>，否则 DI 注入被丢弃，基类 _appManager
    /// 恒为 null → UseApp/BeginScope(appKey) 恒抛「当前模式不支持」（多应用不可用，P0）。
    /// 与 appAuthorizer 透传（MT-02 P0 修复）同一模式：基类字段由基类构造函数赋值。
    /// </summary>
    [Fact]
    public void GeneratedCode_InheritedDefaultModeBase_ShouldForwardAppManager()
    {
        var code = ExtractImplementation(InheritedDefaultModeSource);

        code.Should().Contain("appManager: appManager",
            "G7-01：默认模式基类的派生类构造函数必须透传 appManager（命名实参，与 appAuthorizer 同构）");
    }

    /// <summary>
    /// G7-01 防误加：继承 TokenManager 模式基类时，派生类 <b>不得</b> 生成 <c>appManager: appManager</c>
    /// （派生 appManager 形参类型为 TokenManager，与基类 IAppManager&lt;IMudAppContext&gt; 形参不匹配，
    /// 误加会导致基类调用编译失败）。该用例钉死「仅默认模式基类透传」的判定口径（BaseHasAppManager）。
    /// </summary>
    /// <summary>
    /// G8-10（承接 G7-11）：受信路径三入口（<c>Current</c> setter / <c>SwitchTo(IMudAppContext)</c> /
    /// <c>BeginScope(IMudAppContext)</c>）的生成物 XML 注释必须声明<b>信任边界</b>。
    /// </summary>
    /// <remarks>
    /// 07 G7-11 已定「不加授权校验，只明确信任边界」，但落点只覆盖 README 与 <c>UseApp</c>/<c>UseAppScope</c>；
    /// 三处<b>实例入口</b>此前无任何提示（<c>BeginScope(IMudAppContext)</c> 仅有线程归属警告）。
    /// 本用例把「生成物自带信任边界声明」钉死，避免结论只存在于文档而使用者看不到。
    /// </remarks>
    [Fact]
    public void ContextBasedSwitch_DocumentsTrustBoundary()
    {
        var code = ExtractImplementation(InheritedDefaultModeSource);

        code.Should().Contain("信任边界",
            "G8-10：实例入口必须声明信任边界（不执行 appKey 校验与授权判定）");
        code.Should().Contain("不执行",
            "G8-10：必须明确「不执行」授权判定，而非含糊表述");
        code.Should().Contain("UseAppScope",
            "G8-10：必须指向不可信输入的正确入口（UseAppScope / BeginScope(string)）");

        // 三处实例入口都应有 remarks（Current / SwitchTo / BeginScope(IMudAppContext)）：
        // 统计信任边界段落出现次数（Current 1 + SwitchTo 1 + BeginScope 1 = 3）。
        var trustBoundarySections = System.Text.RegularExpressions.Regex
            .Matches(code, "<b>信任边界</b>").Count;
        trustBoundarySections.Should().BeGreaterThanOrEqualTo(3,
            $"受信路径三入口（Current / SwitchTo / BeginScope(IMudAppContext)）都应声明信任边界；实际 {trustBoundarySections} 处");
    }

    [Fact]
    public void GeneratedCode_TokenManagerBaseInheritance_ShouldNotForwardIAppManager()
    {
        var code = ExtractImplementation(InheritedTokenManagerModeSource);

        code.Should().NotContain("appManager: appManager",
            "G7-01 防误加：TokenManager 模式基类不得透传命名实参 appManager（类型不匹配），仅默认模式基类透传");
    }

    #endregion
}
