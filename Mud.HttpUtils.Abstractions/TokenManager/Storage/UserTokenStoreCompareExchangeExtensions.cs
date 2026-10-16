// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 用户维度 CAS 的统一入口与回退路径 —— 能力探测（<see cref="IUserTokenStoreCompareExchange"/>）
/// 优先，未实现时按「读取 → 明文比较 → 写入/删除」<b>非原子</b>回退（与
/// <see cref="TokenStorePurgeExtensions.PurgeAppTokensAsync"/> 的能力探测模式同构）。
/// </summary>
/// <remarks>
/// <para>
/// <b>调用形态（两类静态类型均可）</b>：<c>store.CompareExchangeAccessTokenAsync(userId, tokenType, expected, newValue, ttl)</c> ——
/// 静态类型为实现类（且实现能力接口）时直接命中实例方法（原子）；静态类型为 <see cref="IUserTokenStore"/>
/// 时命中本扩展（内部探测能力接口，命中则委托其原子实现，未命中走非原子回退）。
/// </para>
/// <para>
/// <b>回退路径的显式代价</b>：读取与写入之间存在竞态窗口（并发轮换可能双写/漏删）——
/// 这是「存储未提供原子原语」时的最佳可得语义；对原子性有硬要求的下游应为其 store 实现
/// <see cref="IUserTokenStoreCompareExchange"/>。
/// </para>
/// </remarks>
public static class UserTokenStoreCompareExchangeExtensions
{
    /// <summary>用户维度访问令牌 CAS（统一入口：能力探测优先，否则非原子回退）。</summary>
    /// <param name="store">用户维度令牌存储。</param>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识。</param>
    /// <param name="expectedToken">期望的旧令牌值（不得为空）。</param>
    /// <param name="newToken">新令牌值；null/空 = compare-and-delete。</param>
    /// <param name="expiresInSeconds">新令牌有效秒数（&gt; 0 时生效；≤ 0 = 不设 TTL）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配且写入/删除成功返回 <c>true</c>；键不存在或不匹配返回 <c>false</c>。</returns>
    public static async Task<bool> CompareExchangeAccessTokenAsync(
        this IUserTokenStore store, string userId, string tokenType, string expectedToken, string? newToken,
        long expiresInSeconds = 0, CancellationToken cancellationToken = default)
    {
        if (store is IUserTokenStoreCompareExchange atomicStore)
            return await atomicStore.CompareExchangeAccessTokenAsync(userId, tokenType, expectedToken, newToken, expiresInSeconds, cancellationToken).ConfigureAwait(false);

        var current = await store.GetAccessTokenAsync(userId, tokenType, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(current, expectedToken, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrEmpty(newToken))
        {
            await store.RemoveAsync(userId, tokenType, cancellationToken).ConfigureAwait(false);
            return true;
        }

        await store.SetAccessTokenAsync(userId, tokenType, newToken!, expiresInSeconds, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>用户维度刷新令牌 CAS（统一入口：能力探测优先，否则非原子回退）。</summary>
    /// <param name="store">用户维度令牌存储。</param>
    /// <param name="userId">用户唯一标识符。</param>
    /// <param name="tokenType">令牌类型标识。</param>
    /// <param name="expectedToken">期望的旧刷新令牌值（不得为空）。</param>
    /// <param name="newToken">新刷新令牌值；null/空 = compare-and-delete。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配且写入/删除成功返回 <c>true</c>；键不存在或不匹配返回 <c>false</c>。</returns>
    public static async Task<bool> CompareExchangeRefreshTokenAsync(
        this IUserTokenStore store, string userId, string tokenType, string expectedToken, string? newToken,
        CancellationToken cancellationToken = default)
    {
        if (store is IUserTokenStoreCompareExchange atomicStore)
            return await atomicStore.CompareExchangeRefreshTokenAsync(userId, tokenType, expectedToken, newToken, cancellationToken).ConfigureAwait(false);

        var current = await store.GetRefreshTokenAsync(userId, tokenType, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(current, expectedToken, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrEmpty(newToken))
        {
            await store.RemoveAsync(userId, tokenType, cancellationToken).ConfigureAwait(false);
            return true;
        }

        await store.SetRefreshTokenAsync(userId, tokenType, newToken!, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
