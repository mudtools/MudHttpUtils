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
/// <b>U-2 v2 信封（可选）</b>：<see cref="RedisTokenStoreOptions.UseVersionedEnvelope"/> 为 <c>true</c> 时
/// 写入侧改用 <see cref="VersionedTokenStoreCodec"/>（<c>v2:{expiresInSeconds}|{token}</c>，含显式过期态），
/// 读取侧本就兼容两种形态（<see cref="DefaultTokenStoreCodec"/> 已识别 v2 前缀；本实现换 codec 后亦直读 v1）。
/// 默认 <c>false</c> = 形态逐位不变。
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
public sealed class RedisTokenStore : ITokenStore, ITokenStoreAppPurge, ITokenStoreCompareExchange
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisTokenStoreOptions _options;
    private readonly ITokenStoreCodec _codec;

    /// <summary>
    /// 创建 Redis 令牌存储。
    /// </summary>
    /// <param name="connection">StackExchange.Redis 连接复用器（连接生命周期由调用方/宿主管理）。</param>
    /// <param name="options">配置；为 <c>null</c> 时取默认（前缀 <c>mud:httputils</c>、共享命名空间、无兜底 TTL）。</param>
    /// <param name="codec">
    /// 存储值编解码器；为 <c>null</c> 时按 <see cref="RedisTokenStoreOptions.UseVersionedEnvelope"/> 取
    /// <see cref="VersionedTokenStoreCodec.Instance"/>（启用 v2 信封）或 <see cref="DefaultTokenStoreCodec.Instance"/>。
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="RedisTokenStoreOptions.UseVersionedEnvelope"/> 已开启但未确认读取方升级
    /// （<see cref="RedisTokenStoreOptions.AcknowledgeVersionedEnvelopeReadersUpgraded"/>）。
    /// </exception>
    public RedisTokenStore(IConnectionMultiplexer connection, RedisTokenStoreOptions? options = null, ITokenStoreCodec? codec = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? new RedisTokenStoreOptions();

        // U-2：混部署风险在唯一构造边界上 fail-fast（显式传入 codec 时以调用方 codec 为准，不受开关影响）。
        RedisTokenStoreOptionsValidation.EnsureVersionedEnvelopeConfirmed(_options);

        _codec = codec ?? (_options.UseVersionedEnvelope
            ? VersionedTokenStoreCodec.Instance
            : DefaultTokenStoreCodec.Instance);
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
        // SE.Redis 3.x：显式 6 参形态（TimeSpan? 为 null = 无过期，宁缺勿滥语义与 2.x 延续一致）；
        // 不用 3 参糖重载（其绑定到 Expiration 原生成员，且「无 TTL」需经 default(Expiration) 表达）。
        await Database.StringSetAsync(key, encoded, ttl, false, When.Always, CommandFlags.None).ConfigureAwait(false);
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

        // SE.Redis 3.x：显式 6 参形态（DefaultTtl 未配置 = null = 无过期，宁缺勿滥）。
        await Database.StringSetAsync(key, encoded, _options.DefaultTtl, false, When.Always, CommandFlags.None).ConfigureAwait(false);
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
    /// U-4 起本方法为 <see cref="PurgeAppAsync"/> 的便捷形态（≡ <c>PurgeAppAsync(本实例 AppKey)</c>），
    /// SCAN/删除逻辑单一事实源，杜绝双轨漂移。
    /// </remarks>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await PurgeAppAsync(_options.AppKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// U-4：快照语义 —— 经 <c>SCAN</c>（<see cref="IServer.Keys(int, RedisValue, int, long, int, CommandFlags)"/>）
    /// 枚举<b>指定 appKey</b> 名下的访问令牌键与刷新令牌键（键解析内聚在
    /// <see cref="RedisTokenStoreKeyBuilder"/>，tokenType 可含 <c>:</c>）。
    /// <paramref name="appKey"/> 为 null/空白时按 <see cref="RedisTokenStoreOptions.SharedAppKeySegment"/>
    /// 共享命名空间处置。
    /// </remarks>
    public async Task<IReadOnlyCollection<string>> EnumerateKeysAsync(string appKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var segment = string.IsNullOrWhiteSpace(appKey)
            ? RedisTokenStoreOptions.SharedAppKeySegment
            : appKey;
        var accessPattern = RedisTokenStoreKeyBuilder.BuildAccessPattern(_options.KeyPrefix, segment);
        var refreshPattern = _options.KeyPrefix + ":" + "*:" + segment + ":" + RedisTokenStoreKeyBuilder.RefreshField;

        var keys = new List<string>();
        foreach (var key in ScanKeys(accessPattern))
            keys.Add(key);
        foreach (var key in ScanKeys(refreshPattern))
            keys.Add(key);

        return keys;
    }

    /// <inheritdoc />
    /// <remarks>
    /// U-4：按<b>参数给定的 appKey</b> 精确清库（与 <see cref="ClearAsync"/> 绑定实例配置不同，
    /// 多应用单实例部署时无需为每个 appKey 建独立 store），不误伤其他 appKey 的键；
    /// 单批 <c>DEL</c> 原子删除，键集为空时不发命令。
    /// </remarks>
    public async Task<int> PurgeAppAsync(string appKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var keys = await EnumerateKeysAsync(appKey, cancellationToken).ConfigureAwait(false);
        if (keys.Count == 0)
            return 0;

        var redisKeys = new RedisKey[keys.Count];
        var i = 0;
        foreach (var key in keys)
            redisKeys[i++] = key;

        await Database.KeyDeleteAsync(redisKeys).ConfigureAwait(false);
        return keys.Count;
    }

    private IDatabase Database => _connection.GetDatabase(_options.Database);

    // ──────────────────────────────────────────────
    //  U-7：原生 CAS（ITokenStoreCompareExchange）
    // ──────────────────────────────────────────────

    /// <summary>
    /// U-7：CAS 原子脚本（默认 codec 家族形态 <c>{seconds}|{token}</c> 可在服务端拆分提取令牌段）：
    /// GET 原值 → 拆出令牌明文与 expected 比较 → 匹配则按语义 SET（含/不含 PX）或 DEL → 不匹配不写入。
    /// </summary>
    private const string CompareExchangeScript =
        "local raw = redis.call('GET', KEYS[1]) " +
        "if not raw then return 0 end " +
        "local sep = string.find(raw, '|', 1, true) " +
        "local stored = '' " +
        "if sep then stored = string.sub(raw, sep + 1) end " +
        "if stored ~= ARGV[1] then return 0 end " +
        "if ARGV[2] == '' then redis.call('DEL', KEYS[1]) return 1 end " +
        "if ARGV[3] == '' then redis.call('SET', KEYS[1], ARGV[2]) else redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3]) end " +
        "return 1";

    /// <summary>
    /// 默认 codec 家族判定：Encode 输出为 <c>{seconds}|{token}</c> 形态（Lua 按首个 <c>|</c> 即可拆出令牌段），CAS 可原子。
    /// U-2：v2 信封 <c>v2:{seconds}|{token}</c> 的前缀段不含 <c>|</c>，首个 <c>|</c> 仍恰为令牌段起点，故同属可原子家族。
    /// </summary>
    private bool IsDefaultFamilyCodec
        => _codec is DefaultTokenStoreCodec or MigrationTokenStoreCodec or VersionedTokenStoreCodec;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>原子性边界（U-7 契约）</b>：默认 codec 家族（<see cref="DefaultTokenStoreCodec"/> /
    /// <see cref="MigrationTokenStoreCodec"/> / <see cref="VersionedTokenStoreCodec"/>，Encode 输出
    /// <c>{seconds}|{token}</c> 或 <c>v2:{seconds}|{token}</c>）经 Lua 脚本原子执行；
    /// 自定义 codec 退化为「读取 → 明文比较 → 写入」<b>非原子</b>路径（服务端脚本无法执行任意 C# codec）。
    /// </para>
    /// <para><paramref name="expiresInSeconds"/> ≤ 0 时不设 TTL（C1 宁缺勿滥）。</para>
    /// </remarks>
    public async Task<bool> CompareExchangeAccessTokenAsync(string tokenType, string expectedToken, string? newToken, long expiresInSeconds = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTokenType(tokenType);
        ValidateExpectedToken(expectedToken);

        var key = (RedisKey)RedisTokenStoreKeyBuilder.BuildAccessKey(_options.KeyPrefix, tokenType, _options.AppKey);
        var pxMilliseconds = expiresInSeconds > 0
            ? (expiresInSeconds * 1000L).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

        if (IsDefaultFamilyCodec)
        {
            var encodedNew = string.IsNullOrEmpty(newToken) ? string.Empty : _codec.Encode(newToken, expiresInSeconds) ?? string.Empty;
            var result = await Database.ScriptEvaluateAsync(
                CompareExchangeScript,
                new RedisKey[] { key },
                new RedisValue[] { expectedToken, encodedNew, pxMilliseconds }).ConfigureAwait(false);
            return ((long)result) == 1;
        }

        // 非默认 codec：非原子回退（读取 → 明文比较 → 写入/删除）。
        return await CompareExchangeFallbackAsync(key, expectedToken, newToken, expiresInSeconds,
            _ => expiresInSeconds > 0 ? TimeSpan.FromSeconds(expiresInSeconds) : null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// refresh 轮换主场景（飞书 TMF-05）：写入侧 TTL 与 <see cref="SetRefreshTokenAsync"/> 同源
    /// （<see cref="RedisTokenStoreOptions.DefaultTtl"/>，未配置则不设 TTL）；compare-and-delete 时忽略 TTL。
    /// 原子性边界同 <see cref="CompareExchangeAccessTokenAsync"/>。
    /// </remarks>
    public async Task<bool> CompareExchangeRefreshTokenAsync(string tokenType, string expectedToken, string? newToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTokenType(tokenType);
        ValidateExpectedToken(expectedToken);

        var key = (RedisKey)RedisTokenStoreKeyBuilder.BuildRefreshKey(_options.KeyPrefix, tokenType, _options.AppKey);

        if (IsDefaultFamilyCodec)
        {
            var encodedNew = string.IsNullOrEmpty(newToken) ? string.Empty : _codec.Encode(newToken, 0) ?? string.Empty;
            var pxMilliseconds = _options.DefaultTtl is { } ttl
                ? ((long)ttl.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
            var result = await Database.ScriptEvaluateAsync(
                CompareExchangeScript,
                new RedisKey[] { key },
                new RedisValue[] { expectedToken, encodedNew, pxMilliseconds }).ConfigureAwait(false);
            return ((long)result) == 1;
        }

        return await CompareExchangeFallbackAsync(key, expectedToken, newToken, 0, _ => _options.DefaultTtl, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>U-7 非默认 codec 的非原子回退：读取 → 明文比较 → 写入/删除（契约允许，须文档化）。</summary>
    private async Task<bool> CompareExchangeFallbackAsync(
        RedisKey key, string expectedToken, string? newToken, long expiresInSeconds,
        Func<string, TimeSpan?> ttlSelector, CancellationToken cancellationToken)
    {
        var raw = await Database.StringGetAsync(key).ConfigureAwait(false);
        if (raw.IsNull)
            return false;

        var decoded = _codec.Decode(raw);
        if (decoded is null || !string.Equals(decoded.Value.Token, expectedToken, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrEmpty(newToken))
        {
            await Database.KeyDeleteAsync(key).ConfigureAwait(false);
            return true;
        }

        var encoded = _codec.Encode(newToken, expiresInSeconds);
        if (encoded is null)
        {
            await Database.KeyDeleteAsync(key).ConfigureAwait(false);
            return true;
        }

        // SE.Redis 3.x：显式 6 参形态（ttl 为 null = 无过期，宁缺勿滥）。
        await Database.StringSetAsync(key, encoded, ttlSelector(encoded), false, When.Always, CommandFlags.None).ConfigureAwait(false);
        return true;
    }

    private static void ValidateExpectedToken(string expectedToken)
    {
        if (string.IsNullOrEmpty(expectedToken))
            throw new ArgumentException("期望的旧令牌明文不得为空。", nameof(expectedToken));
    }

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
