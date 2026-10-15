// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Redis.Tests;

using System.Net;
using Mud.HttpUtils.Redis;
using StackExchange.Redis;

/// <summary>
/// U-7：<see cref="RedisTokenStore"/> 的 <see cref="ITokenStoreCompareExchange"/> CAS 能力 ——
/// 默认 codec 家族 Lua 原子路径（明文比较 / compare-and-delete / PX 形态 / refresh 键 DefaultTtl 同源）、
/// 自定义 codec 非原子回退路径、构造守卫。
/// </summary>
public class RedisTokenStoreCompareExchangeTests
{
    private static (RedisTokenStore Store, Mock<IDatabase> Db) CreateStore(
        RedisTokenStoreOptions? options = null,
        ITokenStoreCodec? codec = null)
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var connection = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);

        var store = new RedisTokenStore(connection.Object, options ?? new RedisTokenStoreOptions(), codec);
        return (store, db);
    }

    [Fact]
    public async Task CompareExchangeAccessTokenAsync_Match_Should_EvaluateScript_WithPlaintextAndEncodedNew()
    {
        var (store, db) = CreateStore();
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        var matched = await store.CompareExchangeAccessTokenAsync("T1", "old-token", "new-token", 7200);

        matched.Should().BeTrue();
        db.Verify(d => d.ScriptEvaluateAsync(
            It.Is<string>(s => s.Contains("string.find") && s.Contains("'DEL'")),
            It.Is<RedisKey[]>(ks => ks.Length == 1 && (string)ks[0]! == "mud:httputils:T1:shared:access"),
            It.Is<RedisValue[]>(vals => vals.Length == 3
                && (string)vals[0]! == "old-token"                            // 明文比较契约
                && (string)vals[1]! == "7200|new-token"                       // 新值经 codec 编码
                && (string)vals[2]! == "7200000"),                            // PX 毫秒
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task CompareExchangeAccessTokenAsync_Mismatch_Should_ReturnFalse()
    {
        var (store, db) = CreateStore();
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(0));

        var matched = await store.CompareExchangeAccessTokenAsync("T1", "expected", "new", 60);

        matched.Should().BeFalse("脚本返回 0 = 键不存在或明文不匹配（未做任何写入）");
    }

    [Fact]
    public async Task CompareExchangeAccessTokenAsync_Delete_Should_PassEmptyNewValue()
    {
        var (store, db) = CreateStore();
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        var matched = await store.CompareExchangeAccessTokenAsync("T1", "old-token", null);

        matched.Should().BeTrue("compare-and-delete 语义");
        db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.Is<RedisValue[]>(vals => vals.Length == 3
                && (string)vals[1]! == string.Empty
                && (string)vals[2]! == string.Empty),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task CompareExchangeAccessTokenAsync_NoExpiry_Should_PassEmptyPx()
    {
        var (store, db) = CreateStore();
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        await store.CompareExchangeAccessTokenAsync("T1", "old", "new", 0);

        db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.Is<RedisValue[]>(vals => (string)vals[2]! == string.Empty),
            It.IsAny<CommandFlags>()), Times.Once, "expiresInSeconds ≤ 0 不设 TTL（C1 宁缺勿滥）");
    }

    [Fact]
    public async Task CompareExchangeRefreshTokenAsync_Should_UseRefreshKey_AndDefaultTtl()
    {
        var (store, db) = CreateStore(new RedisTokenStoreOptions { DefaultTtl = TimeSpan.FromDays(7) });
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        var matched = await store.CompareExchangeRefreshTokenAsync("T1", "old-refresh", "new-refresh");

        matched.Should().BeTrue();
        db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.Is<RedisKey[]>(ks => (string)ks[0]! == "mud:httputils:T1:shared:refresh"),
            It.Is<RedisValue[]>(vals => vals.Length == 3
                && (string)vals[1]! == "0|new-refresh"
                && (string)vals[2]! == "604800000"),                          // DefaultTtl 毫秒（与 SetRefreshTokenAsync 同源）
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task CompareExchangeRefreshTokenAsync_WithoutDefaultTtl_Should_PassEmptyPx()
    {
        var (store, db) = CreateStore();
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        await store.CompareExchangeRefreshTokenAsync("T1", "old", "new");

        db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.Is<RedisValue[]>(vals => (string)vals[2]! == string.Empty),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    // ── 非默认 codec：非原子回退路径 ───────────────────────────────────

    /// <summary>自定义 codec（非默认家族形态，触发回退）。</summary>
    private sealed class CustomCodec : ITokenStoreCodec
    {
        public string? Encode(string? token, long expiresInSeconds)
            => string.IsNullOrEmpty(token) ? null : $"{expiresInSeconds}#custom#{token}";

        public (string? Token, long ExpiresInSeconds)? Decode(string? encoded)
        {
            if (string.IsNullOrEmpty(encoded))
                return null;
            var first = encoded.IndexOf('#');
            var second = encoded.IndexOf('#', first + 1);
            if (first < 0 || second < 0 || !long.TryParse(encoded.Substring(0, first), out var seconds))
                return null;
            var token = encoded.Substring(second + 1);
            return (token.Length == 0 ? null : token, seconds);
        }
    }

    [Fact]
    public async Task CompareExchange_WithCustomCodec_Should_FallBackToNonAtomicPath_MatchAndSet()
    {
        var (store, db) = CreateStore(codec: new CustomCodec());
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"3600#custom#old-token");
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var matched = await store.CompareExchangeAccessTokenAsync("T1", "old-token", "new-token", 60);

        matched.Should().BeTrue();
        db.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
        db.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.Is<RedisValue>(v => (string)v! == "60#custom#new-token"),
            It.Is<TimeSpan?>(t => t == TimeSpan.FromSeconds(60)),
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        db.Verify(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()), Times.Never, "自定义 codec 不得走 Lua 原子路径（服务端无法执行任意 codec）");
    }

    [Fact]
    public async Task CompareExchange_WithCustomCodec_DeleteMatch_Should_DeleteKey()
    {
        var (store, db) = CreateStore(codec: new CustomCodec());
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"3600#custom#old-token");
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var matched = await store.CompareExchangeRefreshTokenAsync("T1", "old-token", null);

        matched.Should().BeTrue();
        db.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task CompareExchange_WithCustomCodec_Mismatch_Should_NotWrite()
    {
        var (store, db) = CreateStore(codec: new CustomCodec());
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"3600#custom#different");

        var matched = await store.CompareExchangeAccessTokenAsync("T1", "old-token", "new-token", 60);

        matched.Should().BeFalse();
        db.Verify(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Never);
        db.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    // ── 构造守卫 ───────────────────────────────────────────────────────

    [Fact]
    public async Task CompareExchange_EmptyExpectedToken_Should_Throw()
    {
        var (store, _) = CreateStore();

        var act = async () => await store.CompareExchangeAccessTokenAsync("T1", "", "new", 60);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CompareExchange_EmptyTokenType_Should_Throw()
    {
        var (store, _) = CreateStore();

        var act = async () => await store.CompareExchangeRefreshTokenAsync("", "old", "new");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
