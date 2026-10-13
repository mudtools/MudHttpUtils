// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷与责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// F-7（G-7/TK-1）<see cref="TokenDependencyGraph"/> 专项测试：
/// 依赖声明语义（"A 由 B 派生" ⇒ B 失效连带 A 失效）、反向 BFS 传递闭包、
/// 互依环终止、以及 <c>42001 → suite_access_token → 重注入</c> 链式场景钉死。
/// </summary>
public class TokenDependencyGraphTests
{
    // ── Add 声明语义 ──────────────────────────────────────────────────

    [Fact]
    public void Add_ShouldBeChainable_AndRegisterBothEnds()
    {
        var graph = new TokenDependencyGraph();

        var returned = graph.Add("corp_access_token", "suite_access_token")
                            .Add("suite_access_token", "provider_access_token");

        returned.Should().BeSameAs(graph, "Add 应支持链式调用并返回自身");
        graph.DeclaredTokenTypes.Should().BeEquivalentTo(
            new[] { "corp_access_token", "suite_access_token", "provider_access_token" },
            "两端（依赖者与被依赖者）都应登记进节点集");
    }

    [Fact]
    public void Add_ShouldDeduplicateRepeatedDependencyDeclaration()
    {
        var graph = new TokenDependencyGraph();
        graph.Add("a", "b").Add("a", "b");

        graph.GetDependentsOf("b").Should().Equal(new[] { "a" }, "重复声明不应产生重复依赖者");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_ShouldRejectBlankTokenType(string? tokenType)
    {
        var graph = new TokenDependencyGraph();

        var act = () => graph.Add(tokenType!, "dep");

        act.Should().Throw<ArgumentException>("令牌类型不得为空白");
    }

    [Fact]
    public void Add_ShouldRejectNullOrEmptyDependencies()
    {
        var graph = new TokenDependencyGraph();

        var nullAct = () => graph.Add("a", null!);
        var emptyAct = () => graph.Add("a");

        nullAct.Should().Throw<ArgumentException>("依赖数组不得为 null");
        emptyAct.Should().Throw<ArgumentException>("依赖数组不得为空");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Add_ShouldRejectBlankDependencyElement(string? dependency)
    {
        var graph = new TokenDependencyGraph();

        var act = () => graph.Add("a", dependency!);

        act.Should().Throw<ArgumentException>("依赖元素不得为空白");
    }

    [Fact]
    public void Add_ShouldRejectSelfDependency()
    {
        var graph = new TokenDependencyGraph();

        var act = () => graph.Add("corp_access_token", "x", "corp_access_token");

        act.Should().Throw<ArgumentException>("自依赖（A → A）应被拒绝");
    }

    // ── GetDependentsOf 传递闭包 ─────────────────────────────────────

    [Fact]
    public void GetDependentsOf_UnknownOrLeafType_ReturnsEmptyNotNull()
    {
        var graph = new TokenDependencyGraph().Add("corp_access_token", "suite_access_token");

        var unknown = graph.GetDependentsOf("no_such_type");
        var leaf = graph.GetDependentsOf("corp_access_token");   // 叶子（无人由它派生）

        unknown.Should().NotBeNull().And.BeEmpty();
        leaf.Should().NotBeNull().And.BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void GetDependentsOf_ShouldRejectBlankTokenType(string? tokenType)
    {
        var graph = new TokenDependencyGraph();

        var act = () => graph.GetDependentsOf(tokenType!);

        act.Should().Throw<ArgumentException>("令牌类型不得为空白");
    }

    /// <summary>
    /// 42001 链式场景钉死：企业微信链式令牌
    /// <c>provider_access_token → suite_access_token → corp_access_token</c>（换码链），
    /// 依赖语义"由……派生" ⇒ 被依赖者失效时依赖者连带失效。
    /// </summary>
    [Fact]
    public void GetDependentsOf_Wechat42001Chain_ShouldReturnTransitiveClosureNearToFar()
    {
        var graph = new TokenDependencyGraph()
            .Add("corp_access_token", "suite_access_token")       // corp 由 suite 换码派生
            .Add("suite_access_token", "provider_access_token");  // suite 由 provider 换码派生

        // provider 失效（如 42001）⇒ suite 连带失效 ⇒ corp 连带失效
        graph.GetDependentsOf("provider_access_token")
             .Should().Equal(new[] { "suite_access_token", "corp_access_token" }, "依赖距离由近及远、传递闭包全量返回");

        graph.GetDependentsOf("suite_access_token")
             .Should().Equal(new[] { "corp_access_token" }, "直接依赖者在列");

        graph.GetDependentsOf("corp_access_token")
             .Should().BeEmpty("corp 是派生链末端，其失效不反向传播");
    }

    [Fact]
    public void GetDependentsOf_MutualDependency_ShouldTerminateAndExcludeSelf()
    {
        // 互依（A ↔ B）不禁止，但查询必须终止且不把源头自己算进依赖者。
        var graph = new TokenDependencyGraph()
            .Add("A", "B")
            .Add("B", "A");

        var dependentsOfA = graph.GetDependentsOf("A");
        var dependentsOfB = graph.GetDependentsOf("B");

        dependentsOfA.Should().Equal(new[] { "B" }, "访问集合保证互依环终止");
        dependentsOfB.Should().Equal(new[] { "A" });
        dependentsOfA.Should().NotContain("A", "失效源头不应出现在自己的依赖者集合里");
    }

    [Fact]
    public void GetDependentsOf_DiamondDependency_ShouldDeduplicateAndKeepNearToFarOrder()
    {
        // d ← b, c ← a（菱形）：b、c 同层（按声明序），a 远层且只出现一次。
        var graph = new TokenDependencyGraph()
            .Add("a", "b", "c")
            .Add("b", "d")
            .Add("c", "d");

        graph.GetDependentsOf("d").Should().Equal(new[] { "b", "c", "a" }, "同层按声明序、远层去重合并");
    }

    // ── Empty 单例 ───────────────────────────────────────────────────

    [Fact]
    public void Empty_ShouldBeSingletonAndReportEmpty()
    {
        var empty = TokenDependencyGraph.Empty;

        empty.IsEmpty.Should().BeTrue("未声明任何依赖的图为空");
        empty.DeclaredTokenTypes.Should().BeEmpty();
        empty.GetDependentsOf("anything").Should().BeEmpty();
        TokenDependencyGraph.Empty.Should().BeSameAs(empty, "Empty 是静态单例");
    }

    [Fact]
    public void NonEmptyGraph_ShouldReportNotEmpty()
    {
        var graph = new TokenDependencyGraph().Add("a", "b");

        graph.IsEmpty.Should().BeFalse("声明过依赖后不应再报告为空");
    }
}
