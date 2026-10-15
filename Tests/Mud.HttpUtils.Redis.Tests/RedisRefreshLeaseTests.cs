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
/// U-3：<see cref="RedisRefreshLease"/> 行为钉死测试（mock IConnectionMultiplexer/IDatabase）——
/// SET NX PX 获取（键形 / TTL / When.NotExists）、未抢到返回 null、Redis 异常 fail-open、
/// 释放经 Lua compare-and-del、释放异常吞掉、构造守卫。
/// </summary>
public class RedisRefreshLeaseTests
{
    private static (RedisRefreshLease Lease, Mock<IDatabase> Db) CreateLease(
        RedisRefreshLeaseOptions? options = null)
    {
        var db = new Mock<IDatabase>(MockBehavior.Strict);
        var connection = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);

        var lease = new RedisRefreshLease(connection.Object, options ?? new RedisRefreshLeaseOptions());
        return (lease, db);
    }

    [Fact]
    public async Task TryAcquireAsync_Should_SetKey_NX_WithTtl()
    {
        var (lease, db) = CreateLease();

        RedisKey? writtenKey = null;
        RedisValue writtenValue = default;
        TimeSpan? writtenTtl = null;
        When writtenWhen = When.Always;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, When>((k, v, ttl, w) =>
            {
                writtenKey = k;
                writtenValue = v;
                writtenTtl = ttl;
                writtenWhen = w;
            })
            .ReturnsAsync(true);

        var handle = await lease.TryAcquireAsync("MgrA:default", TimeSpan.FromSeconds(30));

        handle.Should().NotBeNull("SET NX 成功 = 抢到租约");
        writtenKey.Should().Be((RedisKey)"mud:httputils:lease:MgrA:default");
        writtenWhen.Should().Be(When.NotExists, "SET NX PX 原子获取");
        writtenTtl.Should().Be(TimeSpan.FromSeconds(30), "租约 TTL 由调用方给定（持有方崩溃时自动过期）");
        writtenValue.IsNull.Should().BeFalse("租约值为随机所有权凭据（GUID）");
        handle!.LeaseKey.Should().Be("MgrA:default");
    }

    [Fact]
    public async Task TryAcquireAsync_WhenKeyExists_Should_ReturnNull()
    {
        var (lease, db) = CreateLease();
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ReturnsAsync(false);

        var handle = await lease.TryAcquireAsync("MgrA:default", TimeSpan.FromSeconds(30));

        handle.Should().BeNull("已被其他实例持有 → 返回 null（fail-open 语义由调用方承接）");
    }

    [Fact]
    public async Task TryAcquireAsync_WhenRedisUnavailable_Should_ReturnNull_NotThrow()
    {
        var (lease, db) = CreateLease();
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "boom"));

        var act = async () => await lease.TryAcquireAsync("MgrA:default", TimeSpan.FromSeconds(30));

        (await act.Should().NotThrowAsync()).Subject.Should().BeNull(
            "契约：存储不可用不得抛出异常阻断令牌获取主路径（fail-open）");
    }

    [Fact]
    public async Task ReleaseAsync_Should_Evaluate_CompareAndDel_Script()
    {
        var (lease, db) = CreateLease();

        string? acquiredValue = null;
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, When>((_, v, _, _) => acquiredValue = v)
            .ReturnsAsync(true);
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        var handle = await lease.TryAcquireAsync("MgrA:default", TimeSpan.FromSeconds(30));
        await handle!.ReleaseAsync();

        db.Verify(d => d.ScriptEvaluateAsync(
            It.Is<string>(s => s.Contains("'get'") && s.Contains("'del'") && s.Contains("ARGV[1]")),
            It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == (RedisKey)"mud:httputils:lease:MgrA:default"),
            It.Is<RedisValue[]>(vals => vals.Length == 1 && vals[0] == acquiredValue),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task ReleaseAsync_WhenRedisUnavailable_Should_Swallow()
    {
        var (lease, db) = CreateLease();
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ReturnsAsync(true);
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "boom"));

        var handle = await lease.TryAcquireAsync("MgrA:default", TimeSpan.FromSeconds(30));

        var act = async () => await handle!.ReleaseAsync();
        await act.Should().NotThrowAsync("释放失败吞异常：租约等 TTL 自动过期，语义安全");
    }

    [Fact]
    public async Task TryAcquireAsync_EmptyKey_Should_Throw()
    {
        var (lease, _) = CreateLease();

        var act = async () => await lease.TryAcquireAsync("", TimeSpan.FromSeconds(30));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TryAcquireAsync_NonPositiveTtl_Should_Throw(int seconds)
    {
        var (lease, _) = CreateLease();

        var act = async () => await lease.TryAcquireAsync("MgrA:default", TimeSpan.FromSeconds(seconds));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>(
            "不得颁发无 TTL / 负 TTL 租约（持有方崩溃依赖 TTL 自动过期）");
    }

    [Fact]
    public async Task TryAcquireAsync_CustomKeyPrefix_Should_BeApplied()
    {
        var (lease, db) = CreateLease(new RedisRefreshLeaseOptions { KeyPrefix = "custom:ns" });
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ReturnsAsync(true);

        await lease.TryAcquireAsync("MgrA:default", TimeSpan.FromSeconds(30));

        db.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k == (RedisKey)"custom:ns:MgrA:default"),
            It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()), Times.Once);
    }
}
