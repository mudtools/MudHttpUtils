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

    /// <summary>
    /// P2.4（TK-04）TTL 感知阈值。对短 TTL 令牌，有效阈值被钳位为 <c>min(configuredThreshold, ttl/2)</c>，
    /// 避免"提前量过大导致 token 刚签发即被判为需刷新（短 TTL 令牌永不命中缓存）"。
    /// 当 <paramref name="issuedAtMs"/> 未提供（&lt;= 0）时回退到配置阈值，保持与旧行为一致。
    /// </summary>
    /// <param name="issuedAtMs">令牌签发时间（Unix 时间戳，毫秒）。</param>
    /// <param name="expireMs">令牌过期时间（Unix 时间戳，毫秒）。</param>
    /// <param name="configuredThresholdSeconds">配置的过期提前量（秒）。</param>
    /// <returns>应为该令牌应用的有效提前量（秒）。</returns>
    public static long EffectiveThresholdSeconds(long issuedAtMs, long expireMs, int configuredThresholdSeconds)
    {
        if (issuedAtMs <= 0)
            return configuredThresholdSeconds;

        var ttlSeconds = (expireMs - issuedAtMs) / 1000L;
        if (ttlSeconds <= 0)
            return configuredThresholdSeconds;

        var halfTtl = ttlSeconds / 2L;
        return Math.Min(configuredThresholdSeconds, halfTtl);
    }

    /// <summary>
    /// P2.4（TK-04）TTL 感知的过期判定。当 <c>expire - effectiveThreshold &lt;= now</c> 时为 true。
    /// </summary>
    /// <param name="issuedAtMs">令牌签发时间（Unix 时间戳，毫秒）。</param>
    /// <param name="expireMs">令牌过期时间（Unix 时间戳，毫秒）。</param>
    /// <param name="nowMs">当前时间（Unix 时间戳，毫秒）。</param>
    /// <param name="configuredThresholdSeconds">配置的过期提前量（秒）。</param>
    /// <returns>已过期返回 true，否则 false。</returns>
    public static bool IsExpired(long issuedAtMs, long expireMs, long nowMs, int configuredThresholdSeconds)
    {
        if (expireMs <= 0)
            return true;

        var effectiveThresholdMs = EffectiveThresholdSeconds(issuedAtMs, expireMs, configuredThresholdSeconds) * 1000L;
        return expireMs - effectiveThresholdMs <= nowMs;
    }

    /// <summary>
    /// P2.4（TK-04）TTL 感知的有效性判定。当 <c>expire - effectiveThreshold &gt; now</c> 时为 true。
    /// </summary>
    /// <param name="issuedAtMs">令牌签发时间（Unix 时间戳，毫秒）。</param>
    /// <param name="expireMs">令牌过期时间（Unix 时间戳，毫秒）。</param>
    /// <param name="nowMs">当前时间（Unix 时间戳，毫秒）。</param>
    /// <param name="configuredThresholdSeconds">配置的过期提前量（秒）。</param>
    /// <returns>有效返回 true，否则 false。</returns>
    public static bool IsValid(long issuedAtMs, long expireMs, long nowMs, int configuredThresholdSeconds)
        => !IsExpired(issuedAtMs, expireMs, nowMs, configuredThresholdSeconds);
}