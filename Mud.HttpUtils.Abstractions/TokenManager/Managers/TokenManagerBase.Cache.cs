// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

// -------------------------------------------------------------------------------------------------
//  缓存读写：scope 键解析、命中与有效性判定、字段级失效、字段合并与硬上限 LRU 收敛
//  TokenManagerBase partial: shares all private state with the main file.
// -------------------------------------------------------------------------------------------------
public abstract partial class TokenManagerBase
{
    /// <summary>
    /// 生成作用域缓存键。null 或空数组返回 "default"。
    /// 规范化统一委托 <see cref="ScopeKeyBuilder"/>（Distinct/Ordinal/排序）单一实现。
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
    /// 保留兼容层：委托 <see cref="GetCachedCredentialToken(string)"/> 读取默认作用域。
    /// </summary>
    /// <returns>缓存中的令牌信息，如果不存在或已过期则返回 null。</returns>
    protected CredentialToken? GetCachedCredentialToken()
    {
        return GetCachedCredentialToken(DefaultScopeKey);
    }

    /// <summary>
    /// 从缓存中获取指定作用域的令牌信息，如果不存在或已过期则返回 null。
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
    /// 仅失效缓存条目的 refresh_token，其余字段（AccessToken 等）保留。
    /// 供 invalid_grant 清除回退使用：可疑 refresh_token 被消费后不得继续滞留缓存被后续请求复用。
    /// </summary>
    /// <param name="scopeKey">作用域缓存键。</param>
    protected void InvalidateCachedRefreshToken(string scopeKey)
    {
        if (_tokenCache.TryGet(scopeKey, out var existing) && existing != null)
        {
            // 写时复制 —— 不得原地改写已分发的实例（并发读取方可能看到 RefreshToken == null）。
            _tokenCache.Set(scopeKey, existing.WithRefreshTokenInvalidated());
        }
    }

    /// <summary>
    /// 仅失效缓存条目的访问令牌字段（AccessToken / Expire / IssuedAt），
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
            // 写时复制（字段级保留 refresh_token），避免与锁内刷新交错时
            // 让派生类读到 RefreshToken == null 而静默降级为 client_credentials。
            _tokenCache.Set(scopeKey, existing.WithAccessTokenInvalidated());
        }
    }

    /// <summary>按作用域失效访问令牌（null/空数组 ⇒ 默认作用域）。</summary>
    /// <param name="scopes">令牌作用域数组。</param>
    internal void InvalidateCachedAccessToken(string[]? scopes)
        => InvalidateCachedAccessToken(GetScopeKey(scopes));

    // 凭据字段级合并：刷新响应缺省字段时保留既有凭据，避免静默降级到错误授权流程。
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
            var now = UtcNow.ToUnixTimeMilliseconds();
            var effectiveMaxExpire = now + maxLifetimeMs;
            if (token.Expire > effectiveMaxExpire)
            {
                token.Expire = effectiveMaxExpire;
            }
        }

        _tokenCache.Set(scopeKey, token);

        // 硬上限 LRU 收敛：阻断攻击者/失控代码以海量 scope 组合
        // 制造无界缓存+锁表膨胀（有效期内 CleanupExpiredTokens 清不掉未过期条目）。
        // 常态收敛<b>移出锁内</b> —— 本方法处于【持有 scope 锁】的关键区内，而
        // CleanupExpiredTokens（全表遍历）与 Compact（OrderBy 全排序）在条目多时是 O(n log n)，
        // 会把锁持有时间放大到与缓存规模成正比，进而放大其它等待者的排队时间。
        // 现在：常态只置脏标记（O(1)），由维护定时器在锁外完成；
        // 仅在超过软上限 2 倍时（突发/滥用）才在锁内立即收敛 —— 宁可短暂拉长锁持有时间，也不让内存失控。
        var limit = MaxScopeCacheSize;
        if (_tokenCache.Count > limit)
        {
            if (_tokenCache.Count > (long)limit * 2)
            {
                CleanupExpiredTokens(null);                               // 清过期（顺带消费脏标记）
                if (_tokenCache.Count > limit)                            // 仍超限 → 强制 LRU
                {
                    CompactTo(limit);
                }
            }
            else
            {
                Volatile.Write(ref _compactRequested, 1);                 // 交给维护定时器在锁外收敛
            }
        }
    }

    /// <summary>
    /// 把缓存条目按 LRU 压缩到 <paramref name="limit"/> 以下。
    /// </summary>
    /// <param name="limit">目标条目上限（须 &gt; 0）。</param>
    private void CompactTo(int limit)
    {
        var excess = _tokenCache.Count - limit;
        if (excess <= 0)
            return;

        var ratio = Math.Max(excess / (double)_tokenCache.Count, 0.05);   // 下限 5%，避免 0 取整
        _tokenCache.Compact(ratio);
    }

    /// <summary>
    /// 尝试获取有效的缓存令牌。修复：合并 IsTokenValid + TryGet 为单次查找，
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

        if (!IsCredentialTokenUsable(entry))
            return false;

        token = entry;
        return true;
    }

    /// <summary>
    /// S2-2（治理方案）：异步读穿透探测 —— 当缓存实现 <see cref="IAsyncTokenCache{T}"/> 时，
    /// 经 <see cref="IAsyncTokenCache{T}.GetAsync"/> 读取（桥接器实现下：镜像未命中直达 store），
    /// 有效则返回；否则返回 null。非异步缓存同步返回 null（ValueTask 同步完成路径，无分配）。
    /// 有效性判定与 <see cref="TryGetValidToken"/> 严格同源（TokenExpiryPolicy + TTL 感知阈值）。
    /// </summary>
    private async ValueTask<CredentialToken?> TryGetValidTokenThroughAsync(string scopeKey, CancellationToken cancellationToken)
    {
        if (_tokenCache is not IAsyncTokenCache<CredentialToken> asyncCache)
            return null;

        var entry = await asyncCache.GetAsync(scopeKey, cancellationToken).ConfigureAwait(false);
        return IsCredentialTokenUsable(entry) ? entry : null;
    }

    /// <summary>
    /// 凭据令牌可用性判定（TryGetValidToken 与 S2 异步探测共用的单一实现）。
    /// 有效期判定委托 TokenExpiryPolicy；TTL 感知阈值钳位。
    /// </summary>
    private bool IsCredentialTokenUsable(CredentialToken? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.AccessToken) || entry.Expire <= 0)
            return false;

        var now = UtcNow.ToUnixTimeMilliseconds();
        return TokenExpiryPolicy.IsValid(entry.IssuedAt, entry.Expire, now, ExpireThresholdSeconds);
    }
}
