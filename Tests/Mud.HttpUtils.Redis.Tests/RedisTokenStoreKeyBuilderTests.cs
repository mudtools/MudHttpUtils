// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Redis;

namespace Mud.HttpUtils.Redis.Tests;

/// <summary>
/// F-3：Redis 令牌键布局（<see cref="RedisTokenStoreKeyBuilder"/>）的钉死测试。
/// </summary>
/// <remarks>
/// 键形态 <c>{prefix}:{tokenType}:{appKey}:access|refresh</c> 是持久化数据的兼容性契约
/// —— 一旦上线即不可变更（否则旧数据不可读），故此处逐字符钉死。
/// </remarks>
public class RedisTokenStoreKeyBuilderTests
{
    [Fact]
    public void BuildAccessKey_ShouldMatchDocumentedShape()
    {
        // 方案 F-3 钉死基形：mud:httputils:{tokenType}:{appKey}（+ :access 判别后缀）
        RedisTokenStoreKeyBuilder.BuildAccessKey("mud:httputils", "Wechat.AccessToken", "app-a")
            .Should().Be("mud:httputils:Wechat.AccessToken:app-a:access");
    }

    [Fact]
    public void BuildRefreshKey_ShouldMatchDocumentedShape()
    {
        RedisTokenStoreKeyBuilder.BuildRefreshKey("mud:httputils", "Wechat.AccessToken", "app-a")
            .Should().Be("mud:httputils:Wechat.AccessToken:app-a:refresh");
    }

    [Fact]
    public void BuildKey_WhenAppKeyIsBlank_ShouldUseSharedSegment()
    {
        RedisTokenStoreKeyBuilder.BuildAccessKey("mud:httputils", "T1", null)
            .Should().Be("mud:httputils:T1:shared:access");
        RedisTokenStoreKeyBuilder.BuildAccessKey("mud:httputils", "T1", "  ")
            .Should().Be("mud:httputils:T1:shared:access", "空白 appKey 同样取共享命名空间");
    }

    [Fact]
    public void BuildKey_WhenTokenTypeContainsSeparator_ShouldRoundtrip()
    {
        // tokenType 允许含 ':'：解析从右侧剥除固定段，天然单射（不需转义）。
        var key = RedisTokenStoreKeyBuilder.BuildAccessKey("mud:httputils", "a:b:c", "app-a");
        key.Should().Be("mud:httputils:a:b:c:app-a:access");
        RedisTokenStoreKeyBuilder.TryParseTokenType("mud:httputils", "app-a", key).Should().Be("a:b:c");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryParseTokenType_ShouldTreatBlankAppKeyAsShared(string? appKey)
    {
        var key = RedisTokenStoreKeyBuilder.BuildAccessKey("mud:httputils", "T1", appKey);
        RedisTokenStoreKeyBuilder.TryParseTokenType("mud:httputils", appKey, key).Should().Be("T1");
    }

    [Theory]
    [InlineData("other:prefix:T1:app-a:access")]       // 异前缀
    [InlineData("mud:httputils:T1:app-b:access")]      // 异 appKey（多应用隔离）
    [InlineData("mud:httputils:T1:app-a:refresh")]     // 字段判别失败
    [InlineData("mud:httputils:app-a:access")]         // 段数不足
    [InlineData("")]
    public void TryParseTokenType_ForeignKey_ShouldReturnNull(string key)
    {
        RedisTokenStoreKeyBuilder.TryParseTokenType("mud:httputils", "app-a", key).Should().BeNull();
    }

    [Fact]
    public void Patterns_ShouldCoverNamespaceOnly()
    {
        RedisTokenStoreKeyBuilder.BuildAccessPattern("mud:httputils", "app-a")
            .Should().Be("mud:httputils:*:app-a:access", "枚举只扫本 appKey 命名空间");
        RedisTokenStoreKeyBuilder.BuildAllPattern("mud:httputils")
            .Should().Be("mud:httputils:*:*:*");
    }

    [Fact]
    public void BuildKey_EmptyPrefixOrTokenType_ShouldThrow()
    {
        var act1 = () => RedisTokenStoreKeyBuilder.BuildAccessKey("", "T1", null);
        act1.Should().Throw<ArgumentException>();

        var act2 = () => RedisTokenStoreKeyBuilder.BuildAccessKey("p", "", null);
        act2.Should().Throw<ArgumentException>();
    }
}
