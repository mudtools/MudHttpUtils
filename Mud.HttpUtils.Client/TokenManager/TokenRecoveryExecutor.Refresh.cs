// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// F-8：纯移动拆分（自 TokenRecoveryExecutor.cs 按职责拆出：令牌刷新策略）。

namespace Mud.HttpUtils;

public partial class TokenRecoveryExecutor
{
    /// <summary>
    /// TMR-04/TMR-12：执行令牌刷新，使用 ConcurrentDictionary 去重，确保同一时间窗口内多个 401 只触发一次刷新。
    /// 去重键含 scope（I1/R-P0-04：长度前缀单射编码，见 <see cref="BuildTenantScopedDedupKey(string, string)"/>），
    /// 避免同管理器不同 scope 的并发 401 被合并。
    /// FIX-10：去重键增加 AppKey 维度（前缀），避免多租户同 managerKey 的 401 恢复被错误合并。
    /// TMR-12：刷新完成后结果在 RefreshDedupWindowSeconds 窗口内保留，窗口内后续 401 直接复用结果。
    /// </summary>
    private Task<string?> RefreshTokenWithDedupAsync(
        string managerKey, ITokenManager credentialManager, string[]? scopes, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        var scopeKey = scopes is { Length: > 0 } ? ScopeKeyBuilder.Build(scopes) : ScopeKeyBuilder.DefaultKey;
        var dedupKey = BuildTenantScopedDedupKey(managerKey, scopeKey);

        // MT-05/MT-06：去重与有界收敛统一委托 RefreshDedupTable。
        // 刷新工厂本身即为共享任务（不再经 TaskCompletionSource 转发），
        // 因此失败时不存在"无人 await 的任务"，根除未观察任务异常。
        // M6-HC-23：forceRefresh=true（恢复循环第 2..N 轮）跳过窗口内的已完成结果，避免复用已被拒绝的令牌。
        // I3（R-P0-02）：等待可取消 + 硬预算；工厂入参为共享刷新的 CT（仅受硬预算约束，取消隔离语义保持）。
        return GetOrRefreshWithBudgetAsync(
            _credentialRefreshTasks,
            dedupKey,
            ct => RefreshCredentialWithIsolationAsync(credentialManager, scopes, ct),
            cancellationToken,
            forceRefresh);
    }

    /// <summary>
    /// I3（R-P0-02）：统一的"提交去重刷新"入口 —— 计算硬预算、注入调用方 CT，并把
    /// <see cref="TimeoutException"/> 转为可观测日志后上抛（由恢复循环按刷新失败处理）。
    /// </summary>
    private async Task<string?> GetOrRefreshWithBudgetAsync(
        RefreshDedupTable table,
        string dedupKey,
        Func<CancellationToken, Task<string?>> factory,
        CancellationToken cancellationToken,
        bool forceRefresh)
    {
        try
        {
            return await table.GetOrRefreshAsync(
                dedupKey,
                factory,
                Options.RefreshDedupWindowSeconds,
                ResolveRefreshWaitHardTimeout(),
                cancellationToken,
                forceRefresh).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            // 去重键不含明文（仅打印预算与短哈希），避免 userId / managerKey 入日志。
            MudHttpClientLog.TokenRefreshWaitTimeout(
                _logger, HashForLog(dedupKey), ResolveRefreshWaitHardTimeout().TotalSeconds);
            throw new TimeoutException(ex.Message, ex);
        }
    }

    /// <summary>
    /// I3（R-P0-02）：解析等待硬预算。显式配置 &gt; 0 时采用之，否则取
    /// <see cref="TokenRecoveryOptions.RefreshTimeoutSeconds"/> + 5s 余量（保证协作式超时先于硬墙钟生效）。
    /// </summary>
    private TimeSpan ResolveRefreshWaitHardTimeout()
    {
        var configured = Options.RefreshWaitHardTimeoutSeconds;
        var seconds = configured > 0 ? configured : Options.RefreshTimeoutSeconds + 5;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>日志用短哈希（FNV-1a 32 位），避免去重键内的 userId / managerKey 明文入日志。</summary>
    private static string HashForLog(string key)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in key)
                hash = (hash ^ c) * 16777619;
            return hash.ToString("X8");
        }
    }

    /// <summary>
    /// SR-M6（P2.4，D9）按 TokenRecoveryContext.TokenManagerKey 解析本次恢复应使用的租户令牌管理器。
    /// 注册表缺席 / 键为空 / 解析失败 → 回退构造注入实例（解析失败记 Warning，不 fail-fast——
    /// 生成器默认键场景的 401 恢复可用性优先）。解析在恢复循环外完成一次，失效+刷新+重试全链路复用。
    /// </summary>
    private ITokenManager? ResolveManager(TokenRecoveryContext? ctx)
    {
        var key = ctx?.TokenManagerKey;
        if (string.IsNullOrEmpty(key) || _managerRegistry == null)
        {
            // 既有行为（单管理器绑定）；L-1：注入实例同样受守卫约束
            return TryEnforceTenantBinding(_tokenManager) ? _tokenManager : null;
        }

        var resolved = _managerRegistry.Resolve(key!);
        if (resolved == null)
        {
            MudHttpClientLog.TokenManagerUnresolved(_logger, key!);   // Warning：回退注入实例
            return TryEnforceTenantBinding(_tokenManager) ? _tokenManager : null;
        }

        // L-1：命中：先做租户绑定守卫，再交给失效+刷新+重试全链路。
        // 注册表是宿主级扁平命名空间，两个应用注册同名管理器时 Resolve 可能返回**另一个租户**的实例；
        // 守卫在此 fail-closed（返回 null → 恢复流程按失败处理 → 返回真实 401）。
        return TryEnforceTenantBinding(resolved) ? resolved : null;
    }

    /// <summary>
    /// L-1：恢复链路的租户绑定守卫。
    /// </summary>
    /// <param name="manager">本次恢复将要使用的令牌管理器。</param>
    /// <returns><c>true</c> = 允许使用该管理器；<c>false</c> = 被守卫拒绝（已记结构化告警，调用方应返回真实 401）。</returns>
    /// <remarks>
    /// <para>
    /// 与 <c>DefaultTokenProvider</c> 中的守卫同语义（bind-once）：管理器实例一旦绑定到某个 appKey，
    /// 后续以其它 appKey 使用即被拒绝。
    /// </para>
    /// <para>
    /// 此前守卫**仅在令牌获取路径**执行，恢复路径可绕过 —— 当宿主使用<b>扁平</b>
    /// <see cref="ITokenManagerRegistry"/> 且两个应用注册了同名 key 时，
    /// 应用 A 的 401 可能触发应用 B 的令牌被失效/刷新（凭据错配 / 跨租户越权）。
    /// </para>
    /// <para>
    /// 跳过条件（与既有行为一致，不引入新的误报）：
    /// ① <paramref name="manager"/> 非 <see cref="TokenManagerBase"/> 派生类；
    /// ② 未注入 <see cref="IAppContextHolder"/>（无 DI / 第三方宿主自建执行器时）；
    /// ③ 当前无应用上下文（<c>Current?.AppKey</c> 为空）且 <c>MudMultiTenantOptions.MissingContextPolicy = FallbackToDefaultApp</c>；
    /// ④ 管理器覆写 <c>EnforceTenantBinding = false</c>（合法共享凭据设计）。
    /// </para>
    /// <para>
    /// FIX-09（EL-9）：已注入 <see cref="IAppContextHolder"/> 但当前无应用上下文时，改读 <see cref="MudMultiTenantOptions.MissingContextPolicy"/> 决策：
    /// • <see cref="MissingAppContextPolicy.Reject"/>（默认）：fail-closed，返回 <c>false</c>，恢复流程返回真实 401；
    /// • <see cref="MissingAppContextPolicy.FallbackToDefaultApp"/>：维持旧行为，跳过守卫（<c>return true</c>）。
    /// 注意：未注入 <see cref="IAppContextHolder"/>（无 DI / 第三方宿主自建执行器）时不读策略，始终跳过守卫——
    /// 该场景不涉及多租户，守卫无意义。
    /// </para>
    /// <para>
    /// 拒绝时**不向调用方抛异常**：与其余恢复失败分支保持一致（D3 —— 返回服务端真实 401），
    /// 仅记录结构化告警（<c>TenantBindingRejected</c>，EventId 162）。
    /// </para>
    /// </remarks>
    private bool TryEnforceTenantBinding(ITokenManager manager)
    {
        if (manager is not TokenManagerBase baseManager)
            return true;

        var appKey = _appContextHolder?.Current?.AppKey;
        if (string.IsNullOrEmpty(appKey))
        {
            // FIX-09（EL-9）：已注入 IAppContextHolder 但当前无应用上下文时，改读 MudMultiTenantOptions 决策。
            // 未注入 IAppContextHolder 时（_appContextHolder == null）：跳过守卫——该场景不涉及多租户。
            if (_appContextHolder == null)
                return true;

            // 已注入但上下文缺失：读策略。
            // Reject（默认）：fail-closed — 拒绝恢复，返回真实 401。
            // FallbackToDefaultApp：维持旧行为 — 跳过守卫，允许恢复。
            var policy = _multiTenantOptions?.MissingContextPolicy ?? MissingAppContextPolicy.Reject;
            if (policy == MissingAppContextPolicy.Reject)
            {
                MudHttpClientLog.MissingAppContextRejected(_logger, manager.GetType().Name);
                return false;
            }
            return true;
        }

        try
        {
            // BindTenantGuard 内部已处理 EnforceTenantBinding=false 的逃生门与同键幂等。
            baseManager.BindTenantGuard(appKey!);
            return true;
        }
        catch (InvalidOperationException)
        {
            MudHttpClientLog.TenantBindingRejected(
                _logger,
                manager.GetType().Name,
                baseManager.BoundTenant ?? "(未绑定)",
                appKey!);
            return false;
        }
    }

    /// <summary>
    /// SR-M6（P2.4，D9）用户级恢复的管理器解析：与 <see cref="ResolveManager"/> 同语义，但要求
    /// 解析结果实现 <see cref="IUserTokenManager"/>——解析失败或解析到非用户管理器（租户实例）时
    /// 一律回退构造注入实例并记 Warning（TK-06：绝不用租户管理器执行用户级恢复，凭据错配防线）。
    /// </summary>
    private IUserTokenManager? ResolveUserManager(TokenRecoveryContext? ctx)
    {
        var injected = _userTokenManager!;               // 调用点已保证非空（isUserTokenRecovery 分支）
        var key = ctx?.TokenManagerKey;
        if (string.IsNullOrEmpty(key) || _managerRegistry == null)
        {
            // 既有行为（单管理器绑定）；L-1：注入实例同样受守卫约束
            return TryEnforceTenantBinding(injected) ? injected : null;
        }

        if (_managerRegistry.Resolve(key!) is IUserTokenManager userManager)
        {
            // L-1：解析命中同样受守卫约束
            return TryEnforceTenantBinding(userManager) ? userManager : null;
        }

        MudHttpClientLog.TokenManagerUnresolved(_logger, key!);   // Warning：回退注入实例
        return TryEnforceTenantBinding(injected) ? injected : null;
    }

    /// <summary>
    /// 以取消隔离方式执行租户令牌刷新：刷新操作自身不受调用方 CT 影响，仅受"刷新超时"约束。
    /// TMR-04：按 recoveryContext.Scopes 失效和刷新（不再恒走默认作用域）。
    /// </summary>
    /// <param name="tokenManager">SR-M6（D9）经注册表解析的管理器（解析失败时为构造注入实例）。</param>
    /// <param name="scopes">恢复上下文中的作用域集合，为空时走默认作用域。</param>
    /// <param name="cancellationToken">
    /// I3（R-P0-02）：去重表注入的<b>共享刷新</b>取消令牌 —— 仅受硬预算约束，不等价于任一调用方的 CT
    /// （单方取消不中止共享刷新）；与协作式超时 <see cref="TokenRecoveryOptions.RefreshTimeoutSeconds"/>
    /// 合并后使超时能真正中断底层刷新（消除原实现的"死参数"）。
    /// </param>
    private async Task<string?> RefreshCredentialWithIsolationAsync(
        ITokenManager tokenManager, string[]? scopes, CancellationToken cancellationToken = default)
    {
        // I2（R-P0-01）：覆盖"失效 + 刷新"全程 —— 刷新期间发出的 HTTP 请求由执行器入口短路，
        // 防止令牌端点 401 重新进入恢复并 await 自身（永久死锁，§3.1）。
        using var refreshScope = TokenRefreshAmbient.Enter();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Options.RefreshTimeoutSeconds));
        var refreshCt = timeoutCts.Token;

        try
        {
            // TR-02：优先走"仅清访问令牌"（internal 入口），保留 refresh_token 以支持自愈——
            // 整条 InvalidateTokenAsync 会把 refresh_token 一并销毁，迫使恢复降级为 client_credentials
            // 或重新授权。非 TokenManagerBase 派生实现退化为既有整条失效（记 Warning，行为与历史一致）。
            if (tokenManager is TokenManagerBase baseManager)
            {
                baseManager.InvalidateCachedAccessToken(scopes);
            }
            else
            {
                MudHttpClientLog.AccessTokenInvalidationFallback(_logger, tokenManager.GetType().Name);
                await tokenManager.InvalidateTokenAsync(scopes, refreshCt).ConfigureAwait(false);
            }
        }
        catch (Exception invalidateEx)
        {
            MudHttpClientLog.TokenInvalidationFailed(_logger, invalidateEx);
        }

        return scopes is { Length: > 0 }
            ? await tokenManager.GetOrRefreshTokenAsync(scopes, refreshCt).ConfigureAwait(false)
            : await tokenManager.GetOrRefreshTokenAsync(refreshCt).ConfigureAwait(false);
    }

    /// <summary>
    /// TMR-05/TMR-12：执行用户令牌刷新，使用 ConcurrentDictionary 按 userId + scope 去重。
    /// 去重键含 scope：managerKey + "\u001F" + userId + "\u001F" + scopeKey。
    /// FIX-10：去重键增加 AppKey 维度（前缀），避免多租户同 managerKey 的用户令牌 401 恢复被错误合并。
    /// TMR-12：刷新完成后结果在 RefreshDedupWindowSeconds 窗口内保留，窗口内后续 401 直接复用结果。
    /// </summary>
    private Task<string?> RefreshUserTokenWithDedupAsync(
        string managerKey, string userId, IUserTokenManager userTokenManager, string[]? scopes, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        var scopeKey = scopes is { Length: > 0 } ? ScopeKeyBuilder.Build(scopes) : ScopeKeyBuilder.DefaultKey;
        var dedupKey = BuildTenantScopedDedupKey(managerKey, userId, scopeKey);

        // MT-05/MT-06：同租户级路径，统一委托 RefreshDedupTable（含 userId 的高基数键受条目上限约束）。
        // M6-HC-23：forceRefresh 语义与租户级路径一致。
        // I3（R-P0-02）：等待可取消 + 硬预算，与租户级路径同一入口。
        return GetOrRefreshWithBudgetAsync(
            _userRefreshTasks,
            dedupKey,
            ct => RefreshUserTokenWithIsolationAsync(userId, userTokenManager, scopes, ct),
            cancellationToken,
            forceRefresh);
    }

    /// <summary>
    /// FIX-10：构建含 AppKey 维度的去重键。
    /// 在多租户场景中，两个不同 App 使用同一个 managerKey（如默认键场景）时，
    /// 若去重键不含 AppKey，它们的 401 恢复会被合并为一次刷新——但不同租户的令牌不同，
    /// 合并刷新会导致一个租户拿到另一个租户的令牌（跨租户越权）。
    /// </summary>
    /// <remarks>
    /// <b>I1（R-P0-04）</b>：改为 <see cref="CompositeKey"/> 长度前缀编码 —— 原实现
    /// <c>appKey + "\u001F" + managerKey + ["\u001F" + userId] + "\u001F" + scopeKey</c> 中
    /// <c>managerKey</c> / <c>userId</c> 均为<b>中间层可灌入的不可信输入</b>（见本类 <c>:287</c> 注释），
    /// 内嵌 U+001F 即可与其它分段组合制造碰撞 → 复用他人刷新结果（B11）。
    /// AppKey 为空时使用固定占位符 <c>"_no_tenant"</c>（单租户/无 DI 场景的旧行为等价）。
    /// </remarks>
    private string BuildTenantScopedDedupKey(string managerKey, string scopeKey)
    {
        var appKey = _appContextHolder?.Current?.AppKey;
        return CompositeKey.Combine(string.IsNullOrEmpty(appKey) ? "_no_tenant" : appKey, managerKey, scopeKey);
    }

    /// <summary>
    /// FIX-10：构建含 AppKey 维度的用户令牌去重键（含 userId）。
    /// I1（R-P0-04）：长度前缀编码，杜绝 managerKey / userId 内嵌分隔符造成的跨管理器/跨用户错配。
    /// </summary>
    private string BuildTenantScopedDedupKey(string managerKey, string userId, string scopeKey)
    {
        var appKey = _appContextHolder?.Current?.AppKey;
        return CompositeKey.Combine(
            string.IsNullOrEmpty(appKey) ? "_no_tenant" : appKey, managerKey, userId, scopeKey);
    }

    /// <summary>
    /// 以取消隔离方式执行用户令牌刷新：刷新操作自身不受调用方 CT 影响，仅受"刷新超时"约束。
    /// TMR-05：按 scopes 精准失效（不淆空全部作用域），非基类实现降级为 RemoveTokenAsync + Warning。
    /// </summary>
    /// <param name="userId">用户标识。</param>
    /// <param name="userTokenManager">SR-M6（D9）经注册表解析的用户管理器（解析失败时为构造注入实例）。</param>
    /// <param name="scopes">恢复上下文中的作用域集合。</param>
    /// <param name="cancellationToken">I3（R-P0-02）：去重表注入的共享刷新取消令牌（语义同租户级路径）。</param>
    private async Task<string?> RefreshUserTokenWithIsolationAsync(
        string userId, IUserTokenManager userTokenManager, string[]? scopes, CancellationToken cancellationToken = default)
    {
        // I2（R-P0-01）：覆盖"失效 + 刷新"全程（用户级路径与租户级路径对称）。
        using var refreshScope = TokenRefreshAmbient.Enter();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Options.RefreshTimeoutSeconds));
        var refreshCt = timeoutCts.Token;

        try
        {
            // TMR-05：精准失效——基类实现走 scoped 虚方法，非基类降级为整用户清除 + Warning
            // TR-02（用户级）：优先走"仅清访问令牌字段"（internal 入口，保留 RefreshToken 支持自愈，
            // 且不触发派生类对 InvalidateUserTokenAsync 的覆写副作用——401 恢复不应撤销 IdP 侧凭据）。
            if (userTokenManager is UserTokenManagerBase baseManager)
            {
                baseManager.InvalidateCachedUserAccessToken(userId, scopes);
            }
            else
            {
                MudHttpClientLog.UserTokenScopeInvalidationFallback(_logger, userId);
                await userTokenManager.RemoveTokenAsync(userId, refreshCt).ConfigureAwait(false);
            }
        }
        catch (Exception removeEx)
        {
            MudHttpClientLog.UserTokenRemovalFailed(_logger, userId, removeEx);
        }

        return scopes is { Length: > 0 }
            ? await userTokenManager.GetOrRefreshTokenAsync(userId, scopes, refreshCt).ConfigureAwait(false)
            : await userTokenManager.GetOrRefreshTokenAsync(userId, refreshCt).ConfigureAwait(false);
    }
}
