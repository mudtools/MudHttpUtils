// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯社会公共秩序等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests.Infrastructure;

/// <summary>
/// 仓库根目录定位器（与 Generator.Tests 的同名基建同型）：以 <c>Mud.HttpUtils.slnx</c> 哨兵文件
/// 自 <c>AppContext.BaseDirectory</c> 向上回溯。供源码文本级契约守卫测试（如 G9-01 的 ns2.0
/// 分支接线文本钉子）读取库源码使用。
/// </summary>
public static class TestRepoRoot
{
    /// <summary>仓库根目录绝对路径。</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>以仓库根为基解析子路径。</summary>
    public static string PathOf(params string[] segments)
        => Path.Combine(new[] { Root }.Concat(segments).ToArray());

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mud.HttpUtils.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("无法定位仓库根目录（未找到 Mud.HttpUtils.slnx 哨兵文件）。");
    }
}
