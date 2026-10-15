// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷与责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;
using Mud.HttpUtils.Redis;
using StackExchange.Redis;

namespace Mud.HttpUtils.Redis.Tests;

/// <summary>
/// F-3：<see cref="RedisTokenStore"/> 读写行为的钉死测试（mock IConnectionMultiplexer/IDatabase/IServer）。
/// </summary>
/// <remarks>
/// 钉死四件事：写穿键形（经 <see cref="RedisTokenStoreKeyBuilder"/>）、墓碑删除、TTL 推导（C1 宁缺勿滥）、
/// SCAN 枚举只取本命名空间。真实 Redis 集成测试按方案 F-3 以环境变量门控（可跳过），不在本文件。
/// </remarks>
public class RedisTokenStoreTests
{
    private const string Prefix = "mud:httputils";

    private static (RedisTokenStore Store, Mock<IDatabase> Db, Mock<IServer> Server) CreateStore(
        RedisTokenStoreOptions? options = null)
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var server = new Mock<IServer>(MockBehavior.Strict);
        server.Setup(s => s.IsConnected).Returns(true);

        var connection = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        connection.Setup(c => c.GetEndPoints(It.IsAny<bool>()))
            .Returns(new EndPoint[] { new IPEndPoint(IPAddress.Loopback, 6379) });
        connection.Setup(c => c.GetServer(It.IsAny<EndPoint>(), It.IsAny<object>())).Returns(server.Object);

        var store = new RedisTokenStore(connection.Object, options ?? new RedisTokenStoreOptions());
        return (store, db, server);
    }

    [Fact]
    public async Task SetAccessTokenAsync_ShouldWriteEncodedValue_AtAccessKey_WithDerivedTtl()
    {
        var (store, db, _) = CreateStore();

        RedisKey? writtenKey = null;
        RedisValue writtenValue = default;
        TimeSpan? writtenTtl = null;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((k, v, ttl, _, _, _) =>
            {
                writtenKey = k;
                writtenValue = v;
                writtenTtl = ttl;
            })
            .ReturnsAsync(true);

        await store.SetAccessTokenAsync("Wechat.AccessToken", "tok-1", 7200);

        writtenKey.Should().Be((RedisKey)"mud:httputils:Wechat.AccessToken:shared:access");
        writtenValue.Should().Be((RedisValue)"7200|tok-1", "默认 codec 形态 {expiresInSeconds}|{token}");
        writtenTtl.Should().Be(TimeSpan.FromSeconds(7200), "expiresInSeconds > 0 时 TTL 与写入方过期信息同源（C1）");
        db.Verify(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        db.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetAccessTokenAsync_WhenNoExpiryInfo_ShouldFallBackToDefaultTtl()
    {
        var (store, db, _) = CreateStore(new RedisTokenStoreOptions
        {
            DefaultTtl = TimeSpan.FromHours(6),
            AppKey = "app-a",
        });

        TimeSpan? writtenTtl = null;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((_, _, ttl, _, _, _) => writtenTtl = ttl)
            .ReturnsAsync(true);

        await store.SetAccessTokenAsync("T1", "tok-1", 0);

        writtenTtl.Should().Be(TimeSpan.FromHours(6), "无过期信息时回落 DefaultTtl");
    }

    [Fact]
    public async Task SetAccessTokenAsync_WhenNoExpiryAndNoDefaultTtl_ShouldWriteWithoutTtl()
    {
        var (store, db, _) = CreateStore();   // DefaultTtl = null

        TimeSpan? writtenTtl = TimeSpan.MinValue;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((_, _, ttl, _, _, _) => writtenTtl = ttl)
            .ReturnsAsync(true);

        await store.SetAccessTokenAsync("T1", "tok-1", 0);

        writtenTtl.Should().BeNull("C1 宁缺勿滥：无过期信息且 DefaultTtl 未配置时不设 TTL");
    }

    [Fact]
    public async Task SetAccessTokenAsync_WhenTokenEmpty_ShouldDeleteKeyInsteadOfWriting()
    {
        var (store, db, _) = CreateStore();

        db.Setup(d => d.KeyDeleteAsync((RedisKey)"mud:httputils:T1:shared:access", It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await store.SetAccessTokenAsync("T1", "", 0);

        // 墓碑语义：Encode 返回 null → 删除键，不写空值（Strict mock 未配 StringSetAsync，写入即失败）。
        db.Verify(d => d.KeyDeleteAsync((RedisKey)"mud:httputils:T1:shared:access", It.IsAny<CommandFlags>()), Times.Once);
        db.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetRefreshTokenAsync_ShouldWriteRefreshKey_WithDefaultTtlAndNoExpiryInfo()
    {
        var (store, db, _) = CreateStore(new RedisTokenStoreOptions
        {
            DefaultTtl = TimeSpan.FromDays(30),
            AppKey = "app-a",
        });

        RedisKey? writtenKey = null;
        RedisValue writtenValue = default;
        TimeSpan? writtenTtl = null;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((k, v, ttl, _, _, _) =>
            {
                writtenKey = k;
                writtenValue = v;
                writtenTtl = ttl;
            })
            .ReturnsAsync(true);

        await store.SetRefreshTokenAsync("T1", "refresh-1");

        writtenKey.Should().Be((RedisKey)"mud:httputils:T1:app-a:refresh");
        writtenValue.Should().Be((RedisValue)"0|refresh-1", "刷新令牌无过期信息（expiresInSeconds = 0）");
        writtenTtl.Should().Be(TimeSpan.FromDays(30), "refresh 键 TTL = DefaultTtl");
    }

    [Fact]
    public async Task SetRefreshTokenAsync_WhenTokenEmpty_ShouldDeleteKey()
    {
        var (store, db, _) = CreateStore();

        db.Setup(d => d.KeyDeleteAsync((RedisKey)"mud:httputils:T1:shared:refresh", It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await store.SetRefreshTokenAsync("T1", null!);

        db.Verify(d => d.KeyDeleteAsync((RedisKey)"mud:httputils:T1:shared:refresh", It.IsAny<CommandFlags>()), Times.Once);
        db.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldDecodeStoredValue()
    {
        var (store, db, _) = CreateStore();

        db.Setup(d => d.StringGetAsync((RedisKey)"mud:httputils:T1:shared:access", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"7200|tok-1");

        (await store.GetAccessTokenAsync("T1")).Should().Be("tok-1");
    }

    [Fact]
    public async Task GetRefreshTokenAsync_ShouldDecodeStoredValue()
    {
        var (store, db, _) = CreateStore();

        db.Setup(d => d.StringGetAsync((RedisKey)"mud:httputils:T1:shared:refresh", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"0|refresh-1");

        (await store.GetRefreshTokenAsync("T1")).Should().Be("refresh-1");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenKeyMissing_ShouldReturnNull()
    {
        var (store, db, _) = CreateStore();

        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        (await store.GetAccessTokenAsync("T1")).Should().BeNull();
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenPayloadMalformed_ShouldReturnNullNotThrow()
    {
        var (store, db, _) = CreateStore();

        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"not-a-valid-payload");   // 无 '|' 分隔符：codec 契约返回 null

        (await store.GetAccessTokenAsync("T1")).Should().BeNull("ITokenStoreCodec.Decode 不得抛异常，非法格式按缺失处理");
    }

    [Fact]
    public async Task RemoveAsync_ShouldDeleteBothAccessAndRefreshKeys()
    {
        var (store, db, _) = CreateStore(new RedisTokenStoreOptions { AppKey = "app-a" });

        db.Setup(d => d.KeyDeleteAsync(
                It.Is<RedisKey[]>(ks => ks.Length == 2
                    && (string)ks[0]! == "mud:httputils:T1:app-a:access"
                    && (string)ks[1]! == "mud:httputils:T1:app-a:refresh"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        await store.RemoveAsync("T1");

        db.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(ks => ks.Length == 2
                && (string)ks[0]! == "mud:httputils:T1:app-a:access"
                && (string)ks[1]! == "mud:httputils:T1:app-a:refresh"),
            It.IsAny<CommandFlags>()), Times.Once);
        db.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetTokenTypesAsync_ShouldParseDistinctTokenTypesFromAccessKeys()
    {
        var (store, _, server) = CreateStore(new RedisTokenStoreOptions { AppKey = "app-a" });

        server.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(new RedisKey[]
            {
                "mud:httputils:T1:app-a:access",
                "mud:httputils:T2:app-a:access",
                "mud:httputils:T2:app-a:access",     // 重复（多端点 SCAN 去重）
                "mud:httputils:T1:app-a:refresh",    // 字段判别失败
                "mud:httputils:T3:app-b:access",     // 异 appKey（多应用隔离）
                "other:prefix:T9:app-a:access",      // 异前缀
            });

        var tokenTypes = (await store.GetTokenTypesAsync()).ToArray();

        tokenTypes.Should().BeEquivalentTo(new[] { "T1", "T2" });
        server.Verify(s => s.Keys(
            0,
            It.Is<RedisValue>(v => (string)v! == "mud:httputils:*:app-a:access"),
            It.IsAny<int>(),
            It.IsAny<long>(),
            It.IsAny<int>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetTokenTypesAsync_WhenDatabaseNegative_ShouldScanDbZero()
    {
        var (store, _, server) = CreateStore(new RedisTokenStoreOptions { Database = -1 });

        server.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(Array.Empty<RedisKey>());

        (await store.GetTokenTypesAsync()).Should().BeEmpty();

        server.Verify(s => s.Keys(
            0,                                   // -1 → 0（Redis 集群仅 db0 可用）
            It.IsAny<RedisValue>(),
            It.IsAny<int>(),
            It.IsAny<long>(),
            It.IsAny<int>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task ClearAsync_ShouldDeleteOnlyOwnNamespaceKeys()
    {
        var (store, db, server) = CreateStore(new RedisTokenStoreOptions { AppKey = "app-a" });

        // 两遍 SCAN 分别对应 access / refresh 模式，按模式返回各自的键。
        server.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns((int _, RedisValue pattern, int _, long _, int _, CommandFlags _) =>
                (string)pattern! == "mud:httputils:*:app-a:access"
                    ? new RedisKey[] { "mud:httputils:T1:app-a:access" }
                    : new RedisKey[] { "mud:httputils:T1:app-a:refresh" });

        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        await store.ClearAsync();

        db.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(ks => ks.Length == 2
                && (string)ks[0]! == "mud:httputils:T1:app-a:access"
                && (string)ks[1]! == "mud:httputils:T1:app-a:refresh"),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task GetTokenTypesAsync_WhenEndpointUnreachable_ShouldSkipAndContinue()
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var connection = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        connection.Setup(c => c.GetEndPoints(It.IsAny<bool>()))
            .Returns(new EndPoint[] { new IPEndPoint(IPAddress.Loopback, 6379) });
        connection.Setup(c => c.GetServer(It.IsAny<EndPoint>(), It.IsAny<object>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var store = new RedisTokenStore(connection.Object);

        // SCAN 非热路径：端点不可达按"部分可见"容忍，不得抛异常。
        (await store.GetTokenTypesAsync()).Should().BeEmpty();
    }
}
