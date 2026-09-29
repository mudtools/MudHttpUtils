// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 测试基建：以哨兵文件（Mud.HttpUtils.slnx）定位仓库根目录，供契约 / 守卫类测试解析仓库内源码与文档路径。
/// </summary>
/// <remarks>
/// 修复说明：原各测试以相对 testhost 工作目录（bin\Debug\&lt;tfm&gt;）的固定层级
/// <c>@"../../../../..."</c> 解析仓库路径；「各 .Tests 项目输出目录互相隔离」的输出布局调整后
/// 层级差一级，导致 DirectoryNotFoundException（存量缺陷）。现锚定程序集位置向上查找哨兵文件，
/// 与 CWD / 输出目录层级无关。
/// </remarks>
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
