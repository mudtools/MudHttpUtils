// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌存储的<b>按应用清库</b>能力探测式可选接口（组件级可选接口，不进
/// <see cref="ITokenStore"/> 最小 SPI）。
/// </summary>
/// <remarks>
/// <para><b>动机（双下游证据）</b>：凭据（appSecret）变更时须按 appKey 级联清空该应用全部令牌 ——
/// 飞书已私有实现整套，微信被迫复制键解析做 SCAN 枚举。本接口把键解析与枚举内聚到实现方
/// （键布局单一事实源），终结下游复制。</para>
/// <para><b>语义约定</b>：
/// <list type="bullet">
/// <item><description><see cref="EnumerateKeysAsync"/> 为<b>快照语义</b>：返回集合是枚举时刻的完整快照。</description></item>
/// <item><description><see cref="PurgeAppAsync"/> 返回实际删除键数；<b>不得静默半删</b>——
/// 部分失败时抛出聚合异常（实现方自行保证失败语义）。</description></item>
/// <item><description><paramref name="appKey"/> 为 null/空白时按实现方的「共享命名空间」约定处置。</description></item>
/// <item><description>未实现本接口的 store：<see cref="TokenStorePurgeExtensions.PurgeAppTokensAsync"/>
/// 回退路径按「<see cref="ITokenStore.GetTokenTypesAsync"/> 枚举 → 逐类型 <see cref="ITokenStore.RemoveAsync"/>」
/// 等价清库（仅实例自身命名空间内，无跨 appKey 维度）。</description></item>
/// </list></para>
/// </remarks>
public interface ITokenStoreAppPurge
{
    /// <summary>枚举指定 appKey 名下全部键（含 tokenType / access|refresh 维度），快照语义。</summary>
    /// <param name="appKey">应用标识；null/空白时按实现方的共享命名空间约定处置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>枚举时刻的键快照（不保证顺序）。</returns>
    Task<IReadOnlyCollection<string>> EnumerateKeysAsync(string appKey, CancellationToken cancellationToken = default);

    /// <summary>按应用清库：返回实际删除键数；部分失败时抛出聚合异常（不得静默半删）。</summary>
    /// <param name="appKey">应用标识；null/空白时按实现方的共享命名空间约定处置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际删除的键数。</returns>
    Task<int> PurgeAppAsync(string appKey, CancellationToken cancellationToken = default);
}
