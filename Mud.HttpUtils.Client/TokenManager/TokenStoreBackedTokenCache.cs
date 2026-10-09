// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌存储值的扁平载体：桥接器与具体令牌类型（<see cref="CredentialToken"/> / <see cref="UserTokenInfo"/> 等）解耦的
/// 字符串三元组，形状与 <see cref="ITokenStore"/> 的读写参数一一对应。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ExpiresInSeconds"/> 在<b>写穿</b>方向由值适配器从令牌自身的过期字段推导
/// （如 <see cref="CredentialToken.Expire"/> / <see cref="UserTokenInfo.AccessTokenExpireTime"/>），
/// 保证 store TTL 与管线判定口径同源（治理方案不变量 #4 / C1）；<c>&lt;= 0</c> 表示无过期信息。
/// 在<b>水合</b>方向恒为 0 —— <see cref="ITokenStore"/> 的读契约不回传剩余 TTL，
/// 水合后条目的有效期语义由调用方的值工厂决定（见 <see cref="TokenStoreBackedTokenCache{T}"/> 的 remarks）。
/// </para>
/// </remarks>
public readonly struct TokenStoreValue
{
    /// <summary>访问令牌。可为 null（仅刷新令牌场景）。</summary>
    public string? AccessToken { get; }

    /// <summary>刷新令牌。可为 null。</summary>
    public string? RefreshToken { get; }

    /// <summary>访问令牌的剩余有效时长（秒）。&lt;= 0 表示无过期信息。</summary>
    public long ExpiresInSeconds { get; }

    /// <summary>
    /// 初始化令牌存储值。
    /// </summary>
    /// <param name="accessToken">访问令牌。</param>
    /// <param name="refreshToken">刷新令牌。</param>
    /// <param name="expiresInSeconds">访问令牌剩余有效时长（秒）；&lt;= 0 表示无过期信息。</param>
    public TokenStoreValue(string? accessToken, string? refreshToken, long expiresInSeconds = 0)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresInSeconds = expiresInSeconds;
    }
}

/// <summary>
/// 桥接器：以 <see cref="ITokenCache{T}"/> 门面包装 <see cref="ITokenStore"/> / <see cref="IUserTokenStore"/>（持久化 SPI），
/// 让持久化能力进入令牌管理器管线 —— 消除"宿主在管理器之外自建叠层"的结构性重复（治理方案 §5.3，方案 B）。
/// </summary>
/// <remarks>
/// <para>
/// <b>工作机制（内存镜像 + 异步写穿 + 显式水合）</b>：
/// <list type="bullet">
/// <item>读（<see cref="TryGet"/>）<b>只读内存镜像</b> —— 零 I/O、零阻塞，不破坏 <see cref="ITokenCache{T}"/> 的同步契约（方案 A 的阻塞等待已否决）。</item>
/// <item>写（<see cref="Set(string,T)"/> / <see cref="Set(string,T,System.TimeSpan?,System.TimeSpan?,System.Action{string}?)"/>）更新镜像并<b>异步写穿</b> store；写穿失败进入补偿队列并可重放（<see cref="RetryFailedWritesAsync"/>），不阻塞、不抛出。</item>
/// <item>冷启动由宿主显式调用 <see cref="HydrateAsync(System.Func{string,string}?,System.Threading.CancellationToken)"/>（租户）或
/// <see cref="HydrateUserAsync(string, System.Func{string,string}?, System.Threading.CancellationToken)"/>（用户）装载 —— <see cref="IUserTokenStore"/> 无"枚举所有用户"契约，用户维度只能按 userId 水合。</item>
/// <item><b>S2 真穿透</b>：本类同时实现 <see cref="IAsyncTokenCache{T}"/> —— 管理器在异步管线路径上经能力探测走
/// <see cref="GetAsync"/> 读穿透（镜像未命中时直达 store 并回填镜像），消除"冷启动必须先水合"的限制；
/// 多实例下其他实例写入的数据在本地镜像未命中时即可读到。</item>
/// </list>
/// </para>
/// <para>
/// <b>契约不变量（与 .docs/token-store-cache-architecture-review.md §5.3 一一对应）</b>：
/// <list type="number">
/// <item><see cref="Keys"/> / <see cref="Count"/> 与内存镜像同源且完整 —— 登出/失效经 <see cref="TryRemove"/> / <c>Set(key, null)</c> 会同步写穿删除，防止"登出漏删"（C6）。</item>
/// <item><see cref="Dispose"/> 默认<b>不释放</b>内层 store（<c>ownsInnerStore: false</c>）—— store（如 Redis 连接）通常由 DI 容器管理，提前释放会破坏共享生命周期（C4）；显式传 true 才代管。</item>
/// <item>构造期检测加密叠加：内层 store 已实现 <see cref="IEncryptedTokenStore"/> 且启用加密时记录 Warning 并置 <see cref="IsInnerStoreEncrypted"/> —— 同一读写链路只允许一层加密（C5），此时<b>不得</b>再套 <c>EncryptedTokenCache&lt;T&gt;</c>。</item>
/// <item>过期唯一判定点在管线：写穿的 <c>expiresInSeconds</c> 优先取值适配器从令牌自身过期字段推导的结果（与管线判定同源），Set 的绝对过期参数仅作回退；两者皆缺省时<b>跳过访问令牌写穿</b>（宁缺勿滥，防止 store 以无界 TTL 滞留令牌）（C1）。</item>
/// <item>写穿失败可观测：<see cref="WriteThroughFailures"/> / <see cref="PendingCompensationCount"/> 计数 + Warning 日志 + <see cref="RetryFailedWritesAsync"/> 重放。</item>
/// <item>AOT 安全：值映射走显式委托（<c>valueAdapter</c> / <c>valueFactory</c>），零反射、零序列化（强于 <see cref="EncryptedTokenCache{T}"/> 的 JSON 路径）。</item>
/// </list>
/// </para>
/// <para>
/// <b>键映射（C2 的桥接落点）</b>：cacheKey（scopeKey / userId[\u001F scopeKey]）与 store 键
/// （tokenType / userId+tokenType）不可互相推导，须显式注入映射且<b>必须单射</b>（不单射 = 登出覆盖缺口）：
/// 租户维度 <c>storeKeyMapper</c>（默认恒等）；用户维度 <c>userKeyMapper</c>（必填，
/// 可用 <see cref="TokenStoreBackedTokenCache{T}.DefaultUserKeyMapper"/> 处理本仓复合键约定）。
/// </para>
/// <para>
/// <b>已知限制（方案 B 固有，2.2 的 <c>IAsyncTokenCache&lt;T&gt;</c> 真穿透根治）</b>：多实例部署时，
/// 其他实例写入 store 的数据不会自动刷新本进程镜像，需以适度的重新水合 / TTL 兜底；
/// 镜像 <see cref="Compact"/> / 驱逐仅作用于镜像（store 侧过期由其自身 TTL 负责），不回删 store。
/// </para>
/// </remarks>
/// <typeparam name="T">缓存值类型。</typeparam>
public class TokenStoreBackedTokenCache<T> : ITokenCache<T>, IAsyncTokenCache<T> where T : class
{
    // 补偿队列上限：防故障 store（长时间不可用）把队列撑爆。
    // 超限时丢弃新入队条目并计数 —— 写穿失败本就有 Warning 日志，丢补偿不会静默。
    private const int CompensationQueueLimit = 1024;

    private readonly ConcurrentDictionary<string, CacheEntry> _mirror = new(StringComparer.Ordinal);
    private readonly ITokenStore? _store;
    private readonly IUserTokenStore? _userStore;
    private readonly Func<string, string>? _storeKeyMapper;
    private readonly Func<string, (string UserId, string TokenType)>? _userKeyMapper;
    private readonly Func<T?, TokenStoreValue?> _valueAdapter;
    private readonly Func<TokenStoreValue, T?> _valueFactory;
    private readonly bool _ownsInnerStore;
    private readonly ILogger _logger;
    private readonly ConcurrentQueue<string> _compensation = new();
    private volatile bool _disposed;
    private int _pendingWrites;
    private long _writeThroughFailures;
    private int _droppedCompensations;

    /// <summary>
    /// 初始化租户维度的桥接器（内层为 <see cref="ITokenStore"/>）。
    /// </summary>
    /// <param name="innerStore">内层持久化存储。</param>
    /// <param name="valueAdapter">写穿方向的值适配：缓存值 → 存储三元组；返回 null 表示删除该键。</param>
    /// <param name="valueFactory">水合方向的值工厂：存储三元组 → 缓存值；返回 null 则该条目不入镜像。</param>
    /// <param name="storeKeyMapper">cacheKey → store tokenType 映射（必须单射）；null = 恒等映射。</param>
    /// <param name="ownsInnerStore">Dispose 时是否释放内层 store。默认 false（store 通常由 DI 容器管理，如 Redis 连接）。</param>
    /// <param name="logger">日志记录器（可选）。为 null 时静默。</param>
    /// <exception cref="ArgumentNullException">必填参数为 null。</exception>
    public TokenStoreBackedTokenCache(
        ITokenStore innerStore,
        Func<T?, TokenStoreValue?> valueAdapter,
        Func<TokenStoreValue, T?> valueFactory,
        Func<string, string>? storeKeyMapper = null,
        bool ownsInnerStore = false,
        ILogger? logger = null)
    {
        _store = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _valueAdapter = valueAdapter ?? throw new ArgumentNullException(nameof(valueAdapter));
        _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        _storeKeyMapper = storeKeyMapper;
        _ownsInnerStore = ownsInnerStore;
        _logger = logger ?? NullLogger.Instance;
        WarnIfInnerStoreEncrypted(innerStore);
    }

    /// <summary>
    /// 初始化用户维度的桥接器（内层为 <see cref="IUserTokenStore"/>）。
    /// 本桥接器对用户维度<b>只使用带 userId 的成员</b>，绝不调用继承自 <see cref="ITokenStore"/> 的
    /// 无 userId 成员（其语义未定义 —— 见 <see cref="IUserTokenStore"/> 契约规则 / C7）。
    /// </summary>
    /// <param name="innerUserStore">内层用户级持久化存储。</param>
    /// <param name="userKeyMapper">cacheKey → (userId, tokenType) 映射（<b>必填</b>，必须单射）。
    /// 可用 <see cref="TokenStoreBackedTokenCache{T}.DefaultUserKeyMapper(string)"/> 处理本仓复合键约定。</param>
    /// <param name="valueAdapter">写穿方向的值适配：缓存值 → 存储三元组；返回 null 表示删除该键。</param>
    /// <param name="valueFactory">水合方向的值工厂：存储三元组 → 缓存值；返回 null 则该条目不入镜像。</param>
    /// <param name="ownsInnerStore">Dispose 时是否释放内层 store。默认 false。</param>
    /// <param name="logger">日志记录器（可选）。为 null 时静默。</param>
    /// <exception cref="ArgumentNullException">必填参数为 null。</exception>
    public TokenStoreBackedTokenCache(
        IUserTokenStore innerUserStore,
        Func<string, (string UserId, string TokenType)> userKeyMapper,
        Func<T?, TokenStoreValue?> valueAdapter,
        Func<TokenStoreValue, T?> valueFactory,
        bool ownsInnerStore = false,
        ILogger? logger = null)
    {
        _userStore = innerUserStore ?? throw new ArgumentNullException(nameof(innerUserStore));
        _userKeyMapper = userKeyMapper ?? throw new ArgumentNullException(nameof(userKeyMapper));
        _valueAdapter = valueAdapter ?? throw new ArgumentNullException(nameof(valueAdapter));
        _valueFactory = valueFactory ?? throw new ArgumentNullException(nameof(valueFactory));
        _ownsInnerStore = ownsInnerStore;
        _logger = logger ?? NullLogger.Instance;
        WarnIfInnerStoreEncrypted(innerUserStore);
    }

    /// <summary>
    /// 默认用户键映射：首个 <c>'\u001F'</c> 前缀为 <c>userId</c>（与 <c>UserTokenManagerBase</c> 复合键约定一致），
    /// 其后整段并入 tokenType（保证 scope 维度不碰撞 —— 单射性要求）；裸 userId 键映射为 <c>(userId, userId)</c>。
    /// </summary>
    public static (string UserId, string TokenType) DefaultUserKeyMapper(string cacheKey)
    {
        var separatorIndex = cacheKey.IndexOf('\u001F');
        return separatorIndex < 0
            ? (cacheKey, cacheKey)
            : (cacheKey.Substring(0, separatorIndex), cacheKey.Substring(separatorIndex + 1));
    }

    /// <summary>
    /// G2 便捷工厂：为 <see cref="CredentialToken"/>（租户维度）装配桥接器 ——
    /// 内部完成 valueAdapter / valueFactory 的样板代码，消费方只需提供存储实现。
    /// </summary>
    /// <param name="store">租户维度持久化存储。</param>
    /// <param name="storeKeyMapper">cacheKey → store tokenType 映射；null = 恒等映射。</param>
    /// <param name="ownsInnerStore">Dispose 时是否释放内层 store。</param>
    /// <param name="logger">日志（可选）。</param>
    /// <returns>桥接后的 <see cref="ITokenCache{T}"/>（同时实现 <see cref="IAsyncTokenCache{T}"/>）。</returns>
    /// <remarks>
    /// 与直接调用构造签名等价（零新语义），仅省去"手写值适配/工厂委托"的接线成本。
    /// 若持久化侧把"过期戳 + 令牌"存为<b>单个字符串</b>，请在自己的 <see cref="ITokenStore"/> 实现内
    /// 使用 <see cref="ITokenStoreCodec"/>（默认实现 <see cref="DefaultTokenStoreCodec"/>）编解码，
    /// 避免各下游重复发明私有格式。
    /// </remarks>
    public static TokenStoreBackedTokenCache<CredentialToken> CreateForCredentialToken(
        ITokenStore store,
        Func<string, string>? storeKeyMapper = null,
        bool ownsInnerStore = false,
        ILogger? logger = null)
        => new(
            store,
            token => token is null
                ? null
                : new TokenStoreValue(token.AccessToken, token.RefreshToken, RemainingSeconds(token.Expire)),
            value => new CredentialToken
            {
                AccessToken = value.AccessToken,
                RefreshToken = value.RefreshToken,
                Expire = value.ExpiresInSeconds > 0
                    ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (value.ExpiresInSeconds * 1000)
                    : 0
            },
            storeKeyMapper,
            ownsInnerStore,
            logger);

    /// <summary>
    /// G2 便捷工厂：为 <see cref="UserTokenInfo"/>（用户维度）装配桥接器。
    /// </summary>
    /// <param name="userStore">用户维度持久化存储。</param>
    /// <param name="userKeyMapper">cacheKey → (userId, tokenType) 映射；null 时使用
    /// <see cref="DefaultUserKeyMapper"/>（与本仓复合键约定一致）。</param>
    /// <param name="ownsInnerStore">Dispose 时是否释放内层 store。</param>
    /// <param name="logger">日志（可选）。</param>
    /// <returns>桥接后的用户维度缓存。</returns>
    public static TokenStoreBackedTokenCache<UserTokenInfo> CreateForUserTokenInfo(
        IUserTokenStore userStore,
        Func<string, (string UserId, string TokenType)>? userKeyMapper = null,
        bool ownsInnerStore = false,
        ILogger? logger = null)
        => new(
            userStore,
            userKeyMapper ?? DefaultUserKeyMapper,
            token => token is null
                ? null
                : new TokenStoreValue(token.AccessToken, token.RefreshToken, RemainingSeconds(token.AccessTokenExpireTime)),
            value => new UserTokenInfo
            {
                AccessToken = value.AccessToken,
                RefreshToken = value.RefreshToken,
                AccessTokenExpireTime = value.ExpiresInSeconds > 0
                    ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (value.ExpiresInSeconds * 1000)
                    : 0
            },
            ownsInnerStore,
            logger);

    /// <summary>由绝对过期毫秒推导剩余秒数（&lt;= 0 表示无 TTL 信息）。</summary>
    private static long RemainingSeconds(long expireMs)
    {
        if (expireMs <= 0)
            return 0;

        var remaining = (expireMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000;
        return remaining > 0 ? remaining : 0;
    }

    /// <summary>
    /// 获取一个值，指示内层 store 是否已启用加密（<see cref="IEncryptedTokenStore.IsEncryptionEnabled"/>）。
    /// 为 true 时同一读写链路<b>不得</b>再叠加缓存层加密（<c>EncryptedTokenCache&lt;T&gt;</c>）。
    /// </summary>
    public bool IsInnerStoreEncrypted { get; private set; }

    /// <summary>写穿失败累计次数（不变量 #5）。经 <see cref="RetryFailedWritesAsync"/> 重放成功后不回退（累计口径，对齐 <see cref="MudHttpMeter"/> 的 counter 语义）。</summary>
    public long WriteThroughFailures => Interlocked.Read(ref _writeThroughFailures);

    /// <summary>当前滞留在补偿队列、待 <see cref="RetryFailedWritesAsync"/> 重放的写穿条目数（不变量 #5）。</summary>
    public int PendingCompensationCount => _compensation.Count;

    /// <summary>补偿队列溢出后丢弃的写穿条目累计数（队列有界，防故障 store 撑爆内存）。</summary>
    public int DroppedCompensationCount => Volatile.Read(ref _droppedCompensations);

    /// <summary>当前在途的后台写穿任务数（诊断观测用；同步完成的写穿不计入）。</summary>
    public int PendingWriteThroughCount => Volatile.Read(ref _pendingWrites);

    /// <inheritdoc />
    public int Count => _mirror.Count;

    /// <inheritdoc />
    /// <remarks>与内存镜像同源且完整（不变量 #1）：镜像缺失的键（store 独有、尚未水合）不出现在枚举中，
    /// 保证前缀扫描（登出 / 会话可见性）不会读到镜像中不存在的条目。</remarks>
    public IEnumerable<string> Keys => _mirror.Keys;

    /// <inheritdoc />
    public bool TryGet(string key, out T? value)
    {
        if (_disposed)
        {
            value = default;
            return false;
        }

        if (_mirror.TryGetValue(key, out var entry))
        {
            // TMX-15-8 同款 LRU 时间戳降采样：距上次更新 < 1s 跳过 Interlocked.Exchange
            var nowTicks = DateTime.UtcNow.Ticks;
            var last = entry.LastAccessTicks;
            if (nowTicks - last >= TimeSpan.TicksPerSecond)
                Interlocked.Exchange(ref entry.LastAccessTicks, nowTicks);
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc />
    public void Set(string key, T? value)
        => Set(key, value, null, null, null);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// 语义分层：<paramref name="value"/> 为 null 时按<b>失效</b>处理（镜像移除 + 写穿删除 + 触发回调），
    /// 与 <see cref="TokenManagerBase"/> 的 <c>Set(scopeKey, null)</c> 失效意图对齐。
    /// </para>
    /// <para>
    /// 写穿的 <c>expiresInSeconds</c> 推导顺序：值适配器产出的 <see cref="TokenStoreValue.ExpiresInSeconds"/>
    /// → <paramref name="absoluteExpirationRelativeToNow"/> → 皆缺省则跳过访问令牌写穿（不变量 #4）。
    /// </para>
    /// <para>
    /// <paramref name="postEvictionCallback"/> 在本桥接器自行移除（<c>Set(key, null)</c>）/ 替换 / 压缩驱逐条目时触发；
    /// 镜像不随时间自动过期（过期判定归管线），故 <paramref name="slidingExpiration"/> 仅存储、不生效。
    /// </para>
    /// </remarks>
    public void Set(string key, T? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null)
    {
        if (_disposed)
            return;

        if (value == null)
        {
            if (_mirror.TryRemove(key, out _))
                postEvictionCallback?.Invoke(key);
            RunWriteThrough(WriteThroughCoreAsync(key, storeValue: null, absoluteExpirationRelativeToNow), key);
            return;
        }

        _mirror[key] = new CacheEntry(value, postEvictionCallback);
        RunWriteThrough(WriteThroughCoreAsync(key, AdaptValue(key, value), absoluteExpirationRelativeToNow), key);
    }

    /// <inheritdoc />
    /// <remarks>失效语义：镜像移除 + 写穿 store 删除（登出 / 令牌失效必须落到持久层，防 C6）。
    /// 与 <see cref="MemoryCacheTokenCache{T}.TryRemove"/> 一致，显式移除不触发驱逐回调。</remarks>
    public bool TryRemove(string key, out T? removed)
    {
        if (_disposed)
        {
            removed = default;
            return false;
        }

        if (_mirror.TryRemove(key, out var entry))
        {
            removed = entry.Value;
            RunWriteThrough(WriteThroughCoreAsync(key, storeValue: null, absoluteExpirationRelativeToNow: null), key);
            return true;
        }

        removed = default;
        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 仅对<b>镜像</b>做 LRU 压缩（store 侧过期由其自身 TTL 负责，不回删 —— 镜像驱逐是内存压力行为，
    /// 不是失效语义）。被驱逐条目会触发其登记的驱逐回调（若在 Set 时传入）。
    /// </remarks>
    public void Compact(double percentage)
    {
        if (_disposed || percentage <= 0)
            return;

        if (percentage >= 1.0)
        {
            Evict(_mirror.Keys.ToList());
            return;
        }

        var targetRemoval = (int)(_mirror.Count * percentage);
        if (targetRemoval <= 0)
            return;

        var keysToRemove = _mirror
            .OrderBy(kvp => Interlocked.Read(ref kvp.Value.LastAccessTicks))
            .Take(targetRemoval)
            .Select(kvp => kvp.Key)
            .ToList();
        Evict(keysToRemove);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 清空镜像并写穿删除：租户维度调用 <see cref="ITokenStore.ClearAsync"/>；
    /// 用户维度<b>逐键</b>映射为按 (userId, tokenType) 删除 —— 不调用 <see cref="IUserTokenStore"/> 继承的
    /// <see cref="ITokenStore.ClearAsync"/>（其语义是清空全部用户，误伤面不可接受，见治理方案 §5.3.1 ④）。
    /// </remarks>
    public void Clear()
    {
        if (_disposed)
            return;

        var keys = _mirror.Keys.ToList();
        _mirror.Clear();

        if (_userStore != null)
        {
            // 用户维度：逐键按 (userId, tokenType) 删除，绝不清空全部用户
            foreach (var key in keys)
            {
                RunWriteThrough(WriteThroughCoreAsync(key, storeValue: null, absoluteExpirationRelativeToNow: null), key);
            }
            return;
        }

        RunWriteThrough(_store!.ClearAsync(), keys.Count > 0 ? keys[0] : string.Empty);
    }

    /// <inheritdoc />
    /// <remarks>默认不释放内层 store（不变量 #2）；仅当构造时显式传入 <c>ownsInnerStore: true</c> 且内层实现了
    /// <see cref="IDisposable"/> 才代管释放。</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _mirror.Clear();
        // ns2.0 无 ConcurrentQueue.Clear()：逐出排空
        while (_compensation.TryDequeue(out _))
        {
        }

        if (_ownsInnerStore)
        {
            (_store as IDisposable)?.Dispose();
            (_userStore as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// 租户维度冷启动水合：枚举 store 的全部 tokenType，经 <c>valueFactory</c> 构建缓存值后放入镜像。
    /// </summary>
    /// <param name="storeKeyToCacheKey">store tokenType → cacheKey 的反映射（水合方向）；null = 恒等映射。
    /// 需与构造时的 <c>storeKeyMapper</c> 互逆，否则镜像键与写穿键不成对。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功水合的条目数。</returns>
    /// <remarks>水合不覆盖镜像中已存在的键（本地在途新值优先，<c>TryAdd</c> 原子语义）。
    /// 剩余有效期语义见 <see cref="TokenStoreValue"/> 的 remarks（store 读契约不回传 TTL，由值工厂决定）。</remarks>
    public async Task<int> HydrateAsync(Func<string, string>? storeKeyToCacheKey = null, CancellationToken cancellationToken = default)
    {
        if (_store == null || _disposed)
            return 0;   // 用户维度无全量枚举契约，请改用 HydrateUserAsync

        var tokenTypes = await _store.GetTokenTypesAsync(cancellationToken).ConfigureAwait(false) ?? Enumerable.Empty<string>();
        var hydrated = 0;
        foreach (var tokenType in tokenTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cacheKey = storeKeyToCacheKey != null ? storeKeyToCacheKey(tokenType) : tokenType;
            if (await ReadAndHydrateAsync(cacheKey, tokenType, userId: null, cancellationToken).ConfigureAwait(false))
                hydrated++;
        }
        return hydrated;
    }

    /// <summary>
    /// 租户维度单条水合：读取 store 中 <paramref name="tokenType"/> 槽位的令牌，放入镜像的 <paramref name="cacheKey"/> 键下。
    /// </summary>
    /// <param name="cacheKey">镜像侧缓存键。</param>
    /// <param name="tokenType">store 侧令牌类型键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>store 中存在可构建的令牌且已入镜像则为 true。</returns>
    public Task<bool> HydrateEntryAsync(string cacheKey, string tokenType, CancellationToken cancellationToken = default)
        => ReadAndHydrateAsync(cacheKey, tokenType, userId: null, cancellationToken);

    /// <summary>
    /// 用户维度冷启动水合：枚举指定用户的全部 tokenType，逐条构建缓存值放入镜像。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="tokenTypeToCacheKey">store tokenType → cacheKey 的反映射；null = 恒等映射。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功水合的条目数。</returns>
    public async Task<int> HydrateUserAsync(string userId, Func<string, string>? tokenTypeToCacheKey = null, CancellationToken cancellationToken = default)
    {
        if (_userStore == null || _disposed)
            return 0;   // 租户维度请改用 HydrateAsync

        var tokenTypes = await _userStore.GetTokenTypesAsync(userId, cancellationToken).ConfigureAwait(false) ?? Enumerable.Empty<string>();
        var hydrated = 0;
        foreach (var tokenType in tokenTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cacheKey = tokenTypeToCacheKey != null ? tokenTypeToCacheKey(tokenType) : tokenType;
            if (await ReadAndHydrateAsync(cacheKey, tokenType, userId, cancellationToken).ConfigureAwait(false))
                hydrated++;
        }
        return hydrated;
    }

    /// <summary>
    /// 用户维度单条水合：读取 store 中 (userId, tokenType) 槽位的令牌，放入镜像的 cacheKey 键下。
    /// </summary>
    /// <param name="cacheKey">镜像侧缓存键。</param>
    /// <param name="userId">用户标识（显式给出 —— 水合方向的镜像键由 tokenType 派生，不能反解 userId）。</param>
    /// <param name="tokenType">store 侧令牌类型键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>store 中存在可构建的令牌且已入镜像则为 true。</returns>
    public Task<bool> HydrateUserEntryAsync(string cacheKey, string userId, string tokenType, CancellationToken cancellationToken = default)
    {
        if (_userStore == null || _disposed)
            return Task.FromResult(false);

        return ReadAndHydrateAsync(cacheKey, tokenType, userId, cancellationToken);
    }

    /// <summary>
    /// 重放补偿队列中写穿失败的条目（不变量 #5 的恢复动作）。可在宿主的定时器 / 健康检查中周期调用。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次重放中写穿成功的条目数（仍失败的条目保留在队列中）。</returns>
    public async Task<int> RetryFailedWritesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _compensation.IsEmpty)
            return 0;

        var retryBatch = new List<string>();
        while (_compensation.TryDequeue(out var key))
        {
            retryBatch.Add(key);
        }

        var succeeded = 0;
        foreach (var key in retryBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_mirror.TryGetValue(key, out var entry))
                continue;   // 镜像已无此键：删除意图已由删除路径自行写穿，无东西可补发

            // 镜像条目仅以非 null 值创建（Set/SetAsync 的 null 分支走删除路径），此处恒非空
            var storeValue = AdaptValue(key, entry.Value!);
            if (storeValue == null)
                continue;

            try
            {
                await WriteThroughCoreAsync(key, storeValue.Value, absoluteExpirationRelativeToNow: null).ConfigureAwait(false);
                succeeded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Interlocked.Increment(ref _writeThroughFailures);
                EnqueueCompensation(key);
                _logger.LogWarning(ex, "令牌写穿补偿重放仍失败（Key={Key}），已保留在补偿队列", MessageSanitizer.MaskIdentifier(key));
            }
        }

        return succeeded;
    }

    // ---- S2：IAsyncTokenCache<T> 真穿透 ----

    /// <inheritdoc />
    /// <remarks>
    /// 读穿透：<b>镜像优先</b>（命中即返回，保住热路径零 I/O）；镜像未命中时直达 store 读取并回填镜像
    /// —— 冷启动无需先水合，多实例下其他实例写入的数据在本地未命中时即可读到。
    /// 镜像命中但 store 已被其他实例轮换的窗口仍存在（方案 B 固有限制，见类 remarks）。
    /// <paramref name="key"/> 必须与写入方向同用一套键映射（用户维度按 <c>userKeyMapper</c> 反解
    /// (userId, tokenType)；租户维度按 <c>storeKeyMapper</c> 映射），键映射是宿主的一致性责任。
    /// </remarks>
    public async ValueTask<T?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return null;

        if (_mirror.TryGetValue(key, out var entry))
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks - entry.LastAccessTicks >= TimeSpan.TicksPerSecond)
                Interlocked.Exchange(ref entry.LastAccessTicks, nowTicks);
            return entry.Value;
        }

        string? access, refresh;
        if (_userStore != null)
        {
            var (userId, tokenType) = _userKeyMapper!(key);
            access = await _userStore.GetAccessTokenAsync(userId, tokenType, cancellationToken).ConfigureAwait(false);
            refresh = await _userStore.GetRefreshTokenAsync(userId, tokenType, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var storeKey = MapStoreKey(key);
            access = await _store!.GetAccessTokenAsync(storeKey, cancellationToken).ConfigureAwait(false);
            refresh = await _store.GetRefreshTokenAsync(storeKey, cancellationToken).ConfigureAwait(false);
        }

        if (access == null && refresh == null)
            return null;

        var value = _valueFactory(new TokenStoreValue(access, refresh));
        if (value == null)
            return null;

        _mirror.TryAdd(key, new CacheEntry(value));
        return value;
    }

    /// <inheritdoc />
    /// <remarks>与同步 <see cref="Set(string,T,System.TimeSpan?,System.TimeSpan?,System.Action{string}?)"/> 相同的
    /// 失效 / TTL 推导语义（value 为 null 或值适配器返回 null 均按<b>删除该键</b>写穿），但<b>等待</b>写穿完成
    /// （await 而非后台续体）；任何写穿失败（含删除）均走补偿队列，不抛出。</remarks>
    public async ValueTask SetAsync(string key, T? value, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        if (value == null)
        {
            _mirror.TryRemove(key, out _);
            try
            {
                await WriteThroughCoreAsync(key, storeValue: null, absoluteExpirationRelativeToNow: null).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                HandleWriteFailure(key, ex);
            }
            return;
        }

        _mirror[key] = new CacheEntry(value);
        var storeValue = AdaptValue(key, value);
        if (storeValue == null)
        {
            // 与同步 Set 一致（§5.3.1 ②）：值适配器返回 null = 删除该键的持久层状态，
            // 而非静默 no-op —— 否则异步路径会让已失效条目残留在 store 中（登出/失效覆盖缺口）。
            try
            {
                await WriteThroughCoreAsync(key, storeValue: null, absoluteExpirationRelativeToNow: null).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                HandleWriteFailure(key, ex);
            }
            return;
        }

        try
        {
            await WriteThroughCoreAsync(key, storeValue.Value, absoluteExpirationRelativeToNow: null).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HandleWriteFailure(key, ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>镜像移除 + <b>等待</b> store 删除（失效必须落到持久层，防 C6），返回被移除的值。</remarks>
    public async ValueTask<T?> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return null;

        _mirror.TryRemove(key, out var removed);
        try
        {
            await WriteThroughCoreAsync(key, storeValue: null, absoluteExpirationRelativeToNow: null).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HandleWriteFailure(key, ex);
        }

        return removed?.Value;
    }

    // ---- 内部实现 ----

    private async Task<bool> ReadAndHydrateAsync(string cacheKey, string tokenType, string? userId, CancellationToken cancellationToken)
    {
        string? access, refresh;
        if (userId != null)
        {
            // 用户维度：userId 显式贯穿（水合方向的镜像键由 tokenType 派生，不可反解 userId），只走带 userId 的成员（C7）
            access = await _userStore!.GetAccessTokenAsync(userId, tokenType, cancellationToken).ConfigureAwait(false);
            refresh = await _userStore.GetRefreshTokenAsync(userId, tokenType, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            access = await _store!.GetAccessTokenAsync(tokenType, cancellationToken).ConfigureAwait(false);
            refresh = await _store.GetRefreshTokenAsync(tokenType, cancellationToken).ConfigureAwait(false);
        }

        if (access == null && refresh == null)
            return false;

        var value = _valueFactory(new TokenStoreValue(access, refresh));
        if (value == null)
            return false;

        _mirror.TryAdd(cacheKey, new CacheEntry(value));
        return true;
    }

    private TokenStoreValue? AdaptValue(string key, T value)
    {
        try
        {
            return _valueAdapter(value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Increment(ref _writeThroughFailures);
            _logger.LogWarning(ex, "令牌写穿值适配失败（Key={Key}），本次仅更新内存镜像", MessageSanitizer.MaskIdentifier(key));
            return null;
        }
    }

    private void WarnIfInnerStoreEncrypted(ITokenStore inner)
    {
        if (inner is IEncryptedTokenStore { IsEncryptionEnabled: true })
        {
            IsInnerStoreEncrypted = true;
            MudHttpClientLog.TokenStoreBridgeDoubleEncryptionDetected(_logger, inner.GetType().Name);
        }
    }

    /// <summary>
    /// 写穿核心：删除（storeValue == null）或按维度写入。异常上抛，由 <see cref="RunWriteThrough"/> / 重放路径统一处置。
    /// </summary>
    private async Task WriteThroughCoreAsync(string key, TokenStoreValue? storeValue, TimeSpan? absoluteExpirationRelativeToNow)
    {
        if (_userStore != null)
        {
            var (userId, tokenType) = _userKeyMapper!(key);
            if (storeValue == null)
            {
                await _userStore.RemoveAsync(userId, tokenType).ConfigureAwait(false);
                return;
            }
            await WriteUserStoreValueAsync(userId, tokenType, storeValue.Value).ConfigureAwait(false);
            return;
        }

        var storeKey = MapStoreKey(key);
        if (storeValue == null)
        {
            await _store!.RemoveAsync(storeKey).ConfigureAwait(false);
            return;
        }
        await WriteStoreValueAsync(_store!, storeKey, storeValue.Value, absoluteExpirationRelativeToNow).ConfigureAwait(false);
    }

    private async Task WriteStoreValueAsync(ITokenStore store, string storeKey, TokenStoreValue value, TimeSpan? absoluteExpirationRelativeToNow)
    {
        if (value.AccessToken != null)
        {
            // TTL 推导（不变量 #4 / C1）：优先取值适配器从令牌自身过期字段推导的剩余时长（与管线判定同源），
            // Set 的绝对过期仅作回退；皆缺省则跳过访问令牌写穿（宁缺勿滥，防 store 以无界 TTL 滞留令牌）。
            var expiresInSeconds = value.ExpiresInSeconds > 0
                ? value.ExpiresInSeconds
                : absoluteExpirationRelativeToNow.HasValue
                    ? (long)absoluteExpirationRelativeToNow.Value.TotalSeconds
                    : 0;

            if (expiresInSeconds > 0)
            {
                await store.SetAccessTokenAsync(storeKey, value.AccessToken, expiresInSeconds).ConfigureAwait(false);
            }
            else
            {
                MudHttpClientLog.TokenStoreBridgeWriteSkippedForMissingTtl(_logger, MessageSanitizer.MaskIdentifier(storeKey));
            }
        }

        if (value.RefreshToken != null)
        {
            await store.SetRefreshTokenAsync(storeKey, value.RefreshToken).ConfigureAwait(false);
        }
    }

    private async Task WriteUserStoreValueAsync(string userId, string tokenType, TokenStoreValue value)
    {
        if (value.AccessToken != null)
        {
            if (value.ExpiresInSeconds > 0)
            {
                await _userStore!.SetAccessTokenAsync(userId, tokenType, value.AccessToken, value.ExpiresInSeconds).ConfigureAwait(false);
            }
            else
            {
                MudHttpClientLog.TokenStoreBridgeWriteSkippedForMissingTtl(_logger, MessageSanitizer.MaskIdentifier(tokenType));
            }
        }

        if (value.RefreshToken != null)
        {
            await _userStore!.SetRefreshTokenAsync(userId, tokenType, value.RefreshToken).ConfigureAwait(false);
        }
    }

    private void RunWriteThrough(Task operation, string key)
    {
        // 同步完成的 store（如 MemoryTokenStore 的 Task.FromResult）当帧收尾 —— 契约测试确定性；
        // 真异步 store（如 Redis）走后台续体，失败进补偿队列（不变量 #5）。
        if (operation.Status == TaskStatus.RanToCompletion)
            return;

        if (operation.Status == TaskStatus.Faulted || operation.Status == TaskStatus.Canceled)
        {
            HandleWriteFailure(key, operation.Exception?.GetBaseException()
                ?? new OperationCanceledException("令牌写穿任务已取消"));
            return;
        }

        Interlocked.Increment(ref _pendingWrites);
        _ = operation.ContinueWith(
            t =>
            {
                Interlocked.Decrement(ref _pendingWrites);
                if (t.Status != TaskStatus.RanToCompletion)
                {
                    HandleWriteFailure(key, t.Exception?.GetBaseException()
                        ?? new OperationCanceledException("令牌写穿任务已取消"));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void HandleWriteFailure(string key, Exception? exception)
    {
        Interlocked.Increment(ref _writeThroughFailures);
        EnqueueCompensation(key);
        _logger.LogWarning(exception, "令牌写穿失败，已进入补偿队列（Key={Key}，当前待重放 {Pending}）",
            MessageSanitizer.MaskIdentifier(key), _compensation.Count);
    }

    private void EnqueueCompensation(string key)
    {
        if (_compensation.Count >= CompensationQueueLimit)
        {
            Interlocked.Increment(ref _droppedCompensations);
            return;
        }
        _compensation.Enqueue(key);
    }

    private void Evict(List<string> keys)
    {
        foreach (var key in keys)
        {
            if (_mirror.TryRemove(key, out var entry))
                entry.PostEvictionCallback?.Invoke(key);
        }
    }

    private string MapStoreKey(string cacheKey)
        => _storeKeyMapper != null ? _storeKeyMapper(cacheKey) : cacheKey;

    /// <summary>镜像条目：值 + LRU 最后访问时间 + 驱逐回调（桥接器自行驱逐/移除时触发）。</summary>
    private sealed class CacheEntry
    {
        public T? Value;
        public long LastAccessTicks;
        public Action<string>? PostEvictionCallback;

        public CacheEntry(T value, Action<string>? postEvictionCallback = null)
        {
            Value = value;
            LastAccessTicks = DateTime.UtcNow.Ticks;
            PostEvictionCallback = postEvictionCallback;
        }
    }
}
