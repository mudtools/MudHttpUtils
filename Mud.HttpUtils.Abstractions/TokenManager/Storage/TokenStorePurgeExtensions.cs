// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 按应用清库的统一入口与回退路径 —— 能力探测（<see cref="ITokenStoreAppPurge"/>）优先，
/// 未实现时按「枚举 tokenType → 逐类型 RemoveAsync」回退（与微信 <c>IWechatTokenStoreBatchRemove</c>
/// 的 is 探测模式同构，下游接口可做适配委托）。
/// </summary>
public static class TokenStorePurgeExtensions
{
    /// <summary>
    /// 按应用清库（统一入口）。
    /// </summary>
    /// <param name="store">令牌存储。</param>
    /// <param name="appKey">
    /// 应用标识。实现 <see cref="ITokenStoreAppPurge"/> 的 store 按该维度精确清库（不误伤其他 appKey）；
    /// 未实现的 store 忽略该参数，仅清理实例自身命名空间（回退语义）。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际删除的键数（回退路径 = 删除的 tokenType 数，每类型含 access+refresh 两键）。</returns>
    public static async Task<int> PurgeAppTokensAsync(this ITokenStore store, string? appKey, CancellationToken cancellationToken = default)
    {
        if (store is ITokenStoreAppPurge purge)
            return await purge.PurgeAppAsync(appKey!, cancellationToken).ConfigureAwait(false);

        // 回退：枚举本实例命名空间内的全部 tokenType，逐类型删除（access+refresh 双键由 RemoveAsync 承担）。
        var count = 0;
        foreach (var tokenType in await store.GetTokenTypesAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await store.RemoveAsync(tokenType, cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }
}
