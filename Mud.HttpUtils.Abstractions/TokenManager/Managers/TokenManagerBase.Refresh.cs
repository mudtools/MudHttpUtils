// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils;

// -------------------------------------------------------------------------------------------------
//  刷新主路径：GetTokenAsync / GetOrRefreshTokenAsync、作用域键控锁、跨进程去重租约、失败负缓存、重试与降级退避
//  TokenManagerBase partial: shares all private state with the main file.
// -------------------------------------------------------------------------------------------------
public abstract partial class TokenManagerBase
{
    /// <inheritdoc />
    public abstract Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    /// <remarks>
    /// 默认实现走 scope 感知路径（与 <see cref="GetOrRefreshTokenAsync(string[], CancellationToken)"/> 一致），
    /// 不再静默返回默认作用域令牌。不支持 scope 的派生类应覆写并抛 <see cref="NotSupportedException"/>
    /// （否则调用方会静默拿到默认作用域令牌，构成 scope 错配 / 潜在越权风险；
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
    /// <para><b>取消传播语义</b>：直接调用路径下，首个调用方的 <paramref name="cancellationToken"/>
    /// 会传入锁内刷新操作。若该调用方取消，则正在进行的共享刷新将被中止，其余等待者将在各自重试时发起新的刷新。
    /// 此行为是"尊重调用方取消"的有意设计，并非缺陷。</para>
    /// <para>401 恢复路径已做取消隔离（<c>TokenRecoveryExecutor</c> 使用独立 CT），不受此语义影响。</para>
    /// <para><b>实现约定（子类必读）</b>：</para>
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

        // S2-2（治理方案）：异步缓存能力探测 —— 命中 IAsyncTokenCache<T>（如 TokenStoreBackedTokenCache<T> 桥接器）
        // 时经 GetAsync 读穿透（镜像未命中时直达 store），消除"冷启动必须先水合"的限制与多实例镜像滞后。
        // 非异步缓存：探测同步返回 null（ValueTask 同步路径），行为与此前完全一致。
        var probedToken = await TryGetValidTokenThroughAsync(scopeKey, cancellationToken).ConfigureAwait(false);
        if (probedToken != null)
        {
            return probedToken.AccessToken!;
        }

        // TM-02 修复：使用 TryGetValidToken 合并 IsTokenValid + TryGet 为单次查找，避免冗余的双字典访问
        if (TryGetValidToken(scopeKey, out var fastToken))
        {
            return fastToken!.AccessToken!;
        }

        // 从键控锁表获取作用域锁
        using (var releaser = await _keyedLockTable.AcquireAsync(scopeKey, cancellationToken).ConfigureAwait(false))
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            // S2-2：锁内二次探测 —— 等待锁期间其他实例/其他作用域写入 store 的新令牌经读穿透直接命中
            probedToken = await TryGetValidTokenThroughAsync(scopeKey, cancellationToken).ConfigureAwait(false);
            if (probedToken != null)
            {
                return probedToken.AccessToken!;
            }

            if (TryGetValidToken(scopeKey, out var lockedToken))
            {
                return lockedToken!.AccessToken!;
            }

            var nowMs = UtcNow.ToUnixTimeMilliseconds();
            var negativeCacheSeconds = NegativeCacheSeconds;

            // 窗口内复用上次失败，阻断"等待者串行各刷一次"
            if (negativeCacheSeconds > 0
                && _recentFailures.TryGetValue(scopeKey, out var failed)
                && failed.UntilMs > nowMs)
            {
                // 抑制同样计入标准刷新指标（outcome=failure），
                // 使"被负缓存吞掉的失败"在通用监控视图（失败率/刷新次数）中可见 ——
                // 此前仅增专用抑制计数器，聚合面板看不到这部分失败。
                RecordTokenRefresh(success: false, MetricsKey, elapsedMs: 0, isFallback: false);

                MudHttpMeter.TokenRefreshSuppressedCounter.Add(1,
                    MudHttpMeter.FilterTags(new KeyValuePair<string, object?>[] { new("token_manager_key", MetricsKey) }));

                // **不复用同一异常实例**。原实现用 ExceptionDispatchInfo 重抛缓存中的同一对象，
                // 并发等待者会共享一个 Exception：其 StackTrace / Data / 内部可变状态被多方同时读写，
                // 且栈信息指向"首次抛出的位置"，无法区分"真正失败点"与"被负缓存抑制的复用点"。
                // 改为每次构造新异常、把原异常作为 InnerException 保留完整因果链。
                throw new InvalidOperationException(
                    $"令牌刷新在负缓存窗口内被抑制（ScopeKey={scopeKey}）：{failed.Exception.Message}",
                    failed.Exception);
            }

            // 跨进程刷新去重租约（可选；RefreshLease 默认 null = 零行为变化）。
            // 进程内键控锁已保证单实例内去重；租约补多实例维度 —— 未抢到的实例限时轮询读穿透，
            // 超时 fail-open 放行本机刷新（租约持有方崩溃时不得死锁；fail-open 仅退化为重复刷新一次）。
            ITokenRefreshLeaseHandle? refreshLeaseHandle = null;
            var leaseAcquired = false;
            try
            {
                var lease = RefreshLease;
                if (lease != null)
                {
                    refreshLeaseHandle = await lease.TryAcquireAsync(
                        BuildRefreshLeaseKey(scopeKey), RefreshLeaseTime, cancellationToken).ConfigureAwait(false);
                    leaseAcquired = refreshLeaseHandle != null;

                    if (!leaseAcquired)
                    {
                        var waitDeadlineMs = nowMs + RefreshLeaseWaitTimeoutMilliseconds;
                        var pollIntervalMs = RefreshLeasePollIntervalMilliseconds;
                        while (UtcNow.ToUnixTimeMilliseconds() < waitDeadlineMs)
                        {
                            await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);

                            // 等待期间持有方大概率已完成刷新并写入：读穿透直接命中则免本机刷新
                            var waitedToken = await TryGetValidTokenThroughAsync(scopeKey, cancellationToken).ConfigureAwait(false);
                            if (waitedToken != null)
                            {
                                return waitedToken.AccessToken!;
                            }
                            if (TryGetValidToken(scopeKey, out var waitedLocalToken))
                            {
                                return waitedLocalToken!.AccessToken!;
                            }
                        }
                        // fail-open：等待超时（持有方崩溃 / 租约 TTL 过短），放行本机刷新。
                    }
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
            finally
            {
                // U-3：租约释放按所有权校验（compare-and-del），失败吞异常（不得掩盖刷新结果 / 阻断主路径）
                if (leaseAcquired && refreshLeaseHandle != null)
                {
                    try
                    {
                        await refreshLeaseHandle.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 释放失败仅意味着租约等 TTL 自动过期，语义安全（compare-and-del 契约兜底）。
                    }
                }
            }
        }
    }

    /// <summary>
    /// 刷新令牌的核心实现，由子类实现具体的刷新逻辑。
    /// </summary>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>刷新后的凭证令牌。</returns>
    /// <remarks>
    /// <para><b>实现约定（子类必读）</b>：</para>
    /// <list type="bullet">
    /// <item><description>本方法在<b>持有作用域键控锁（<see cref="KeyedLockTable"/>）期间</b>被调用
    /// （<c>RefreshTokenWithScopesAsync</c> 的默认实现亦然）。</description></item>
    /// <item><description>该锁基于 <see cref="SemaphoreSlim"/>，<b>不可重入</b>：
    /// 本实现内部<b>不得</b>回调本管理器的 <see cref="GetOrRefreshTokenAsync(string[], CancellationToken)"/> /
    /// <see cref="GetOrRefreshTokenAsync(CancellationToken)"/> / <see cref="GetTokenAsync(string[], CancellationToken)"/> /
    /// <see cref="InvalidateTokenAsync(string[], CancellationToken)"/>，否则将<b>自锁死</b>。</description></item>
    /// <item><description>需要复用已缓存令牌时，请使用<b>不加锁</b>的读取入口：
    /// <see cref="GetCachedCredentialToken()"/> / <see cref="GetCachedCredentialToken(string)"/>。</description></item>
    /// <item><description>本方法抛出的异常会被基类记入负缓存窗口（<c>NegativeCacheSeconds</c>）并向调用方传播；
    /// <see cref="OperationCanceledException"/> 除外（取消不计为刷新失败）。</description></item>
    /// </list>
    /// </remarks>
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

    private async Task<CredentialToken> RefreshTokenWithRetryCoreAsync(
        Func<CancellationToken, Task<CredentialToken>> refreshFunc,
        CancellationToken cancellationToken)
    {
        var retryCount = 0;
        Exception? lastException = null;

        // 可观测性：记录刷新开始时间戳
        var startTimestamp = Stopwatch.GetTimestamp();
        var timestampToMs = 1000.0 / Stopwatch.Frequency;
        // 使用可覆写的 MetricsKey 而非 GetType().Name，使多管理器/多实例维度可区分。
        string? tokenManagerKey = MetricsKey;

        while (retryCount <= MaxRefreshRetryCount)
        {
            try
            {
                var token = await refreshFunc(cancellationToken).ConfigureAwait(false);

                // 成功路径：复位降级退避计数，记录指标（服务恢复后尽快回到正常令牌）
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

                    // 降级令牌有效期 = ExpireThresholdSeconds + 指数退避增量，
                    // 保证降级条目在缓存层必然判定为可用（严格 > 成立），避免刷新风暴；
                    // 同时退避使服务恢复后能快速回到正常令牌。
                    var backoff = NextFallbackLifetimeSeconds();
                    return new CredentialToken
                    {
                        AccessToken = eventArgs.FallbackToken,
                        Expire = UtcNow
                            .AddSeconds(ExpireThresholdSeconds + backoff)
                            .ToUnixTimeMilliseconds()
                    };
                }

                // 重试语义显式化：
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
    /// 计算降级令牌的"退避增量"（秒）。修复：
    /// 从 <c>FallbackGraceSeconds</c>（默认 60s）开始指数翻倍，命中 <see cref="MaxFallbackLifetimeSeconds"/> 后封顶。
    /// 返回值为"增量"，实际有效期 = <see cref="ExpireThresholdSeconds"/> + 增量，保证缓存层严格判定为可用。
    /// </summary>
    private int NextFallbackLifetimeSeconds()
    {
        var n = Math.Min(Interlocked.Increment(ref _consecutiveFallbacks), 5);   // 上限 2^4 倍
        return Math.Min(FallbackGraceSeconds << (n - 1), MaxFallbackLifetimeSeconds);
    }
}
