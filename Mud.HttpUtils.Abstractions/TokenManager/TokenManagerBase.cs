// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Observability;
using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌管理器抽象基类，提供并发安全的令牌刷新实现。
/// </summary>
/// <remarks>
/// <para><b>Dispose(bool) 契约（SR-H1，P1.3）</b>：</para>
/// <list type="number">
/// <item><description><see cref="Dispose(bool)"/> 可重入、幂等；不以 <c>_disposed</c> 早退（派生类置位后仍须执行基类释放）。</description></item>
/// <item><description>派生类覆写时应在开头自行检查/置位 <c>_disposed</c>（保留 NEW-TM-11 快速感知语义），且无论标志状态如何都必须调用 <c>base.Dispose(disposing)</c>。</description></item>
/// <item><description>各释放步骤自身幂等。</description></item>
/// </list>
/// </remarks>
public abstract class TokenManagerBase : ITokenManager, IDisposable
{
    private readonly ITokenCache<CredentialToken> _tokenCache;
    // P2.2（TK-05/09/24）键控锁表统一管理作用域锁，替代原有的 ConcurrentDictionary<string, Lazy<SemaphoreSlim>>。
    private readonly KeyedLockTable _keyedLockTable = new();
    // SR-L9（P3.10，D14-V5）：可空——仅 SupportsTenantMaintenance=true 时分配（用户管理器跳过，避免 NRE 面不置 null 释放路径）。
    private readonly Timer? _cleanupTimer;
    private readonly Timer? _lockCleanupTimer;
    private readonly object _cleanupLock = new();
    // P1.3（TK-04）降级令牌连续命中的计数，用于指数退避；成功刷新时复位为 0。
    private int _consecutiveFallbacks;

    // TMX-04：刷新失败负缓存——窗口内同 scopeKey 的等待者直接重抛同一失败，不再发起刷新。
    // 键为 scopeKey，值为 (Exception, UntilMs)。窗口过期后条目在 CleanupExpiredTokens 中被清理。
    private readonly ConcurrentDictionary<string, FailedRefresh> _recentFailures = new(StringComparer.Ordinal);

    /// <summary>
    /// TMX-04：刷新失败负缓存窗口（秒），默认 5；0 = 关闭（恢复"每个等待者各刷一次"的旧行为）。
    /// </summary>
    protected virtual int NegativeCacheSeconds => 5;

    /// <summary>
    /// TMX-04：负缓存条目（异常 + 过期时间）。
    /// </summary>
    private readonly struct FailedRefresh
    {
        public readonly Exception Exception;
        public readonly long UntilMs;
        public FailedRefresh(Exception ex, long untilMs) { Exception = ex; UntilMs = untilMs; }
    }
    /// <summary>
    /// 指示对象是否已释放。
    /// </summary>
    protected volatile bool _disposed;

    /// <summary>
    /// 默认作用域键（<c>"default"</c>）。当调用方未指定作用域时，令牌缓存按此键归属。
    /// </summary>
    protected const string DefaultScopeKey = "default";
    private const int CleanupIntervalSeconds = 300;
    private const int LockCleanupIntervalSeconds = 600;

    /// <summary>
    /// 令牌刷新失败事件。
    /// </summary>
    public event EventHandler<TokenRefreshFailedEventArgs>? RefreshFailed;

    /// <summary>
    /// 获取令牌刷新失败时的最大重试次数，默认 0（不重试）。
    /// P2.10（TK-20）重试语义显式化：此值是重试的唯一主控门，
    /// ShouldRetry（TokenRefreshFailedEventArgs）默认为 true 仅用于提前取消；
    /// 子类覆写此属性或事件处理器设 ShouldRetry=false 与 MaxRefreshRetryCount 正交协作。
    /// </summary>
    protected virtual int MaxRefreshRetryCount => 0;

    /// <summary>
    /// 获取令牌刷新重试间隔（毫秒），默认 1000ms。
    /// </summary>
    protected virtual int RefreshRetryDelayMilliseconds => 1000;

    /// <summary>
    /// 令牌过期提前量的默认值（秒）。<c>UserTokenCacheOptions.DefaultExpireThresholdSeconds</c> 引用此常量以保持跨层一致。
    /// </summary>
    public const int DefaultExpireThresholdSeconds = 300;

    /// <summary>
    /// 获取令牌过期提前量（秒），默认 <see cref="DefaultExpireThresholdSeconds"/>（300 秒 = 5 分钟）。
    /// 令牌在此时间内即将过期时将触发自动刷新。
    /// </summary>
    protected virtual int ExpireThresholdSeconds => DefaultExpireThresholdSeconds;

    /// <summary>
    /// 降级令牌的额外宽限（秒），默认 60。确保降级条目在缓存层判定为可用，
    /// 避免因 <c>expire - threshold == now</c> 的严格比较边界而复现刷新风暴。
    /// </summary>
    protected virtual int FallbackGraceSeconds => 60;

    /// <summary>
    /// 降级指数退避的增量上限（秒），默认 300。注意此为"退避增量"上限而非令牌总有效期上限；
    /// 实际降级令牌有效期 = <see cref="ExpireThresholdSeconds"/> + 退避增量。
    /// </summary>
    protected virtual int MaxFallbackLifetimeSeconds => 300;

    /// <summary>
    /// 指示此令牌管理器是否支持后台主动刷新。默认为 <c>true</c>。
    /// 子类可重写为 <c>false</c> 以声明不支持后台刷新（如用户令牌管理器，
    /// 其令牌通过 OAuth 授权码按需获取，不适合后台预热刷新）。
    /// 后台刷新服务应检查此属性，避免注册不支持后台刷新的令牌管理器。
    /// </summary>
    public virtual bool SupportsBackgroundRefresh => true;

    /// <summary>
    /// 获取作用域缓存的最大容量，默认 64。超过此容量时将清理过期条目。
    /// </summary>
    protected virtual int MaxScopeCacheSize => 64;

    /// <summary>
    /// 获取缓存令牌的最大存活时间（秒），默认 86400 秒（24 小时）。
    /// 即使远端返回的过期时间异常大，缓存条目也不会超过此时间。
    /// </summary>
    protected virtual int MaxCacheLifetimeSeconds => 86400;

    /// <summary>
    /// 获取此令牌管理器在可观测性（指标 tag、Activity、健康检查）中使用的键。
    /// P3.4（C4，TK-23）指标键可配置：默认回落到 CLR 类型名 <see cref="object.GetType"/>().Name。
    /// 当多个逻辑上不同的管理器共享同一实现类型、或同一类型多实例需要区隔维度时，
    /// 子类可覆写本属性返回可区分（且稳定）的键，例如 DI 注册名、配置区段名或业务键。
    /// 覆写时应保证返回值稳定且不含敏感信息，因为它会作为指标维度/日志维度被持久化。
    /// </summary>
    protected virtual string MetricsKey => GetType().Name;

    /// <inheritdoc />
    protected TokenManagerBase()
        : this(new ConcurrentDictionaryTokenCache<CredentialToken>())
    {
    }

    /// <summary>
    /// 使用自定义令牌缓存初始化令牌管理器。
    /// </summary>
    /// <param name="tokenCache">令牌缓存实现。</param>
    protected TokenManagerBase(ITokenCache<CredentialToken> tokenCache)
    {
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
        // SR-L9（P3.10，D14-V5）仅当管理器声明支持租户维护时才启动两个 Timer；
        // 用户令牌管理器覆写 SupportsTenantMaintenance=false 跳过（其令牌经 IMemoryCache 自带过期）。
        if (SupportsTenantMaintenance)
        {
            _cleanupTimer = new Timer(CleanupExpiredTokens, null,
                TimeSpan.FromSeconds(CleanupIntervalSeconds),
                TimeSpan.FromSeconds(CleanupIntervalSeconds));
            _lockCleanupTimer = new Timer(CleanupUnusedLocks, null,
                TimeSpan.FromSeconds(LockCleanupIntervalSeconds),
                TimeSpan.FromSeconds(LockCleanupIntervalSeconds));
        }
    }

    /// <inheritdoc />
    public abstract Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    /// <remarks>
    /// TMX-07：默认实现走 scope 感知路径（与 <see cref="GetOrRefreshTokenAsync(string[], CancellationToken)"/> 一致），
    /// 不再静默返回默认作用域令牌。不支持 scope 的派生类应覆写并抛 <see cref="NotSupportedException"/>
    /// （MT-11：否则调用方会静默拿到默认作用域令牌，构成 scope 错配 / 潜在越权风险；
    /// 支持按作用域取令牌的子类<b>必须覆写本重载</b>，<c>StandardOAuth2TokenManager</c>（Client 程序集）已覆写）。
    /// </remarks>
    public virtual Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
    {
        return GetOrRefreshTokenAsync(scopes, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        return await GetOrRefreshTokenAsync((string[]?)null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>取消传播语义（TMX-15-11 / B9）</b>：直接调用路径下，首个调用方的 <paramref name="cancellationToken"/>
    /// 会传入锁内刷新操作。若该调用方取消，则正在进行的共享刷新将被中止，其余等待者将在各自重试时发起新的刷新。
    /// 此行为是"尊重调用方取消"的有意设计，并非缺陷。</para>
    /// <para>401 恢复路径已做取消隔离（<c>TokenRecoveryExecutor</c> 使用独立 CT），不受此语义影响。</para>
    /// <para><b>MT-27 实现约定（子类必读）</b>：</para>
    /// <list type="bullet">
    /// <item><description>本方法在<b>持有作用域键控锁（<see cref="KeyedLockTable"/>）期间</b>调用
    /// <see cref="RefreshTokenCoreAsync"/> / <see cref="RefreshTokenWithScopesAsync"/>。</description></item>
    /// <item><description>该锁基于 <see cref="SemaphoreSlim"/>，<b>不可重入</b>：
    /// 刷新实现内部<b>不得</b>再次调用本管理器的 <see cref="GetOrRefreshTokenAsync(string[], CancellationToken)"/>
    /// 或 <see cref="GetTokenAsync(CancellationToken)"/>，否则将<b>自锁死</b>（同线程永久等待自己持有的信号量）。</description></item>
    /// <item><description>需要复用已缓存令牌时，请在刷新实现内使用 <see cref="GetCachedCredentialToken()"/> /
    /// <see cref="GetCachedCredentialToken(string)"/> 等<b>不加锁</b>的读取入口。</description></item>
    /// <item><description>取消语义：若取消发生在"等待锁"阶段，锁不会被获取，缓存保持原状；
    /// 若发生在"持有锁刷新"阶段，异常向上传播且<b>不会写入半成品缓存</b>（写入仅在刷新成功后执行）。</description></item>
    /// </list>
    /// </remarks>
    public virtual async Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var scopeKey = GetScopeKey(scopes);

        // TM-02 修复：使用 TryGetValidToken 合并 IsTokenValid + TryGet 为单次查找，避免冗余的双字典访问
        if (TryGetValidToken(scopeKey, out var fastToken))
        {
            return fastToken!.AccessToken!;
        }

        // P2.2（TK-05/09/24）从键控锁表获取作用域锁
        using (var releaser = await _keyedLockTable.AcquireAsync(scopeKey, cancellationToken).ConfigureAwait(false))
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            if (TryGetValidToken(scopeKey, out var lockedToken))
            {
                return lockedToken!.AccessToken!;
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var negativeCacheSeconds = NegativeCacheSeconds;

            // TMX-04：窗口内复用上次失败，阻断"等待者串行各刷一次"
            if (negativeCacheSeconds > 0
                && _recentFailures.TryGetValue(scopeKey, out var failed)
                && failed.UntilMs > nowMs)
            {
                MudHttpMeter.TokenRefreshSuppressedCounter.Add(1,
                    MudHttpMeter.FilterTags(new KeyValuePair<string, object?>[] { new("token_manager_key", MetricsKey) }));
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failed.Exception).Throw();
            }

            CredentialToken token;
            try
            {
                token = await RefreshTokenWithRetryCoreAsync(
                    scopes == null || scopes.Length == 0
                        ? ct => RefreshTokenCoreAsync(ct)
                        : ct => RefreshTokenWithScopesAsync(scopes, ct),
                    cancellationToken).ConfigureAwait(false);
                if (negativeCacheSeconds > 0) _recentFailures.TryRemove(scopeKey, out _);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)   // 取消不是"刷新失败"
            {
                if (negativeCacheSeconds > 0)
                    _recentFailures[scopeKey] = new FailedRefresh(ex, nowMs + negativeCacheSeconds * 1000L);
                throw;
            }

            if (token == null || string.IsNullOrEmpty(token.AccessToken))
                throw new InvalidOperationException($"令牌刷新返回了无效的凭证：AccessToken 为空。（ScopeKey={scopeKey}）");

            UpdateToken(scopeKey, token);
            // TM-05 修复：token 已是有效凭证，无需再次字典查找，直接返回。
            return token.AccessToken!;
        }
    }

    /// <inheritdoc />
    public virtual async Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var scopeKey = GetScopeKey(scopes);
        using (var releaser = await _keyedLockTable.AcquireAsync(scopeKey, cancellationToken).ConfigureAwait(false))
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            _tokenCache.TryRemove(scopeKey, out var removed);
            if (removed == null || string.IsNullOrEmpty(removed.AccessToken))
                return TokenResult.Empty;

            return new TokenResult(removed.AccessToken!, removed.Expire, scopeKey);
        }
    }

    /// <summary>
    /// 刷新令牌的核心实现，由子类实现具体的刷新逻辑。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>刷新后的凭证令牌。</returns>
    protected abstract Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 刷新指定作用域的令牌。默认实现忽略 scopes 调用 <see cref="RefreshTokenCoreAsync"/>，
    /// 子类可重写此方法以支持基于 Scope 的令牌刷新。
    /// </summary>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>刷新后的凭证令牌。</returns>
    protected virtual Task<CredentialToken> RefreshTokenWithScopesAsync(string[]? scopes, CancellationToken cancellationToken)
    {
        return RefreshTokenCoreAsync(cancellationToken);
    }

    /// <summary>
    /// 生成作用域缓存键。null 或空数组返回 "default"。
    /// SR-M5（P3.3，D11）规范化统一委托 <see cref="ScopeKeyBuilder"/>（Distinct/Ordinal/排序）单一实现。
    /// 行为差异（{"a","a"} 从 "a,a" 变 "a"、大小写不同 scope 不再分裂）属键规范化，记入变更说明。
    /// </summary>
    /// <param name="scopes">令牌作用域数组。</param>
    /// <returns>缓存键字符串。</returns>
    protected string GetScopeKey(string[]? scopes)
    {
        return ScopeKeyBuilder.Build(scopes);
    }

    /// <summary>
    /// 更新指定作用域的缓存令牌信息。
    /// </summary>
    /// <param name="scopeKey">作用域缓存键。</param>
    /// <param name="token">凭证令牌。</param>
    protected void UpdateScopedToken(string scopeKey, CredentialToken token)
    {
        UpdateToken(scopeKey, token);
    }

    /// <summary>
    /// 设置缓存的令牌信息（用于初始化或外部更新）。
    /// </summary>
    /// <param name="accessToken">访问令牌。</param>
    /// <param name="expireTime">过期时间（Unix 时间戳，毫秒）。</param>
    protected void SetCachedToken(string accessToken, long expireTime)
    {
        _tokenCache.Set(DefaultScopeKey, new CredentialToken
        {
            AccessToken = accessToken,
            Expire = expireTime
        });
    }

    /// <summary>
    /// 从缓存中获取默认作用域的令牌信息，如果不存在或已过期则返回 null。
    /// P2.3（TK-02）保留兼容层：委托 <see cref="GetCachedCredentialToken(string)"/> 读取默认作用域。
    /// </summary>
    /// <returns>缓存中的令牌信息，如果不存在或已过期则返回 null。</returns>
    protected CredentialToken? GetCachedCredentialToken()
    {
        return GetCachedCredentialToken(DefaultScopeKey);
    }

    /// <summary>
    /// P2.3（TK-02）从缓存中获取指定作用域的令牌信息，如果不存在或已过期则返回 null。
    /// 按 scopeKey 隔离刷新链路，避免用默认作用域的 refresh_token 去刷新任意 scope（跨作用域污染）。
    /// </summary>
    /// <param name="scopeKey">作用域缓存键（由 <see cref="GetScopeKey(string[])"/> 生成）。</param>
    /// <returns>缓存中的令牌信息，如果不存在或已过期则返回 null。</returns>
    protected CredentialToken? GetCachedCredentialToken(string scopeKey)
    {
        if (_tokenCache.TryGet(scopeKey, out var token))
            return token;
        return null;
    }

    /// <summary>
    /// SR-M2（P2.3，D8）仅失效缓存条目的 refresh_token，其余字段（AccessToken 等）保留。
    /// 供 invalid_grant 清除回退使用：可疑 refresh_token 被消费后不得继续滞留缓存被后续请求复用。
    /// </summary>
    /// <param name="scopeKey">作用域缓存键。</param>
    protected void InvalidateCachedRefreshToken(string scopeKey)
    {
        if (_tokenCache.TryGet(scopeKey, out var existing) && existing != null)
        {
            existing.RefreshToken = null;
            existing.RefreshTokenExpire = 0;
            _tokenCache.Set(scopeKey, existing);
        }
    }

    /// <summary>
    /// TR-02 仅失效缓存条目的访问令牌字段（AccessToken / Expire / IssuedAt），
    /// 保留 refresh_token / refresh_token_expire / scope。
    /// 供 401 恢复链路使用：恢复的目标是"重新拿一个访问令牌"，而非"作废该作用域的全部凭据"
    /// （后者会销毁可用于自愈的 refresh_token，迫使走 client_credentials 或重新授权）。
    /// 与 <see cref="InvalidateCachedRefreshToken"/> 构成对称的字段级失效对，两者互不代偿。
    /// </summary>
    /// <param name="scopeKey">作用域缓存键。</param>
    internal void InvalidateCachedAccessToken(string scopeKey)
    {
        if (_tokenCache.TryGet(scopeKey, out var existing) && existing != null)
        {
            existing.AccessToken = null;
            existing.Expire = 0;
            existing.IssuedAt = 0;
            _tokenCache.Set(scopeKey, existing);   // 字段级保留 refresh_token
        }
    }

    /// <summary>TR-02 按作用域失效访问令牌（null/空数组 ⇒ 默认作用域）。</summary>
    /// <param name="scopes">令牌作用域数组。</param>
    internal void InvalidateCachedAccessToken(string[]? scopes)
        => InvalidateCachedAccessToken(GetScopeKey(scopes));

    /// <summary>
    /// SR-H5（P2.1，D6）租户绑定键（bind-once）。null = 尚未绑定。
    /// 绑定键 = <c>IMudAppContext.AppKey</c>（多租户在本框架的投影即多 App）。
    /// </summary>
    private string? _tenantBinding;

    /// <summary>
    /// SR-H5（P2.1，D6）是否启用租户绑定守卫（防止单实例跨租户共享导致凭据错配）。默认 true。
    /// 所有租户共享同一 IdP 凭据且令牌无租户属性的合法场景，可覆写为 false（需自证凭据无租户属性）。
    /// </summary>
    protected virtual bool EnforceTenantBinding => true;

    /// <summary>
    /// SR-H5（P2.1，D6）bind-once 租户绑定守卫：首个租户键绑定后，不同租户键的请求被拒绝。
    /// 同键重复绑定幂等通过。internal：仅框架调用点（<c>DefaultTokenProvider</c>）触发，不进公共 API。
    /// </summary>
    /// <param name="tenantKey">租户键（AppKey）。</param>
    /// <exception cref="InvalidOperationException">已绑定其他租户且守卫启用时抛出。</exception>
    internal void BindTenantGuard(string tenantKey)
    {
        if (!EnforceTenantBinding || string.IsNullOrEmpty(tenantKey))
            return;
        var existing = Interlocked.CompareExchange(ref _tenantBinding, tenantKey, null);
        if (existing != null && !string.Equals(existing, tenantKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"令牌管理器（{MetricsKey}）已绑定租户 '{existing}'，不能用于租户 '{tenantKey}' 的请求。" +
                "跨租户复用同一管理器实例会导致令牌/凭据错配；若确属共享凭据设计，请覆写 EnforceTenantBinding 返回 false。");
    }

    /// <summary>
    /// L-1：当前已绑定的租户键（null = 尚未绑定）。供恢复链路在守卫拒绝时输出结构化告警。
    /// </summary>
    internal string? BoundTenant => Volatile.Read(ref _tenantBinding);

    /// <summary>
    /// SR-L9（P3.10，D14-V5）是否启动租户层维护 Timer（过期清理 300s / 锁清理 600s）。默认 true。
    /// 用户令牌管理器覆写 false 以跳过其永不使用的租户层 Timer 资源。
    /// <para>注意：仅跳过 Timer 分配；<c>_tokenCache</c> / <c>_keyedLockTable</c> 仍保留分配（避免基类
    /// 公共路径解引用空字段的 NRE 面，见 §0.3-V5 评审修订）。</para>
    /// </summary>
    protected virtual bool SupportsTenantMaintenance => true;

    /// <summary>
    /// 触发令牌刷新失败事件。
    /// NEW-TM-04 修复：捕获订阅者异常，避免掩盖原始令牌刷新失败的根因。
    /// </summary>
    /// <param name="e">事件参数。</param>
    private void OnRefreshFailed(TokenRefreshFailedEventArgs e)
    {
        try
        {
            RefreshFailed?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            // 订阅者异常不应掩盖原始刷新失败，仅记录
            System.Diagnostics.Debug.WriteLine($"TokenManagerBase: RefreshFailed 订阅者抛出异常: {ex.Message}");
        }
    }

    // P1.1（TK-01）凭据字段级合并：刷新响应缺省字段时保留既有凭据，避免静默降级到错误授权流程。
    // 语义与 UserTokenInfo.UpdateFromCredentialToken 对齐（仅当新值缺失/无效时保留旧值，服务端返回的新值必须优先）。
    private void UpdateToken(string scopeKey, CredentialToken? token)
    {
        if (token == null)
        {
            _tokenCache.Set(scopeKey, null);
            return;
        }

        // 字段级合并：读取旧条目，仅当新令牌未提供 refresh_token / refresh_token_expire / scope 时保留旧值。
        if (_tokenCache.TryGet(scopeKey, out var existing) && existing != null)
        {
            if (string.IsNullOrEmpty(token.RefreshToken))
            {
                token.RefreshToken = existing.RefreshToken;
                if (token.RefreshTokenExpire <= 0 && existing.RefreshTokenExpire > 0)
                    token.RefreshTokenExpire = existing.RefreshTokenExpire;
            }
            if (string.IsNullOrEmpty(token.Scope))
                token.Scope = existing.Scope;
        }

        if (token.Expire > 0)
        {
            var maxLifetimeMs = MaxCacheLifetimeSeconds * 1000L;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var effectiveMaxExpire = now + maxLifetimeMs;
            if (token.Expire > effectiveMaxExpire)
            {
                token.Expire = effectiveMaxExpire;
            }
        }

        _tokenCache.Set(scopeKey, token);

        // SR-M5（P3.3，D11）硬上限 LRU 收敛：超限路径从"仅清过期"升级为
        // "清过期 → 仍超限 → 强制 LRU Compact"，阻断攻击者/失控代码以海量 scope 组合
        // 制造无界缓存+锁表膨胀（有效期内 CleanupExpiredTokens 清不掉未过期条目）。
        if (_tokenCache.Count > MaxScopeCacheSize)
        {
            CleanupExpiredTokens(null);
            if (_tokenCache.Count > MaxScopeCacheSize)                    // 清过期后仍超限 → 强制 LRU
            {
                var excess = _tokenCache.Count - MaxScopeCacheSize;
                var ratio = Math.Max(excess / (double)_tokenCache.Count, 0.05);   // 下限 5%，避免 0 取整
                _tokenCache.Compact(ratio);
            }
        }
    }

    private async Task<CredentialToken> RefreshTokenWithRetryCoreAsync(
        Func<CancellationToken, Task<CredentialToken>> refreshFunc,
        CancellationToken cancellationToken)
    {
        var retryCount = 0;
        Exception? lastException = null;

        // 可观测性：记录刷新开始时间戳
        var startTimestamp = Stopwatch.GetTimestamp();
        var timestampToMs = 1000.0 / Stopwatch.Frequency;
        // P3.4（C4，TK-23）：使用可覆写的 MetricsKey 而非 GetType().Name，使多管理器/多实例维度可区分。
        string? tokenManagerKey = MetricsKey;

        while (retryCount <= MaxRefreshRetryCount)
        {
            try
            {
                var token = await refreshFunc(cancellationToken).ConfigureAwait(false);

                // 成功路径：复位降级退避计数，记录指标（P1.3：服务恢复后尽快回到正常令牌）
                Interlocked.Exchange(ref _consecutiveFallbacks, 0);
                var elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * timestampToMs;
                RecordTokenRefresh(success: true, tokenManagerKey, elapsedMs, isFallback: false);

                return token;
            }
            // NEW-TM-12 修复：取消操作必须立即传播，不进入重试/降级逻辑。
            // 原实现将 OperationCanceledException 视为普通刷新失败，可能错误返回 FallbackToken，
            // 违背 .NET 异步编程规范（取消应立即传播，不应被业务逻辑拦截）。
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                var eventArgs = new TokenRefreshFailedEventArgs(ex, tokenType: null, retryCount);

                OnRefreshFailed(eventArgs);

                if (!string.IsNullOrEmpty(eventArgs.FallbackToken))
                {
                    // 降级路径：记录指标
                    var elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * timestampToMs;
                    RecordTokenRefresh(success: true, tokenManagerKey, elapsedMs, isFallback: true);

                    // P1.3（TK-04）降级令牌有效期 = ExpireThresholdSeconds + 指数退避增量，
                    // 保证降级条目在缓存层必然判定为可用（严格 > 成立），避免刷新风暴；
                    // 同时退避使服务恢复后能快速回到正常令牌。
                    var backoff = NextFallbackLifetimeSeconds();
                    return new CredentialToken
                    {
                        AccessToken = eventArgs.FallbackToken,
                        Expire = DateTimeOffset.UtcNow
                            .AddSeconds(ExpireThresholdSeconds + backoff)
                            .ToUnixTimeMilliseconds()
                    };
                }

                // TK-20（P2.10）重试语义显式化：
                // MaxRefreshRetryCount 是重试的唯一主控门（默认 0 = 不重试），
                // ShouldRetry 仅用于事件处理器提前取消剩余重试，不能启用重试。
                if (retryCount >= MaxRefreshRetryCount || !eventArgs.ShouldRetry)
                {
                    // 失败路径：记录指标
                    var elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * timestampToMs;
                    RecordTokenRefresh(success: false, tokenManagerKey, elapsedMs, isFallback: false);

                    throw;
                }

                retryCount++;
                await Task.Delay(RefreshRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }

        // NEW-TM-05 修复：移除不可达的死代码。
        // while 循环内 catch 块在达到最大重试次数时必然 throw，循环永不自然退出。
        // 此处通过 throw 确保编译器控制流分析通过，且语义清晰。
        throw new InvalidOperationException("令牌刷新失败：达到最大重试次数。");
    }

    /// <summary>
    /// 计算降级令牌的"退避增量"（秒）。P1.3（TK-04）修复：
    /// 从 <c>FallbackGraceSeconds</c>（默认 60s）开始指数翻倍，命中 <see cref="MaxFallbackLifetimeSeconds"/> 后封顶。
    /// 返回值为"增量"，实际有效期 = <see cref="ExpireThresholdSeconds"/> + 增量，保证缓存层严格判定为可用。
    /// </summary>
    private int NextFallbackLifetimeSeconds()
    {
        var n = Math.Min(Interlocked.Increment(ref _consecutiveFallbacks), 5);   // 上限 2^4 倍
        return Math.Min(FallbackGraceSeconds << (n - 1), MaxFallbackLifetimeSeconds);
    }

    /// <summary>
    /// 记录令牌刷新指标（无锁零分配）。
    /// </summary>
    private void RecordTokenRefresh(bool success, string? tokenManagerKey, double elapsedMs, bool isFallback)
    {
        var outcome = success
            ? (isFallback ? "fallback" : "success")
            : "failure";

        var tmKey = tokenManagerKey ?? "(unknown)";

        // R-1：指标 tag 白名单过滤
        var refreshTags = MudHttpMeter.FilterTags(
            new KeyValuePair<string, object?>[] { new("token_manager_key", tmKey), new("outcome", outcome) });
        MudHttpMeter.TokenRefreshCounter.Add(1, refreshTags);

        MudHttpMeter.TokenRefreshDuration.Record(elapsedMs, MudHttpMeter.FilterTags(
            new KeyValuePair<string, object?>[] { new("token_manager_key", tmKey) }));

        // 同步写入无锁统计收集器，供健康检查使用
        TokenRefreshStatsCollector.Record(success, tokenManagerKey, elapsedMs, isFallback);

        // 将令牌管理器键写入当前 Activity tag（仅 Mud Activity）
        var activity = Activity.Current;
        if (activity != null && MudHttpActivitySource.IsMudActivity(activity))
            activity.SetTag(MudHttpActivitySource.Tags.MudTokenManagerKey, tmKey);

        // 写入 DiagnosticSource 事件 + Activity Event（G28：门控前移 + 惰性 tags 工厂）
        if (MudHttpActivitySource.EventsEnabled)
        {
            MudHttpActivitySource.AddActivityEvent(
                MudHttpDiagnosticNames.TokenRefreshed,
                () => new TokenRefreshDiagnosticPayload(tokenManagerKey, success, isFallback, elapsedMs),
                MudHttpDiagnosticNames.TokenRefreshed,
                () => new[]
                {
                    new KeyValuePair<string, object?>("token_manager_key", tmKey),
                    new KeyValuePair<string, object?>("success", success),
                    new KeyValuePair<string, object?>("is_fallback", isFallback),
                    new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                });
        }
    }

    /// <summary>
    /// 尝试获取有效的缓存令牌。TM-02 修复：合并 IsTokenValid + TryGet 为单次查找，
    /// 避免在 GetOrRefreshTokenAsync 中对缓存进行两次独立的 TryGet 调用。
    /// </summary>
    /// <param name="scopeKey">作用域缓存键。</param>
    /// <param name="token">有效令牌（如果返回 true）。</param>
    /// <returns>如果存在有效令牌则返回 true，否则返回 false。</returns>
    private bool TryGetValidToken(string scopeKey, out CredentialToken? token)
    {
        token = default;
        if (!_tokenCache.TryGet(scopeKey, out var entry))
            return false;

        if (entry == null || string.IsNullOrEmpty(entry.AccessToken) || entry.Expire <= 0)
            return false;

        // P1.3（TK-04）收敛：有效期判定统一委托 TokenExpiryPolicy，保证与 CleanupExpiredTokens 严格一致
        // P2.4（TK-04）TTL 感知阈值：短 TTL 令牌的有效提前量被钳位为 min(configuredThreshold, ttl/2)
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (TokenExpiryPolicy.IsValid(entry.IssuedAt, entry.Expire, now, ExpireThresholdSeconds))
        {
            token = entry;
            return true;
        }
        return false;
    }

    private void CleanupExpiredTokens(object? state)
    {
        if (_disposed)
            return;

        // NEW-TM-02 修复：Timer 回调异常会被 .NET 静默吞掉，包裹 try-catch 以保证可观测性
        try
        {
            lock (_cleanupLock)
            {
                if (_disposed)
                    return;

                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                foreach (var key in _tokenCache.Keys.ToList())
                {
                    // T-2 修复：默认作用域（DefaultScopeKey）令牌过期后也应被定时清理，
                    // 下次访问 GetOrRefreshTokenAsync 时会自动重新获取，不再跳过。
                    // 注意：CleanupUnusedLocks 仍保留对 DefaultScopeKey 的跳过，以避免默认作用域锁被回收。

                    // P1.3（TK-04）收敛：过期判定统一委托 TokenExpiryPolicy，与 TryGetValidToken 严格一致
                    // P2.4（TK-04）TTL 感知阈值
                    if (_tokenCache.TryGet(key, out var entry)
                        && entry != null
                        && TokenExpiryPolicy.IsExpired(entry.IssuedAt, entry.Expire, now, ExpireThresholdSeconds))
                    {
                        _tokenCache.TryRemove(key, out _);
                        _keyedLockTable.TryRetire(key);   // P2.2（TK-05/09/24）经 retire 协议统一回收
                    }
                }

                // TMX-04：顺带清理过期的负缓存条目
                foreach (var kv in _recentFailures)
                {
                    if (kv.Value.UntilMs <= now)
                        _recentFailures.TryRemove(kv.Key, out _);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TokenManagerBase: CleanupExpiredTokens 回调异常: {ex.Message}");
        }
    }

    private void CleanupUnusedLocks(object? state)
    {
        if (_disposed)
            return;

        // NEW-TM-02 修复：Timer 回调异常会被 .NET 静默吞掉，包裹 try-catch 以保证可观测性
        try
        {
            lock (_cleanupLock)
            {
                if (_disposed)
                    return;

                foreach (var key in _keyedLockTable.Keys.ToList())
                {
                    if (key == DefaultScopeKey)
                        continue;

                    // P2.2（TK-05/09/24）仅当缓存中已无该作用域令牌时才退休对应的锁。
                    // TryRetire 内部以 retire 协议保证：若锁正被占用则仅标记退休，由最后一个 Releaser 完成移除。
                    if (!_tokenCache.TryGet(key, out _))
                    {
                        _keyedLockTable.TryRetire(key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TokenManagerBase: CleanupUnusedLocks 回调异常: {ex.Message}");
        }
    }

    /// <inheritdoc />
    // P1.5（TK-08）Dispose 补充缓存释放，且不再 Dispose SemaphoreSlim。
    // 1. 补上 _tokenCache.Dispose()（原实现仅 Clear()，未释放缓存底层资源）。
    // 2. P2.2（TK-05/09/24）_keyedLockTable.Dispose() 与 KeyedLockTable 内部"不 Dispose SemaphoreSlim"决策一致：
    //    SemaphoreSlim.Dispose 与在途 WaitAsync/Release 并存会抛 ObjectDisposedException，
    //    破坏"Dispose 后允许在途请求完成、其 Release 不抛异常"的契约。
    // SR-H1（P1.3，D3）Dispose 链重构：可重入 + 每步幂等。
    // 原实现第一行 if (_disposed) return; 在派生类（如 UserTokenManagerBase）先行置位 _disposed 后
    // 调 base.Dispose 时直接早退，导致基类维护 Timer / 锁表 / 缓存永不释放（TK-08 / NEW-TM-11 修复引入的回归）。
    // 新契约（写入基类 XML 文档，见本类 remarks）：
    //   1) Dispose(bool) 可重入、幂等；不以 _disposed 早退（派生类置位后仍须执行基类释放）。
    //   2) 派生类覆写时应在开头自行检查/置位 _disposed（保留 NEW-TM-11 快速感知语义），
    //      且无论标志状态如何都必须调用 base.Dispose(disposing)。
    //   3) 各释放步骤自身幂等。
    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;                        // 幂等置位（volatile 写）

        if (!disposing)
            return;

        StopMaintenanceTimers();                 // 内部幂等（_timersStopped 一次性标志）
        _keyedLockTable.Dispose();               // 现有实现已幂等（全量置 Retired + Clear）
        _tokenCache.Dispose();                   // 两个缓存实现均自带 _disposed 守卫
    }

    private bool _timersStopped;

    /// <summary>
    /// SR-H1（P1.3）停止两个租户维护 Timer。与 Timer 回调共用 _cleanupLock，天然互斥；
    /// _timersStopped 一次性标志保证可重入幂等。
    /// </summary>
    private void StopMaintenanceTimers()
    {
        lock (_cleanupLock)
        {
            if (_timersStopped)
                return;
            _timersStopped = true;
            _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _lockCleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _cleanupTimer?.Dispose();
            _lockCleanupTimer?.Dispose();
        }
    }

    /// <summary>
    /// SR-H1（P1.3）测试观测钩子：维护 Timer 是否仍在运行（经 InternalsVisibleTo 供测试断言 Dispose 后 Timer 停止）。
    /// </summary>
    internal bool TimersActive => !_timersStopped;

    /// <summary>
    /// MT-15：是否已释放。供 <c>TokenRefreshHelper</c>（Client 程序集）区分
    /// 「管理器已被释放（应反注册）」与「管理器<b>暂时</b>不可用（不应永久反注册）」。
    /// </summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// SR-M5（P3.3）测试观测钩子：作用域缓存当前条目数（供断言硬上限 LRU 收敛）。
    /// </summary>
    internal int CacheCountInternal => _tokenCache.Count;

    /// <inheritdoc />
    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
