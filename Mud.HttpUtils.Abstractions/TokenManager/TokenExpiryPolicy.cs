// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌过期判定的单一真相（Single Source of Truth）。
/// P1.3（TK-04）修复：收敛 <c>TokenManagerBase</c> 与用户令牌管理器（<c>UserTokenManagerBase</c>）中
/// 分散的"过期判定 / 有效性判定"逻辑，避免各处实现因微妙的取反或比较边界不一致而引发
/// "降级令牌刷新风暴"或"短 TTL 令牌永不命中缓存"等行为差异。
/// </summary>
/// <remarks>
/// 语义约定（Unix 时间戳毫秒）：
/// <list type="bullet">
/// <item><description><c>IsExpired</c>：<c>expire - threshold &lt;= now</c> 视为已过期（应刷新/清理）。</description></item>
/// <item><description><c>IsValid</c>：<c>expire - threshold &gt; now</c> 视为可用（可复用缓存）。</description></item>
/// </list>
/// 二者互为取反，保证"可用于缓存"与"需要刷新/清理"判定在任意实现上严格一致。
/// </remarks>
internal static class TokenExpiryPolicy
{
    /// <summary>
    /// 判定令牌是否已过期（含提前量）。当 <c>expire - threshold &lt;= now</c> 时为 true。
    /// </summary>
    /// <param name="expireMs">令牌过期时间（Unix 时间戳，毫秒）。</param>
    /// <param name="nowMs">当前时间（Unix 时间戳，毫秒）。</param>
    /// <param name="thresholdSeconds">过期提前量（秒）。</param>
    /// <returns>已过期返回 true，否则 false。</returns>
    public static bool IsExpired(long expireMs, long nowMs, int thresholdSeconds)
    {
        if (expireMs <= 0)
            return true;

        var thresholdMs = thresholdSeconds * 1000L;
        return expireMs - thresholdMs <= nowMs;
    }

    /// <summary>
    /// 判定令牌是否仍有效（在提前量之内仍可用）。当 <c>expire - threshold &gt; now</c> 时为 true。
    /// </summary>
    /// <param name="expireMs">令牌过期时间（Unix 时间戳，毫秒）。</param>
    /// <param name="nowMs">当前时间（Unix 时间戳，毫秒）。</param>
    /// <param name="thresholdSeconds">过期提前量（秒）。</param>
    /// <returns>有效返回 true，否则 false。</returns>
    public static bool IsValid(long expireMs, long nowMs, int thresholdSeconds)
        => !IsExpired(expireMs, nowMs, thresholdSeconds);
}