// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// G10：<see cref="AppKeyAllowListAuthorizer"/> —— 库内首个
/// <see cref="IAppAccessAuthorizer"/> 内建实现（按 appKey 白名单授权）。
/// </summary>
public class AppKeyAllowListAuthorizerTests
{
    [Fact]
    public void AllowList_ContainsAppKey_ShouldAllow()
    {
        var authorizer = new AppKeyAllowListAuthorizer(new[] { "app-A", "app-B" });

        authorizer.CanSwitchTo("app-A").Should().BeTrue();
        authorizer.CanSwitchTo("app-B").Should().BeTrue();
    }

    [Fact]
    public void AllowList_MissingAppKey_ShouldDeny()
    {
        var authorizer = new AppKeyAllowListAuthorizer(new[] { "app-A" });

        authorizer.CanSwitchTo("app-C").Should().BeFalse("跨 appKey 越权请求必须被拒（安全用例）");
    }

    [Fact]
    public void AllowList_ShouldBeCaseSensitive()
    {
        // 与 IAppManager<TAppContext> 的既有契约一致：appKey 区分大小写。
        var authorizer = new AppKeyAllowListAuthorizer(new[] { "App-A" });

        authorizer.CanSwitchTo("App-A").Should().BeTrue();
        authorizer.CanSwitchTo("app-a").Should().BeFalse();
    }

    [Fact]
    public void AllowList_NullOrBlankAppKey_ShouldDeny()
    {
        var authorizer = new AppKeyAllowListAuthorizer(new[] { "app-A" });

        authorizer.CanSwitchTo(null!).Should().BeFalse("fail-closed");
        authorizer.CanSwitchTo("").Should().BeFalse();
        authorizer.CanSwitchTo("   ").Should().BeFalse();
    }

    [Fact]
    public void AllowList_NullOrEmptySource_ShouldDenyAll()
    {
        new AppKeyAllowListAuthorizer((IEnumerable<string>?)null).CanSwitchTo("app-A").Should().BeFalse();
        new AppKeyAllowListAuthorizer(Array.Empty<string>()).CanSwitchTo("app-A").Should().BeFalse();
    }

    [Fact]
    public void AllowList_ShouldIgnoreNullAndBlankEntries()
    {
        var authorizer = new AppKeyAllowListAuthorizer(new[] { null!, "", "  ", "app-A" });

        authorizer.CanSwitchTo("app-A").Should().BeTrue();
        authorizer.CanSwitchTo("").Should().BeFalse();
    }

    [Fact]
    public void FromPredicate_ShouldDelegate()
    {
        var authorizer = AppKeyAllowListAuthorizer.FromPredicate(
            appKey => appKey.StartsWith("tenant-1:", StringComparison.Ordinal));

        authorizer.CanSwitchTo("tenant-1:app-A").Should().BeTrue();
        authorizer.CanSwitchTo("tenant-2:app-A").Should().BeFalse("其它租户的 appKey 必须被拒");
    }

    [Fact]
    public void FromPredicate_NullPredicate_Throws()
    {
        var act = () => AppKeyAllowListAuthorizer.FromPredicate(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
    }
}
