// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯社会公共秩序等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G9-03：快照基线卫生守卫——项目根不得出现 <c>.verified.txt</c> 孤儿快照。
/// </summary>
/// <remarks>
/// Verify 的默认落盘是「测试类所在目录」。历史成因：快照测试类曾位于项目根，后被整体迁入
/// <c>Snapshots\</c>（基线目录正确），根目录的同名 <c>.verified.txt</c> 是迁移前的接收残留被误提交——
/// 它不参与任何测试执行，但内容为旧版（506 行 vs 现行 547 行），对基线阅读者构成「双基线」误信风险。
/// 本守卫防其复活；若快照测试类将来再次迁回项目根，请同步修改本守卫与 .gitignore 规则。
/// </remarks>
public class SnapshotBaselineHygieneTests
{
    /// <summary>项目根（<c>Tests/Mud.HttpUtils.Generator.Tests/</c>）不得有任何 <c>.verified.txt</c>。</summary>
    [Fact]
    public void NoVerifiedSnapshotAtProjectRoot()
    {
        var projectRoot = Path.Combine(TestRepoRoot.Root, "Tests", "Mud.HttpUtils.Generator.Tests");

        var orphans = Directory.GetFiles(projectRoot, "*.verified.txt", SearchOption.TopDirectoryOnly);

        orphans.Should().BeEmpty(
            "项目根不得存在孤儿快照（基线只允许在测试类旁的 Snapshots\\ / Generators\\ 子目录）；" +
            $"发现：[{string.Join(", ", orphans.Select(Path.GetFileName))}]");
    }

    /// <summary>
    /// 正向对照：既有基线目录必须仍在产出快照——防止本守卫在目录结构大改后「空转变绿」。
    /// </summary>
    [Fact]
    public void SnapshotBaselineDirectories_StillContainBaselines()
    {
        var projectRoot = Path.Combine(TestRepoRoot.Root, "Tests", "Mud.HttpUtils.Generator.Tests");

        Directory.GetFiles(Path.Combine(projectRoot, "Snapshots"), "*.verified.txt", SearchOption.TopDirectoryOnly)
            .Should().NotBeEmpty("Snapshots\\ 基线目录应持有主快照基线（目录迁移须同步更新本守卫）");
        Directory.GetFiles(Path.Combine(projectRoot, "Generators"), "*.verified.txt", SearchOption.TopDirectoryOnly)
            .Should().NotBeEmpty("Generators\\ 基线目录应持有 HeaderCollection 快照基线（目录迁移须同步更新本守卫）");
    }
}
