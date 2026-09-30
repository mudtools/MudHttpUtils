// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// SW-02 / SW-05 / SW-13 / I-25：多应用切换的「文档与消息契约」守卫。
/// </summary>
/// <remarks>
/// <para>
/// 这类缺陷（错误的修复指引、互相打架的措辞）**不会**被编译或行为测试拦截，只能靠文本守卫钉死；
/// 同时刻意<b>不做</b>"逐字相同"的过度约束 —— 只锁定关键结论与关键词，保留文档可读性（`I-25`）。
/// </para>
/// </remarks>
public class AppSwitchDocsContractTests
{
    private static readonly string DefaultAppManagerSource =
        TestRepoRoot.PathOf("Mud.HttpUtils.Abstractions", "AppContext", "DefaultAppManager.cs");

    private static readonly string ClientReadme =
        TestRepoRoot.PathOf("Mud.HttpUtils.Client", "README.md");

    private static readonly string AbstractionsReadme =
        TestRepoRoot.PathOf("Mud.HttpUtils.Abstractions", "README.md");

    private static readonly string GeneratorReadme =
        TestRepoRoot.PathOf("Mud.HttpUtils.Generator", "README.md");

    private static readonly string Changelog =
        TestRepoRoot.PathOf("CHANGELOG.md");

    [Fact]
    public void DefaultAppManager_FactoryHint_DoesNotSuggest_UncompilableLambda()
    {
        var source = File.ReadAllText(DefaultAppManagerSource);

        source.Should().NotContain("ctx => new",
            "SW-05：工厂委托只有一个入参（Func<TAppContext, TContextSwitcher>），而生成类（默认模式）构造函数有 3 个必需参数，" +
            "`ctx => new Xxx(ctx)` 必然编译失败 —— 异常文案不得再给出该示例");

        source.Should().Contain("UseAppScope",
            "SW-05：文案必须指向推荐路径（作用域式切换），而不是让调用方去写一份不可编译的工厂委托");
    }

    [Fact]
    public void AuthorizerMissingConclusion_IsConsistentAcrossDocs()
    {
        var authorizerRow = File.ReadAllLines(ClientReadme)
            .FirstOrDefault(line => line.Contains("`AppAccessAuthorizer`"));

        authorizerRow.Should().NotBeNull("Client/README.md 的选项表必须列出 AppAccessAuthorizer");

        authorizerRow!.Should().Contain("InvalidOperationException",
            "SW-04/SW-13：未注册授权器时按 appKey 切换直接抛 InvalidOperationException（默认拒绝），该结论须在配置表中显式声明");
        authorizerRow.Should().Contain("默认拒绝",
            "同上：措辞必须与实现一致");
        authorizerRow.Should().NotContain("不执行授权判定",
            "SW-04：原文『为 null 时不执行授权判定（仅存在性校验）』与『默认拒绝』字面冲突，必须移除");

        File.ReadAllText(AbstractionsReadme).Should().Contain("InvalidOperationException",
            "Abstractions/README.md 的授权器注册约定必须与默认拒绝结论一致");
        File.ReadAllText(Changelog).Should().Contain("默认拒绝",
            "CHANGELOG 的『令牌与多应用』行为基线必须保留默认拒绝表述");
    }

    [Fact]
    public void GeneratorReadme_Recommends_UseAppScope_AsSingleEntry()
    {
        var readme = File.ReadAllText(GeneratorReadme);

        readme.Should().Contain("唯一推荐入口",
            "SW-02：`UseAppScope` 与 `BeginScope(appKey)` 语义等价，README 必须收敛到唯一推荐名，避免团队无法判定正确写法");
        readme.Should().Contain("IAppScopeSwitcher",
            "SW-01：文档必须说明作用域式切换的抽象面（IAppScopeSwitcher），否则按接口编程者仍找不到安全入口");
    }
}
