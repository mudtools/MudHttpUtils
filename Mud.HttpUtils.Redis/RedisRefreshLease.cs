// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using StackExchange.Redis;

namespace Mud.HttpUtils.Redis;

/// <summary>
/// U-3：<see cref="RedisRefreshLease"/> 的配置项。
/// </summary>
public sealed class RedisRefreshLeaseOptions
{
    /// <summary>租约键命名空间前缀（默认 <c>mud:httputils:lease</c>；实际键 = <c>{KeyPrefix}:{leaseKey}</c>，可 SCAN）。</summary>
    public string KeyPrefix { get; set; } = "mud:httputils:lease";

    /// <summary>Redis 数据库编号；<c>-1</c>（默认）= 连接默认库。</summary>
    public int Database { get; set; } = -1;
}

/// <summary>
/// U-3：跨进程<b>令牌刷新去重租约</b>的 Redis 实现 —— <c>SET NX PX</c> 原子获取 + Lua compare-and-del
/// 所有权校验释放（3.0.8 F-3 登记的「跨进程刷新去重（SET NX PX 租约）」增强项）。
/// </summary>
/// <remarks>
/// <para>
/// <b>键布局</b>：<c>{KeyPrefix}:{leaseKey}</c>（默认 <c>mud:httputils:lease:{管理器键}:{scopeKey}</c>，
/// 与 <see cref="RedisTokenStore"/> 的 <c>{prefix}:{tokenType}:{appKey}:access|refresh</c> 命名空间并列、可 SCAN）。
/// </para>
/// <para>
/// <b>所有权语义</b>：获取成功时生成随机租约值（GUID）写入；释放经 Lua 脚本
/// 「值相等才删除」—— 租约 TTL 过期后被其他实例重新获取时，前一持有方的迟到释放为 no-op，
/// <b>不误删新持有方的租约</b>。
/// </para>
/// <para>
/// <b>fail-open 契约</b>：Redis 不可用（连接/超时异常）时获取返回 <c>null</c>、释放吞异常 ——
/// 遵循 <see cref="ITokenRefreshLease"/> 契约「不得抛出异常阻断令牌获取主路径」；
/// fail-open 后果仅是「重复刷新一次」，与未启用租约时等价。
/// </para>
/// </remarks>
public sealed class RedisRefreshLease : ITokenRefreshLease
{
    /// <summary>Lua：值相等才删除（compare-and-del，原子）。</summary>
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly IConnectionMultiplexer _connection;
    private readonly RedisRefreshLeaseOptions _options;

    /// <summary>
    /// 创建 Redis 刷新租约。
    /// </summary>
    /// <param name="connection">StackExchange.Redis 连接复用器（连接生命周期由调用方/宿主管理）。</param>
    /// <param name="options">配置；为 <c>null</c> 时取默认（前缀 <c>mud:httputils:lease</c>、连接默认库）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> 为 null。</exception>
    public RedisRefreshLease(IConnectionMultiplexer connection, RedisRefreshLeaseOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new RedisRefreshLeaseOptions();
    }

    /// <inheritdoc />
    public async Task<ITokenRefreshLeaseHandle?> TryAcquireAsync(string leaseKey, TimeSpan leaseTime, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(leaseKey))
            throw new ArgumentException("租约键不得为空。", nameof(leaseKey));
        if (leaseTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseTime), "租约 TTL 必须为正（持有方崩溃时依赖 TTL 自动过期，不得颁发永久租约）。");

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var value = Guid.NewGuid().ToString("N");
            var db = _connection.GetDatabase(_options.Database);
            var acquired = await db.StringSetAsync(Key(leaseKey), value, leaseTime, When.NotExists).ConfigureAwait(false);
            return acquired ? new LeaseHandle(this, leaseKey, value) : null;
        }
        catch (RedisException)
        {
            // fail-open 契约：存储不可用时返回 null（未抢到），由调用方等待/降级，不阻断令牌主路径。
            return null;
        }
    }

    private IDatabase Database => _connection.GetDatabase(_options.Database);

    private string Key(string leaseKey) => $"{_options.KeyPrefix}:{leaseKey}";

    private async Task ReleaseAsync(string leaseKey, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await Database.ScriptEvaluateAsync(
                ReleaseScript,
                new RedisKey[] { Key(leaseKey) },
                new RedisValue[] { value }).ConfigureAwait(false);
        }
        catch (RedisException)
        {
            // 释放失败吞异常：租约等 TTL 自动过期，语义安全（compare-and-del 契约兜底）。
        }
    }

    private sealed class LeaseHandle(RedisRefreshLease owner, string leaseKey, string value) : ITokenRefreshLeaseHandle
    {
        public string LeaseKey { get; } = leaseKey;

        public Task ReleaseAsync(CancellationToken cancellationToken = default)
            => owner.ReleaseAsync(LeaseKey, value, cancellationToken);
    }
}
