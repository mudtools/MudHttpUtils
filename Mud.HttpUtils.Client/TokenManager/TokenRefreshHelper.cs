using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Mud.HttpUtils;

internal static class TokenRefreshHelper
{
    /// <summary>
    /// SR-M4（P3.2，D14）后台刷新注册守卫的单一实现：<see cref="ITokenManager.SupportsBackgroundRefresh"/>
    /// 为 false 的管理器静默跳过并记日志。两个后台服务（net6+ <see cref="TokenRefreshHostedService"/> 与
    /// netstandard2.0 <see cref="TokenRefreshBackgroundService"/>）共用，消除双实现行为漂移（单一真相原则）。
    /// </summary>
    /// <returns>true = 允许注册；false = 已跳过（调用方不得登记该管理器）。</returns>
    public static bool TryRegisterTokenManager(
        ConcurrentDictionary<string, ITokenManager> tokenManagers,
        ITokenManager tokenManager,
        string? name,
        ILogger logger)
    {
        if (!tokenManager.SupportsBackgroundRefresh)
        {
            MudHttpClientLog.TokenManagerSkippedNoBackgroundRefresh(logger, name ?? tokenManager.GetType().Name);
            return false;
        }

        var key = name ?? Guid.NewGuid().ToString("N");
        tokenManagers[key] = tokenManager;
        MudHttpClientLog.TokenManagerRegistered(logger, key);
        return true;
    }

    /// <summary>
    /// 刷新所有已注册的令牌管理器，返回是否应继续后台刷新调度。
    /// </summary>
    /// <remarks>
    /// P1.6（TK-10）：<see cref="TokenRefreshBackgroundOptions.StopOnError"/> 为 <c>true</c> 时任一管理器刷新失败即
    /// 返回 <c>false</c>（停止调度）；为 <c>false</c> 时记录错误并继续，返回 <c>true</c>。
    /// 本重载为单次调用的简便形式（内部使用临时状态），供无需跨调度周期保留"连续失败计数"的调用方使用。
    /// </remarks>
    public static async Task<bool> RefreshAllTokenManagersAsync(
        ConcurrentDictionary<string, ITokenManager> tokenManagers,
        ILogger logger,
        TokenRefreshBackgroundOptions options,
        CancellationToken cancellationToken)
    {
        var state = new TokenRefreshLoopState();
        return await RefreshAllTokenManagersAsync(tokenManagers, logger, options, cancellationToken, state).ConfigureAwait(false);
    }

    /// <summary>
    /// 刷新所有已注册的令牌管理器，返回是否应继续后台刷新调度。
    /// </summary>
    /// <remarks>
    /// P1.6（TK-10 / TK-10-max）停止语义：
    /// <list type="bullet">
    ///   <item><see cref="TokenRefreshBackgroundOptions.StopOnError"/> 为 <c>true</c>：任一管理器失败即停止（返回 <c>false</c>）。</item>
    ///   <item><see cref="TokenRefreshBackgroundOptions.MaxConsecutiveFailures"/> 大于 0：连续失败达到该阈值即停止（返回 <c>false</c>），
    ///     与 <see cref="TokenRefreshBackgroundOptions.StopOnError"/> 正交；默认 0 表示不因连续失败次数停止。</item>
    /// </list>
    /// <paramref name="state"/> 由调用方跨调度周期保留，用于累计"连续失败的刷新周期数"；
    /// 当某个周期内所有管理器均成功时自动复位为 0。
    /// </remarks>
    public static async Task<bool> RefreshAllTokenManagersAsync(
        ConcurrentDictionary<string, ITokenManager> tokenManagers,
        ILogger logger,
        TokenRefreshBackgroundOptions options,
        CancellationToken cancellationToken,
        TokenRefreshLoopState state)
    {
        if (tokenManagers.IsEmpty)
        {
            // L-9：统一经 Reset() 原子复位（与 RestartAsync 并发安全）
            state.Reset();
            return true;
        }

        var cycleHadFailure = false;

        foreach (var kvp in tokenManagers)
        {
            try
            {
                MudHttpClientLog.TokenRefreshStarting(logger, kvp.Key);
                var token = await kvp.Value.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    MudHttpClientLog.TokenRefreshCompleted(logger, kvp.Key);
                }
            }
            catch (ObjectDisposedException ex)
            {
                // MT-15：原实现把任何 ObjectDisposedException 都当作"管理器已释放"并<b>永久反注册</b>，
                // 且没有任何恢复机制 —— 一次内部资源短期不可用（或宿主重建管理器）就会让该管理器的
                // 后台刷新永久停止，而宿主仍显示"健康"。
                // 现在只有确认管理器自身已 Dispose 时才反注册；否则按普通失败处理（保留登记、计入连续失败）。
                if (kvp.Value is TokenManagerBase baseManager && baseManager.IsDisposed)
                {
                    MudHttpClientLog.TokenManagerDisposed(logger, kvp.Key);
                    tokenManagers.TryRemove(kvp.Key, out _);
                }
                else
                {
                    MudHttpClientLog.TokenRefreshFailed(logger, kvp.Key, ex);
                    cycleHadFailure = true;

                    if (options.StopOnError)
                    {
                        MudHttpClientLog.TokenRefreshFailedAndStopped(logger, kvp.Key);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MudHttpClientLog.TokenRefreshFailed(logger, kvp.Key, ex);
                cycleHadFailure = true;

                if (options.StopOnError)
                {
                    MudHttpClientLog.TokenRefreshFailedAndStopped(logger, kvp.Key);
                    return false;
                }
            }
        }

        // 全成功则复位连续失败计数，否则累加
        // L-9：改为原子操作，与 RestartAsync 的 Reset() 并发安全（此前为普通字段读写）。
        if (!cycleHadFailure)
        {
            state.Reset();
        }
        else
        {
            Interlocked.Increment(ref state.ConsecutiveFailures);
        }

        // MaxConsecutiveFailures > 0 且达到阈值：停止调度（与 StopOnError 正交）
        var consecutiveFailures = Volatile.Read(ref state.ConsecutiveFailures);
        if (options.MaxConsecutiveFailures > 0 && consecutiveFailures >= options.MaxConsecutiveFailures)
        {
            MudHttpClientLog.TokenRefreshFailedAndStopped(logger, "(max-consecutive-failures)");
            return false;
        }

        return true;
    }
}

/// <summary>
/// 令牌刷新循环的状态，用于跨调度周期保留"连续失败周期数"。
/// </summary>
internal sealed class TokenRefreshLoopState
{
    /// <summary>连续失败的刷新周期数。</summary>
    public int ConsecutiveFailures;

    /// <summary>
    /// L-9：复位连续失败计数（供 <c>RestartAsync</c> 使用）。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="Interlocked.Exchange(ref int, int)"/> 保证与刷新循环内的自增/归零并发安全。
    /// </remarks>
    public void Reset() => Interlocked.Exchange(ref ConsecutiveFailures, 0);
}