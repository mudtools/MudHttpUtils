// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using StackExchange.Redis;

namespace Mud.HttpUtils.Redis;

/// <summary>
/// F-3：官方分布式令牌存储 —— 基于 <see cref="StackExchange.Redis"/> 的 <see cref="ITokenStore"/> 实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>定位（durable，跨实例/跨进程）</b>：注册本实现后，B-2 自动桥接（<c>TokenStoreBackedTokenCache&lt;T&gt;</c>）
/// 会把持久化能力接入令牌管理器管线 —— 多实例共享 Redis 令牌、进程重启令牌不丢。
/// </para>
/// <para>
/// <b>键布局</b>：<c>{KeyPrefix}:{tokenType}:{appKey}:access|refresh</c>（基形与方案 F-3 的
/// <c>mud:httputils:{tokenType}:{appKey}</c> 一致），构建与解析单一事实来源见
/// <see cref="RedisTokenStoreKeyBuilder"/>。
/// </para>
/// <para>
/// <b>值序列化</b>：经 <see cref="ITokenStoreCodec"/>（默认 <see cref="DefaultTokenStoreCodec"/>）
/// 编解码 <c>{expiresInSeconds}|{token}</c> —— 与桥接器写穿形态同源，替换 codec 即可换格式。
/// 令牌为空时 codec 返回 <c>null</c>（墓碑语义）→ 对应键<b>删除</b>，不写空值。
/// </para>
/// <para>
/// <b>TTL 策略（C1，宁缺勿滥）</b>：访问令牌键 TTL = 写入方给定的 <c>expiresInSeconds</c>（&gt; 0 时），
/// 无过期信息时回落 <see cref="RedisTokenStoreOptions.DefaultTtl"/>（缺省 <c>null</c> = 不设 TTL）；
/// 刷新令牌键 TTL = <see cref="RedisTokenStoreOptions.DefaultTtl"/>。
/// 建议生产环境显式配置 <see cref="RedisTokenStoreOptions.DefaultTtl"/>，防 store 无界滞留令牌。
/// </para>
/// <para>
/// <b>并发刷新去重</b>（方案 F-3 验收"两实例共享 Redis 令牌只刷新一次"）：P1 最小版为
/// <b>进程内去重 + 分布式写原子</b>（Redis 单键 SET 原子）；跨进程 <c>SET NX PX</c> 租约去重
/// 列为增强项（写入时后写覆盖，代价是偶发重复刷新请求，不产生错误令牌）。
/// </para>
/// <para>
/// <b>非 AOT 交付物</b>：StackExchange.Redis 含反射/动态转换路径，本包已退出 AOT 严格模式
/// （与 <c>Mud.HttpUtils.Xml</c> 同口径），定位为服务器端可选依赖。
/// </para>
/// </remarks>
public sealed class RedisTokenStore : ITokenStore
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisTokenStoreOptions _options;
    private readonly ITokenStoreCodec _codec;

    /// <summary>
    /// 创建 Redis 令牌存储。
    /// </summary>
    /// <param name="connection">StackExchange.Redis 连接复用器（连接生命周期由调用方/宿主管理）。</param>
    /// <param name="options">配置；为 <c>null</c> 时取默认（前缀 <c>mud:httputils</c>、共享命名空间、无兜底 TTL）。</param>
    /// <param name="codec">存储值编解码器；为 <c>null</c> 时取 <see cref="DefaultTokenStoreCodec.Instance"/>。</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> 为 null。</exception>
    public RedisTokenStore(IConnectionMultiplexer connection, RedisTokenStoreOptions? options = null, ITokenStoreCodec? codec = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new RedisTokenStoreOptions();
        _codec = codec ?? DefaultTokenStoreCodec.Instance;
    }

    /// <inheritdoc />
    public async Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTokenType(tokenType);

        var raw = await Database.StringGetAsync(
            RedisTokenStoreKeyBuilder.BuildAccessKey(_options.KeyPrefix, tokenType, _options.AppKey)).ConfigureAwait(false);
        return DecodeToken(raw);
    }

    /// <inheritdoc />
    public async Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTokenType(tokenType);

        var key = RedisTokenStoreKeyBuilder.BuildAccessKey(_options.KeyPrefix, tokenType, _options.AppKey);
        var encoded = _codec.Encode(accessToken, expiresInSeconds);

        // 墓碑语义：空令牌（Encode 返回 null）→ 删除键，不写空值（与 ITokenStoreCodec 契约一致）。
        if (encoded is null)
        {
            await Database.KeyDeleteAsync(key).ConfigureAwait(false);
            return;
        }

        var ttl = expiresInSeconds > 0 ? TimeSpan.FromSeconds(expiresInSeconds) : _options.DefaultTtl;
        await Database.StringSetAsync(key, encoded, ttl).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTokenType(tokenType);

        var raw = await Database.StringGetAsync(
            RedisTokenStoreKeyBuilder.BuildRefreshKey(_options.KeyPrefix, tokenType, _options.AppKey)).ConfigureAwait(false);
        return DecodeToken(raw);
    }

    /// <inheritdoc />
    public async Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTokenType(tokenType);

        var key = RedisTokenStoreKeyBuilder.BuildRefreshKey(_options.KeyPrefix, tokenType, _options.AppKey);
        // 刷新令牌无过期信息（expiresInSeconds = 0，codec 契约的"无 TTL 信息"值）。
        var encoded = _codec.Encode(refreshToken, 0);

        if (encoded is null)
        {
            await Database.KeyDeleteAsync(key).ConfigureAwait(false);
            return;
        }

        await Database.StringSetAsync(key, encoded, _options.DefaultTtl).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTokenType(tokenType);

        var keys = new[]
        {
            (RedisKey)RedisTokenStoreKeyBuilder.BuildAccessKey(_options.KeyPrefix, tokenType, _options.AppKey),
            (RedisKey)RedisTokenStoreKeyBuilder.BuildRefreshKey(_options.KeyPrefix, tokenType, _options.AppKey)
        };
        await Database.KeyDeleteAsync(keys).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 经 <c>SCAN</c>（<see cref="IServer.Keys(int, RedisValue, int, long, int, CommandFlags)"/>）枚举
    /// 访问令牌键并反解 tokenType（<see cref="RedisTokenStoreKeyBuilder.TryParseTokenType"/>）。
    /// 同步枚举（低频管理面操作，非热路径）；<see cref="RedisTokenStoreOptions.Database"/> 为
    /// <c>-1</c>（连接默认库）时按 db0 枚举（Redis 集群仅 db0 可用）。
    /// </remarks>
    public Task<IEnumerable<string>> GetTokenTypesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tokenTypes = new HashSet<string>(StringComparer.Ordinal);
        var pattern = RedisTokenStoreKeyBuilder.BuildAccessPattern(_options.KeyPrefix, _options.AppKey);

        foreach (var key in ScanKeys(pattern))
        {
            var tokenType = RedisTokenStoreKeyBuilder.TryParseTokenType(_options.KeyPrefix, _options.AppKey, key);
            if (tokenType != null)
                tokenTypes.Add(tokenType);
        }

        return Task.FromResult<IEnumerable<string>>(tokenTypes.ToArray());
    }

    /// <inheritdoc />
    /// <remarks>
    /// 仅清理<b>本前缀 + 本 appKey 命名空间</b>下的键（<c>{prefix}:*:{appKey}:*</c>），
    /// 不触碰其他前缀/其他应用的数据（多应用隔离）。
    /// </remarks>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pattern = RedisTokenStoreKeyBuilder.BuildAccessPattern(_options.KeyPrefix, _options.AppKey);
        var refreshPattern = _options.KeyPrefix + ":" + "*:" +
            (string.IsNullOrWhiteSpace(_options.AppKey) ? RedisTokenStoreOptions.SharedAppKeySegment : _options.AppKey) +
            ":" + RedisTokenStoreKeyBuilder.RefreshField;

        var keys = new List<RedisKey>();
        foreach (var key in ScanKeys(pattern))
            keys.Add(key);
        foreach (var key in ScanKeys(refreshPattern))
            keys.Add(key);

        if (keys.Count == 0)
            return;

        await Database.KeyDeleteAsync(keys.ToArray()).ConfigureAwait(false);
    }

    private IDatabase Database => _connection.GetDatabase(_options.Database);

    /// <summary>解析存储值（codec 解析失败返回 null，符合 ITokenStoreCodec"不得抛异常"契约）。</summary>
    private string? DecodeToken(RedisValue raw)
        => raw.IsNull ? null : _codec.Decode(raw)?.Token;

    private IEnumerable<string> ScanKeys(string pattern)
    {
        // SCAN 需要 IServer；遍历全部端点（主从/集群各节点）并去重。
        var db = _options.Database < 0 ? 0 : _options.Database;
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var endpoint in _connection.GetEndPoints())
        {
            IServer server;
            try
            {
                server = _connection.GetServer(endpoint);
            }
            catch (RedisConnectionException)
            {
                continue;   // 端点不可达：跳过（SCAN 非热路径，容忍部分可见）
            }

            if (!server.IsConnected)
                continue;

            foreach (var key in server.Keys(db, pattern))
                keys.Add(key.ToString());
        }

        return keys;
    }

    private static void ValidateTokenType(string tokenType)
    {
        if (string.IsNullOrEmpty(tokenType))
            throw new ArgumentException("令牌类型不得为空。", nameof(tokenType));
    }
}
