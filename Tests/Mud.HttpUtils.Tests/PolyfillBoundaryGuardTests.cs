using System.Linq;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// Polyfill 边界护栏（BC-29）。
/// <para>
/// 规则：polyfill 的 <c>#if</c> 锚点必须对齐「该 API 的 in-box 首个 TFM」，且**资产矩阵必须覆盖该边界**，
/// 否则更上游的 TFM 会解析到更低 TFM 的资产并与其 BCL 类型撞名：
/// 实测 net7.0 下游引用本地验证包（Abstractions 只到 net6.0）时，
/// 只要在自身代码标注 <c>[RequiresDynamicCode]</c> 即报
/// <c>error CS0433: 类型"RequiresDynamicCodeAttribute"同时存在于 Mud.HttpUtils.Abstractions 和 System.Runtime</c>。
/// </para>
/// </summary>
public class PolyfillBoundaryGuardTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mud.HttpUtils.slnx")))
            {
                dir = dir.Parent;
            }

            dir.Should().NotBeNull("测试应从仓库内运行（需要定位 polyfill 源码与 csproj 做契约断言）");
            return dir!.FullName;
        }
    }

    [Theory]
    // polyfill 文件 → 期望 guard（= 该 API 的 in-box 首个 TFM 之下才定义）
    [InlineData("Mud.HttpUtils.Abstractions/Polyfills/RequiresUnreferencedCodeAttribute.cs", "!NET6_0_OR_GREATER")]
    [InlineData("Mud.HttpUtils.Abstractions/Polyfills/RequiresDynamicCodeAttribute.cs", "!NET7_0_OR_GREATER")]
    public void PolyfillGuard_ShouldMatchInBoxBoundary(string relativePath, string expectedGuard)
    {
        var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"{relativePath} 应存在");

        var content = File.ReadAllText(path);
        content.Should().Contain($"#if {expectedGuard}",
            "guard 必须对齐该 API 的 in-box 首个 TFM："
            + "RequiresUnreferencedCodeAttribute 自 .NET 6 起 in-box、RequiresDynamicCodeAttribute 自 .NET 7 起 in-box；"
            + "锚点取错会在上游 TFM 造成 CS0433/CS0436 双定义冲突");
    }

    /// <summary>
    /// 携带 RequiresDynamicCodeAttribute polyfill 的程序集必须提供 net7.0 资产，
    /// 否则 net7.0 下游会解析到 net6.0 资产并与 BCL 撞名（CS0433）。
    /// </summary>
    [Fact]
    public void AbstractionsPackage_ShouldShipNet70Asset_ToAvoidPolyfillCollision()
    {
        var csproj = Path.Combine(RepoRoot, "Mud.HttpUtils.Abstractions", "Mud.HttpUtils.Abstractions.csproj");
        var content = File.ReadAllText(csproj);

        var tfmsLine = content.Split('\n').First(l => l.Contains("<TargetFrameworks>", StringComparison.Ordinal));
        tfmsLine.Should().Contain("net7.0",
            "Abstractions 在 netstandard2.0/net6.0 资产中携带 RequiresDynamicCodeAttribute polyfill，"
            + "而该 API 自 .NET 7 起 in-box ⇒ 必须发布 net7.0 资产，否则 net7.0 下游解析 net6.0 资产时 CS0433");

        var publicApiDir = Path.Combine(RepoRoot, "Mud.HttpUtils.Abstractions", "PublicAPI", "net7.0");
        Directory.Exists(publicApiDir).Should().BeTrue("net7.0 资产需要配套的公共 API 契约文件（PublicAPI/net7.0/）");
    }
}
