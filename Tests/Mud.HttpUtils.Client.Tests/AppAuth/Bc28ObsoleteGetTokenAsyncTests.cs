// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio  2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// F-12（BC-28）：<see cref="IAppContextSwitcher.GetTokenAsync"/> 的移除版本定稿守卫 ——
/// 必须带 <see cref="ObsoleteAttribute"/>，消息含 BC-28 编号、4.0 移除承诺与迁移目标（ITokenProvider）。
/// </summary>
/// <remarks>
/// 钉死"定移除版本"这一 API 治理动作：若有人把移除版本悄悄改掉或删掉迁移指引，
/// 本用例立即失败并触发设计讨论。生成类成员不标 <c>[Obsolete]</c>（GEN-02 噪音豁免）不变。
/// </remarks>
public class Bc28ObsoleteGetTokenAsyncTests
{
    [Fact]
    public void IAppContextSwitcher_GetTokenAsync_IsObsoleteWithBc28Message()
    {
        var member = typeof(IAppContextSwitcher).GetMethod(nameof(IAppContextSwitcher.GetTokenAsync));

        member.Should().NotBeNull();

        var obsolete = member!.GetCustomAttribute<ObsoleteAttribute>();

        obsolete.Should().NotBeNull("BC-28 弃用入口必须可被编译器检出（经接口调用得 CS0618）");
        obsolete!.Message.Should().Contain("BC-28", "弃用消息须带方案编号便于追溯");
        obsolete.Message.Should().Contain("4.0", "F-12：移除版本已定稿为 4.0，改动须经设计讨论");
        obsolete.Message.Should().Contain("ITokenProvider", "弃用消息须给出可执行的迁移目标");
    }
}
