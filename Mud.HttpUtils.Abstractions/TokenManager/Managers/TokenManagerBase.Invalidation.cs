// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

// -------------------------------------------------------------------------------------------------
//  失效与级联：InvalidateTokenAsync、依赖图传播（AsyncLocal 重入抑制）、TokenScopeKey 定位符重载与一致性校验
//  TokenManagerBase partial: shares all private state with the main file.
// -------------------------------------------------------------------------------------------------
public abstract partial class TokenManagerBase
{
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

            // S2-2：异步缓存经 RemoveAsync 穿透删除（桥接器实现下失效同步落到持久层，防 C6）
            CredentialToken? removed;
            if (_tokenCache is IAsyncTokenCache<CredentialToken> asyncCache)
            {
                removed = await asyncCache.RemoveAsync(scopeKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _tokenCache.TryRemove(scopeKey, out removed);
            }

            var result = removed == null || string.IsNullOrEmpty(removed.AccessToken)
                ? TokenResult.Empty
                : new TokenResult(removed.AccessToken!, removed.Expire, scopeKey);

            // 失效通知 + 级联传播（独立开关，默认关；事件恒触发，不受开关影响）。
            NotifyAndCascadeInvalidated(scopes, result, cancellationToken);

            return result;
        }
    }

    /// <summary>
    /// <see cref="InvalidateTokenAsync(string[], CancellationToken)"/> 的收尾钩子 ——
    /// ① 触发 <see cref="TokenInvalidated"/>（订阅者异常隔离，不阻断返回）；
    /// ② 若 <see cref="EnableTokenDependencyCascade"/> 且 <see cref="DependencyGraph"/> 非空，
    /// 把失效沿依赖图传播到依赖令牌的管理器（<see cref="ResolveTokenManagerForType(string)"/>）。
    /// 级联期间经 <c>AsyncLocal</c> 重入抑制防互相依赖图（A↔B）无限递归；
    /// 级联为 fire-and-forget（不阻塞调用方、级联失败不抛出）。
    /// </summary>
    private void NotifyAndCascadeInvalidated(string[]? scopes, TokenResult removed, CancellationToken cancellationToken)
    {
        var handlers = TokenInvalidated;
        if (handlers != null)
        {
            var args = new TokenInvalidatedEventArgs(TokenType, scopes, removed);
            foreach (EventHandler<TokenInvalidatedEventArgs> subscriber in handlers.GetInvocationList())
            {
                try
                {
                    subscriber(this, args);
                }
                catch
                {
                    // 事件订阅者故障不得阻断失效主流程（与 DefaultAppManager 事件隔离口径一致）。
                }
            }
        }

        if (!EnableTokenDependencyCascade || s_cascadeInProgress.Value)
            return;

        var graph = DependencyGraph;
        if (graph.IsEmpty || string.IsNullOrEmpty(TokenType))
            return;

        var dependents = graph.GetDependentsOf(TokenType!);
        if (dependents.Count == 0)
            return;

        s_cascadeInProgress.Value = true;
        try
        {
            foreach (var dependent in dependents)
            {
                var manager = ResolveTokenManagerForType(dependent);
                if (manager == null)
                    continue;   // 未接线/未注册：fail-safe 跳过，不抛。

                // 依赖令牌沿同一 scopes 归属失效（企业级链式场景下 scope 即 authCorpId）。
                _ = manager.InvalidateTokenAsync(scopes, cancellationToken);
            }
        }
        finally
        {
            s_cascadeInProgress.Value = false;
        }
    }

    // 以下两个 TokenScopeKey 重载刻意携带与既有重载同形的可选 CancellationToken（保持调用形态一致，
    // 且不得改动既有 string[] scopes 签名）。RS0026 建议"去掉可选参数"反而会造成新旧重载调用形态分裂，
    // 与 Mud.HttpUtils.Client 对同一权衡的登记口径一致，此处局部豁免。
#pragma warning disable RS0026 // 请勿添加具有可选参数的多个重载：定位符重载刻意保留可选 CancellationToken
    /// <summary>
    /// 按<b>定位符形态</b>的 <see cref="TokenScopeKey"/> 获取有效令牌（未命中则刷新）。
    /// 与既有 <see cref="GetOrRefreshTokenAsync(string[], CancellationToken)"/> <b>单缓存互通</b>：
    /// 按段映射到旧路径（<c>ForScope(t,a,s)</c> → <c>scopes = [s]</c>；<c>ForApp(t,a)</c> /
    /// <c>ForShared(t)</c> → <c>scopes = null</c>），同一归属键命中同一缓存条目。
    /// </summary>
    /// <param name="scopeKey">
    /// 归属键（tokenType + appKey + scopeKey 三段式）。其中 <c>scopeKey</c> 段是<b>归属键</b>
    /// （如 <c>authCorpId</c>）而非 OAuth scope；<c>tokenType</c> / <c>appKey</c> 段仅做一致性校验，
    /// <b>绝不参与缓存键</b>。
    /// </param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>包含有效访问令牌的字符串。</returns>
    /// <exception cref="ArgumentException">tokenType / appKey 段与本管理器声明的令牌类型 / 上下文 AppKey 不一致。</exception>
    public virtual Task<string> GetOrRefreshTokenAsync(TokenScopeKey scopeKey, CancellationToken cancellationToken = default)
    {
        ValidateScopeKeyLocator(scopeKey);
        return GetOrRefreshTokenAsync(scopeKey.ToScopes(), cancellationToken);
    }

    /// <summary>
    /// 按<b>定位符形态</b>的 <see cref="TokenScopeKey"/> 使缓存令牌失效。
    /// 与既有 <see cref="InvalidateTokenAsync(string[], CancellationToken)"/> <b>单缓存互通</b>（段映射规则同上）。
    /// </summary>
    /// <param name="scopeKey">归属键（tokenType / appKey 段仅做一致性校验，绝不参与缓存键）。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>失效前的令牌信息，如果缓存中没有令牌则返回 <see cref="TokenResult.Empty"/>。</returns>
    /// <exception cref="ArgumentException">tokenType / appKey 段与本管理器声明的令牌类型 / 上下文 AppKey 不一致。</exception>
    public virtual Task<TokenResult> InvalidateTokenAsync(TokenScopeKey scopeKey, CancellationToken cancellationToken = default)
    {
        ValidateScopeKeyLocator(scopeKey);
        return InvalidateTokenAsync(scopeKey.ToScopes(), cancellationToken);
    }
#pragma warning restore RS0026

    /// <summary>
    /// 定位符形态的一致性校验——<c>tokenType</c> 段对照本管理器声明的
    /// <see cref="TokenType"/>（未声明则跳过）；<c>appKey</c> 段对照上下文 AppKey（即
    /// <see cref="BindTenantGuard(string, string?)"/> 绑定的租户键；尚未绑定 / 该段为空则跳过）。
    /// 两段<b>只做校验</b>：不绑定、不参与缓存键（缓存键仍由 scopeKey 段经既有路径生成）。
    /// </summary>
    /// <param name="scopeKey">待校验的归属键定位符。</param>
    /// <exception cref="ArgumentException">任一段与当前已知取值不一致。</exception>
    private void ValidateScopeKeyLocator(TokenScopeKey scopeKey)
    {
        var declaredTokenType = TokenType;
        if (declaredTokenType != null
            && !string.Equals(declaredTokenType, scopeKey.TokenType, StringComparison.Ordinal))
        {
            // 异常消息经 ToSafeText 文本化（截断 128 + 控制字符过滤），防日志注入面。
            throw new ArgumentException(
                $"令牌类型不一致：TokenScopeKey 声明 tokenType '{AppKeyValidator.ToSafeText(scopeKey.TokenType)}'，" +
                $"本管理器声明 '{AppKeyValidator.ToSafeText(declaredTokenType)}'。" +
                "tokenType / appKey 段仅做一致性校验，不参与缓存键。",
                nameof(scopeKey));
        }

        var bound = Volatile.Read(ref _tenantBinding);
        if (bound != null
            && scopeKey.AppKey != null
            && !string.Equals(bound.AppKey, scopeKey.AppKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"应用归属不一致：TokenScopeKey 声明 AppKey '{AppKeyValidator.ToSafeText(scopeKey.AppKey)}'，" +
                $"当前上下文 AppKey '{AppKeyValidator.ToSafeText(bound.AppKey)}'。" +
                "tokenType / appKey 段仅做一致性校验，不参与缓存键。",
                nameof(scopeKey));
        }
    }
}
