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
}
