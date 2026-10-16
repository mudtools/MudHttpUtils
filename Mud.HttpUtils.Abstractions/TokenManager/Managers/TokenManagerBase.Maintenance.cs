// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

// -------------------------------------------------------------------------------------------------
//  后台维护：过期条目清理、闲置锁回收、LRU 脏标记消费、维护计时器停止
//  TokenManagerBase partial: shares all private state with the main file.
// -------------------------------------------------------------------------------------------------
public abstract partial class TokenManagerBase
{
    private void CleanupExpiredTokens(object? state)
    {
        if (_disposed)
            return;

        // 修复：Timer 回调异常会被 .NET 静默吞掉，包裹 try-catch 以保证可观测性
        try
        {
            lock (_cleanupLock)
            {
                if (_disposed)
                    return;

                var now = UtcNow.ToUnixTimeMilliseconds();

                foreach (var key in _tokenCache.Keys.ToList())
                {
                    // 修复：默认作用域（DefaultScopeKey）令牌过期后也应被定时清理，
                    // 下次访问 GetOrRefreshTokenAsync 时会自动重新获取，不再跳过。
                    // 注意：CleanupUnusedLocks 仍保留对 DefaultScopeKey 的跳过，以避免默认作用域锁被回收。

                    // 收敛：过期判定统一委托 TokenExpiryPolicy，与 TryGetValidToken 严格一致
                    // TTL 感知阈值
                    if (_tokenCache.TryGet(key, out var entry)
                        && entry != null
                        && TokenExpiryPolicy.IsExpired(entry.IssuedAt, entry.Expire, now, ExpireThresholdSeconds))
                    {
                        _tokenCache.TryRemove(key, out _);
                        _keyedLockTable.TryRetire(key);   // 经 retire 协议统一回收
                    }
                }

                // 顺带清理过期的负缓存条目
                foreach (var kv in _recentFailures)
                {
                    if (kv.Value.UntilMs <= now)
                        _recentFailures.TryRemove(kv.Key, out _);
                }

                // 消费 UpdateToken 置下的 LRU 收敛脏标记。
                // 此处由维护定时器驱动（不持有任何 scope 锁）⇒ 全表遍历与 Compact 的开销不再落在请求关键区。
                if (Interlocked.Exchange(ref _compactRequested, 0) == 1 && _tokenCache.Count > MaxScopeCacheSize)
                {
                    CompactTo(MaxScopeCacheSize);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TokenManagerBase: CleanupExpiredTokens 回调异常: {ex.Message}");
        }
    }

    private void CleanupUnusedLocks(object? state)
    {
        if (_disposed)
            return;

        // 修复：Timer 回调异常会被 .NET 静默吞掉，包裹 try-catch 以保证可观测性
        try
        {
            lock (_cleanupLock)
            {
                if (_disposed)
                    return;

                foreach (var key in _keyedLockTable.Keys.ToList())
                {
                    if (key == DefaultScopeKey)
                        continue;

                    // 仅当缓存中已无该作用域令牌时才退休对应的锁。
                    // TryRetire 内部以 retire 协议保证：若锁正被占用则仅标记退休，由最后一个 Releaser 完成移除。
                    if (!_tokenCache.TryGet(key, out _))
                    {
                        _keyedLockTable.TryRetire(key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TokenManagerBase: CleanupUnusedLocks 回调异常: {ex.Message}");
        }
    }

    private bool _timersStopped;

    /// <summary>
    /// 停止两个租户维护 Timer。与 Timer 回调共用 _cleanupLock，天然互斥；
    /// _timersStopped 一次性标志保证可重入幂等。
    /// </summary>
    private void StopMaintenanceTimers()
    {
        lock (_cleanupLock)
        {
            if (_timersStopped)
                return;
            _timersStopped = true;
            _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _lockCleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _cleanupTimer?.Dispose();
            _lockCleanupTimer?.Dispose();
        }
    }
}
