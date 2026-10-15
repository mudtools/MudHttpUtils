// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;
using Mud.HttpUtils.Redis;
using StackExchange.Redis;

namespace Mud.HttpUtils.Tests.Redis;

/// <summary>
/// U-4：<see cref="RedisTokenStore"/> 的 <see cref="ITokenStoreAppPurge"/> 能力面 ——
/// 按 appKey 精确枚举/清库（不误伤其他 appKey、tokenType 含 ':'、共享段口径）、
/// ClearAsync 复用便捷形态、<see cref="TokenStorePurgeExtensions.PurgeAppTokensAsync"/> 探测与回退路径。
/// </summary>
public class RedisTokenStoreAppPurgeTests
{
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

    private static void SetupScanByPattern(Mock<IServer> server, Func<string, RedisKey[]> byPattern)
        => server.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(),
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns((int _, RedisValue pattern, int _, long _, int _, CommandFlags _) => byPattern((string)pattern!));

    // ── EnumerateKeysAsync ─────────────────────────────────────────────

    [Fact]
    public async Task EnumerateKeysAsync_Should_Return_AccessAndRefreshKeys_ForGivenAppKey()
    {
        var (store, _, server) = CreateStore(new RedisTokenStoreOptions { AppKey = "other-app" });

        SetupScanByPattern(server, pattern => (string)pattern switch
        {
            "mud:httputils:*:app-a:access" => new RedisKey[]
            {
                "mud:httputils:T1:app-a:access",
                "mud:httputils:Wechat.AccessToken:app-a:access",   // tokenType 含 ':'
            },
            "mud:httputils:*:app-a:refresh" => new RedisKey[] { "mud:httputils:T1:app-a:refresh" },
            _ => Array.Empty<RedisKey>(),
        });

        var keys = await store.EnumerateKeysAsync("app-a");

        keys.Should().BeEquivalentTo(new[]
        {
            "mud:httputils:T1:app-a:access",
            "mud:httputils:Wechat.AccessToken:app-a:access",
            "mud:httputils:T1:app-a:refresh",
        });
    }

    [Fact]
    public async Task EnumerateKeysAsync_EmptyAppKey_Should_UseSharedSegment()
    {
        var (store, _, server) = CreateStore();

        SetupScanByPattern(server, pattern => (string)pattern switch
        {
            "mud:httputils:*:shared:access" => new RedisKey[] { "mud:httputils:T1:shared:access" },
            _ => Array.Empty<RedisKey>(),
        });

        var keys = await store.EnumerateKeysAsync("");

        keys.Should().BeEquivalentTo(new[] { "mud:httputils:T1:shared:access" },
            "null/空白 appKey 按共享命名空间约定处置");
    }

    // ── PurgeAppAsync：精确清库不误伤 ──────────────────────────────────

    [Fact]
    public async Task PurgeAppAsync_Should_DeleteOnlyGivenAppKeys_And_ReturnCount()
    {
        var (store, db, server) = CreateStore(new RedisTokenStoreOptions { AppKey = "other-app" });

        SetupScanByPattern(server, pattern => (string)pattern switch
        {
            "mud:httputils:*:app-a:access" => new RedisKey[] { "mud:httputils:T1:app-a:access" },
            "mud:httputils:*:app-a:refresh" => new RedisKey[] { "mud:httputils:T1:app-a:refresh" },
            _ => Array.Empty<RedisKey>(),   // 异 appKey 的键不在本 appKey 模式下，天然不命中
        });
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        var purged = await store.PurgeAppAsync("app-a");

        purged.Should().Be(2);
        db.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(ks => ks.Length == 2
                && ks.All(k => ((string)k!).EndsWith(":app-a:access") || ((string)k!).EndsWith(":app-a:refresh"))),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task PurgeAppAsync_WhenNoKeys_Should_NotSendDeleteCommand()
    {
        var (store, db, server) = CreateStore();
        SetupScanByPattern(server, _ => Array.Empty<RedisKey>());

        var purged = await store.PurgeAppAsync("app-empty");

        purged.Should().Be(0);
        db.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()), Times.Never,
            "键集为空时不发 DEL 命令");
    }

    [Fact]
    public async Task ClearAsync_Should_PurgeInstanceAppKeyNamespace()
    {
        var (store, db, server) = CreateStore(new RedisTokenStoreOptions { AppKey = "app-a" });

        SetupScanByPattern(server, pattern => (string)pattern switch
        {
            "mud:httputils:*:app-a:access" => new RedisKey[] { "mud:httputils:T1:app-a:access" },
            "mud:httputils:*:app-a:refresh" => new RedisKey[] { "mud:httputils:T1:app-a:refresh" },
            _ => Array.Empty<RedisKey>(),
        });
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        await store.ClearAsync();

        db.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(ks => ks.Length == 2),
            It.IsAny<CommandFlags>()), Times.Once,
            "ClearAsync ≡ PurgeAppAsync(本实例 AppKey) 便捷形态（行为与 U-4 前逐位一致）");
    }

    // ── PurgeAppTokensAsync 统一入口：探测与回退 ───────────────────────

    /// <summary>仅实现 ITokenStore 的探测替身（回退路径消费）。</summary>
    private class PlainTokenStore : ITokenStore
    {
        public List<string> RemovedTokenTypes { get; } = new();

        public Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
        {
            RemovedTokenTypes.Add(tokenType);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> GetTokenTypesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<string>>(new[] { "T1", "T2" });

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>能力探测替身：验证入口优先走 ITokenStoreAppPurge。</summary>
    private sealed class PurgeCapableStore(int purgeResult) : PlainTokenStore, ITokenStoreAppPurge
    {
        public string? PurgedAppKey { get; private set; }

        public Task<IReadOnlyCollection<string>> EnumerateKeysAsync(string appKey, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());

        public Task<int> PurgeAppAsync(string appKey, CancellationToken cancellationToken = default)
        {
            PurgedAppKey = appKey;
            return Task.FromResult(purgeResult);
        }
    }

    [Fact]
    public async Task PurgeAppTokensAsync_Should_Probe_Capability_Interface_First()
    {
        var store = new PurgeCapableStore(7);

        var purged = await TokenStorePurgeExtensions.PurgeAppTokensAsync(store, "app-a");

        purged.Should().Be(7);
        store.PurgedAppKey.Should().Be("app-a");
        store.RemovedTokenTypes.Should().BeEmpty("探测命中时不得走逐键回退");
    }

    [Fact]
    public async Task PurgeAppTokensAsync_WithoutCapability_Should_FallBackToPerTypeRemove()
    {
        var store = new PlainTokenStore();

        var purged = await TokenStorePurgeExtensions.PurgeAppTokensAsync(store, "app-a");

        purged.Should().Be(2, "回退路径：每 tokenType 删除一次（RemoveAsync 含 access+refresh 双键）");
        store.RemovedTokenTypes.Should().BeEquivalentTo(new[] { "T1", "T2" });
    }

    [Fact]
    public async Task PurgeAppTokensAsync_Should_Work_AsExtension_OnRedisStore()
    {
        var (store, db, server) = CreateStore();
        SetupScanByPattern(server, pattern => (string)pattern switch
        {
            "mud:httputils:*:app-a:access" => new RedisKey[] { "mud:httputils:T1:app-a:access" },
            _ => Array.Empty<RedisKey>(),
        });
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var purged = await store.PurgeAppTokensAsync("app-a");

        purged.Should().Be(1);
    }
}
