// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Redis.Tests;

using Mud.HttpUtils.Redis;
using StackExchange.Redis;

/// <summary>
/// U-2：<see cref="RedisTokenStore"/> 的 v2 信封写入开关（默认关闭零影响 / 开启写 v2 / 两阶段确认 fail-fast）、
/// v2 与 v1 读取兼容、以及开启后 CAS 仍走 Lua 原子路径。
/// </summary>
public class RedisTokenStoreVersionedEnvelopeTests
{
    private static RedisTokenStoreOptions VersionedOptions() => new()
    {
        UseVersionedEnvelope = true,
        AcknowledgeVersionedEnvelopeReadersUpgraded = true,
        DefaultTtl = TimeSpan.FromDays(30),
    };

    private static (RedisTokenStore Store, Mock<IDatabase> Db) CreateStore(RedisTokenStoreOptions? options = null)
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var connection = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);

        return (new RedisTokenStore(connection.Object, options ?? new RedisTokenStoreOptions()), db);
    }

    [Fact]
    public async Task SetAccessTokenAsync_DefaultOptions_Should_WriteLegacyForm_ZeroImpactAnchor()
    {
        var (store, db) = CreateStore();

        RedisValue written = default;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((_, v, _, _, _, _) => written = v)
            .ReturnsAsync(true);

        await store.SetAccessTokenAsync("T1", "tok-1", 3600);

        written.Should().Be((RedisValue)"3600|tok-1", "开关默认关闭 ⇒ 写入形态与旧版逐字节一致（零影响锚用例）");
    }

    [Fact]
    public async Task SetAccessTokenAsync_WithVersionedEnvelope_Should_WriteVersionedForm()
    {
        var (store, db) = CreateStore(VersionedOptions());

        RedisValue written = default;
        TimeSpan? writtenTtl = null;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((_, v, ttl, _, _, _) =>
            {
                written = v;
                writtenTtl = ttl;
            })
            .ReturnsAsync(true);

        await store.SetAccessTokenAsync("T1", "tok-1", 3600);

        written.Should().Be((RedisValue)"v2:3600|tok-1");
        writtenTtl.Should().Be(TimeSpan.FromSeconds(3600), "TTL 推导与形态无关（C1 宁缺勿滥口径不变）");
    }

    [Fact]
    public async Task SetRefreshTokenAsync_WithVersionedEnvelope_Should_WriteVersionedNoTtlForm()
    {
        var (store, db) = CreateStore(VersionedOptions());

        RedisValue written = default;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, bool, When, CommandFlags>((_, v, _, _, _, _) => written = v)
            .ReturnsAsync(true);

        await store.SetRefreshTokenAsync("T1", "refresh-1");

        written.Should().Be((RedisValue)"v2:0|refresh-1", "刷新令牌的 0 值语义（无 TTL 信息）不变，仅加版本前缀");
    }

    [Fact]
    public void Constructor_WithVersionedEnvelopeButNoAcknowledgement_Should_FailFast()
    {
        var options = new RedisTokenStoreOptions { UseVersionedEnvelope = true };

        var act = () => new RedisTokenStore(new Mock<IConnectionMultiplexer>().Object, options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AcknowledgeVersionedEnvelopeReadersUpgraded*", "两阶段切换的确认项缺失必须 fail-fast，不得静默降级");
    }

    [Fact]
    public async Task GetAccessTokenAsync_Should_ReadVersionedAndLegacyValues()
    {
        var (store, db) = CreateStore(VersionedOptions());

        db.Setup(d => d.StringGetAsync((RedisKey)"mud:httputils:T1:shared:access", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"v2:3600|tok-1");

        (await store.GetAccessTokenAsync("T1")).Should().Be("tok-1");

        // 读取兼容：v1 存量值在启用 v2 后仍可读。
        db.Setup(d => d.StringGetAsync((RedisKey)"mud:httputils:T2:shared:access", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"3600|tok-2");

        (await store.GetAccessTokenAsync("T2")).Should().Be("tok-2");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenExplicitExpiredValue_Should_ReturnNull()
    {
        var (store, db) = CreateStore(VersionedOptions());

        db.Setup(d => d.StringGetAsync((RedisKey)"mud:httputils:T1:shared:access", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)VersionedTokenStoreCodec.ExpiredValue);

        (await store.GetAccessTokenAsync("T1")).Should().BeNull(
            "显式过期态读侧等价于删键（miss），与墓碑/缺失同口径");
    }

    [Fact]
    public async Task CompareExchangeAccessTokenAsync_WithVersionedEnvelope_Should_KeepAtomicScriptPath()
    {
        var (store, db) = CreateStore(VersionedOptions());

        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        (await store.CompareExchangeAccessTokenAsync("T1", "old-token", "new-token", 7200)).Should().BeTrue();

        // v2 前缀段不含 '|'，Lua 按首个 '|' 拆出的仍是令牌段 ⇒ 同属可原子家族（不退化为非原子回退）。
        db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.Is<RedisValue[]>(vals => vals.Length == 3
                && (string)vals[0]! == "old-token"
                && (string)vals[1]! == "v2:7200|new-token"
                && (string)vals[2]! == "7200000"),
            It.IsAny<CommandFlags>()), Times.Once);
        db.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never,
            "非原子回退路径不得被走到");
    }
}
