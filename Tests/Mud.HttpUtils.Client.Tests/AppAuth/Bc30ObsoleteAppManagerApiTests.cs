// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio  2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// B-3（BC-30）：「弃用而不删除」的编译期验证 ——
/// <see cref="IAppManager{TAppContext}"/> 三个死 API 入口必须带 <see cref="ObsoleteAttribute"/>
/// 且消息含 BC-30 编号与迁移指引，使下游在编译期收到弃用告警。
/// </summary>
public class Bc30ObsoleteAppManagerApiTests
{
    [Theory]
    [InlineData("GetWebApi")]
    [InlineData("GetDefaultWebApi")]
    [InlineData("RegisterSwitcherFactory")]
    public void IAppManager_Member_IsObsoleteWithBc30Message(string memberName)
    {
        var member = typeof(IAppManager<>).GetMethods().Single(m => m.Name == memberName);

        var obsolete = member.GetCustomAttribute<ObsoleteAttribute>();

        obsolete.Should().NotBeNull($"{memberName} 是 BC-30 弃用入口，必须可被编译器检出");
        obsolete!.Message.Should().Contain("BC-30", "弃用消息须带方案编号便于追溯");
        obsolete.Message.Should().Contain("UseDefaultAppScope", "弃用消息须给出替代路径");
    }

    [Theory]
    [InlineData("GetWebApi")]
    [InlineData("GetDefaultWebApi")]
    [InlineData("RegisterSwitcherFactory")]
    public void DefaultAppManager_Member_IsObsoleteWithBc30Message(string memberName)
    {
        var member = typeof(DefaultAppManager<>).GetMethods().Single(m => m.Name == memberName);

        var obsolete = member.GetCustomAttribute<ObsoleteAttribute>();

        obsolete.Should().NotBeNull("实现侧同样标注，覆盖经具体类型调用的下游");
        obsolete!.Message.Should().Contain("BC-30");
    }
}
