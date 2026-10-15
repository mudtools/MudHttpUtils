// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// U-6：<see cref="TokenScopeKey.ToDelimitedKey"/> / <see cref="TokenScopeKey.TryParseDelimited"/>
/// 分隔符编码键契约 —— 三形态往返、微信现网键形态 golden、段含分隔符 fail-fast、解析负例、与 CompositeKey 不互通。
/// </summary>
public class TokenScopeKeyDelimitedKeyTests
{
    // ── 编码：三形态 ───────────────────────────────────────────────────

    [Theory]
    [InlineData("corp_access_token", "ww-app", "auth-corp-1", "corp_access_token:ww-app:auth-corp-1")]
    [InlineData("suite_token", "suite-app", "suite-77", "suite_token:suite-app:suite-77")]
    public void ToDelimitedKey_ForScope_Should_JoinThreeSegments(string tokenType, string appKey, string scopeKey, string expected)
    {
        TokenScopeKey.ForScope(tokenType, appKey, scopeKey).ToDelimitedKey().Should().Be(expected);
    }

    [Fact]
    public void ToDelimitedKey_ForApp_Should_JoinTwoSegments()
    {
        TokenScopeKey.ForApp("t", "app-1").ToDelimitedKey().Should().Be("t:app-1");
    }

    [Fact]
    public void ToDelimitedKey_ForShared_Should_BeSingleSegment()
    {
        TokenScopeKey.ForShared("t").ToDelimitedKey().Should().Be("t");
    }

    [Fact]
    public void ToDelimitedKey_CustomSeparator_Should_BeApplied()
    {
        TokenScopeKey.ForScope("t", "a", "s").ToDelimitedKey('|').Should().Be("t|a|s");
    }

    // ── 微信现网键形态 golden（方案 U-6 验收项）────────────────────────

    [Fact]
    public void ToDelimitedKey_Should_Match_DownstreamLegacyBuildStoreKeyShape()
    {
        // 微信 BuildStoreKey 历史形态：string.Join(":", tokenType, appKey, authCorpId) —— 逐字节兼容 golden
        var tokenType = "contact_token";
        var appKey = "ww-main";
        var authCorpId = "corp_abc123";

        var golden = $"{tokenType}:{appKey}:{authCorpId}";

        TokenScopeKey.ForScope(tokenType, appKey, authCorpId).ToDelimitedKey(':').Should().Be(golden);
    }

    // ── 编码 fail-fast：段含分隔符 ─────────────────────────────────────

    [Fact]
    public void ToDelimitedKey_SegmentContainsSeparator_Should_Throw()
    {
        var act = () => TokenScopeKey.ForScope("corp:access", "app", "s").ToDelimitedKey();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ToDelimitedKey_AppKeyContainsSeparator_Should_Throw()
    {
        var act = () => TokenScopeKey.ForApp("t", "app:x").ToDelimitedKey();
        act.Should().Throw<InvalidOperationException>("微信 AppKey 形状约束（禁 ':'）在编码侧天然成立");
    }

    [Fact]
    public void ToDelimitedKey_NullSeparator_Should_Throw()
    {
        var act = () => TokenScopeKey.ForShared("t").ToDelimitedKey('\0');
        act.Should().Throw<ArgumentException>();
    }

    // ── 反向解析 ───────────────────────────────────────────────────────

    [Fact]
    public void TryParseDelimited_ThreeSegments_Should_RestoreForScope()
    {
        var ok = TokenScopeKey.TryParseDelimited("corp_access_token:ww-app:auth-corp-1", ':', out var key);

        ok.Should().BeTrue();
        key.TokenType.Should().Be("corp_access_token");
        key.AppKey.Should().Be("ww-app");
        key.ScopeKey.Should().Be("auth-corp-1");
    }

    [Fact]
    public void TryParseDelimited_TwoSegments_Should_RestoreForApp()
    {
        var ok = TokenScopeKey.TryParseDelimited("t:app-1", ':', out var key);

        ok.Should().BeTrue();
        key.TokenType.Should().Be("t");
        key.AppKey.Should().Be("app-1");
        key.ScopeKey.Should().BeNull();
    }

    [Fact]
    public void TryParseDelimited_OneSegment_Should_RestoreForShared()
    {
        var ok = TokenScopeKey.TryParseDelimited("t", ':', out var key);

        ok.Should().BeTrue();
        key.TokenType.Should().Be("t");
        key.AppKey.Should().BeNull();
        key.ScopeKey.Should().BeNull();
    }

    [Fact]
    public void TryParseDelimited_RoundTrip_ThreeForms_Should_PreserveEquality()
    {
        var scope = TokenScopeKey.ForScope("t", "a", "s");
        var app = TokenScopeKey.ForApp("t", "a");
        var shared = TokenScopeKey.ForShared("t");

        TokenScopeKey.TryParseDelimited(scope.ToDelimitedKey(), ':', out var s2).Should().BeTrue();
        TokenScopeKey.TryParseDelimited(app.ToDelimitedKey(), ':', out var a2).Should().BeTrue();
        TokenScopeKey.TryParseDelimited(shared.ToDelimitedKey(), ':', out var sh2).Should().BeTrue();

        s2.Should().Be(scope);
        a2.Should().Be(app);
        sh2.Should().Be(shared);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(":app:s")]
    [InlineData("t::s")]
    [InlineData("t:a:")]
    [InlineData("t:a:s:x")]
    public void TryParseDelimited_InvalidInput_Should_ReturnFalse(string? value)
    {
        var ok = TokenScopeKey.TryParseDelimited(value, ':', out var result);

        ok.Should().BeFalse("空段/超段/null 均无归属语义，miss 不抛异常");
        result.TokenType.Should().BeNull("失败时 result 恒为 default");
    }

    [Fact]
    public void TryParseDelimited_CustomSeparator_Should_OnlySplitOnThatSeparator()
    {
        // ':' 出现在段内容里时（以 '|' 为分隔符编码）不得被误拆
        var key = TokenScopeKey.ForScope("corp:access", "app", "s");

        var ok = TokenScopeKey.TryParseDelimited(key.ToDelimitedKey('|'), '|', out var parsed);

        ok.Should().BeTrue();
        parsed.Should().Be(key);
    }

    // ── 双形态不互通（文档契约）────────────────────────────────────────

    [Fact]
    public void DelimitedKey_And_CompositeKey_Should_NotInteroperate()
    {
        var key = TokenScopeKey.ForScope("t", "a", "s");

        key.ToDelimitedKey().Should().NotBe(key.ToCompositeKey(), "两形态文档钉死不互通");
        TokenScopeKey.TryParseDelimited(key.ToCompositeKey(), ':', out _).Should().BeFalse(
            "长度前缀形态混入分隔符解析必须失败（fail-safe）");
    }
}
