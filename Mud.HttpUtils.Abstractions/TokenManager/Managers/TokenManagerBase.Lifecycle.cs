// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

// -------------------------------------------------------------------------------------------------
//  生命周期：Dispose 链（可重入 + 每步幂等）与 internal 测试观测钩子
//  TokenManagerBase partial: shares all private state with the main file.
// -------------------------------------------------------------------------------------------------
public abstract partial class TokenManagerBase
{
    /// <inheritdoc />
    // Dispose 补充缓存释放，且不再 Dispose SemaphoreSlim。
    // 1. 补上 _tokenCache.Dispose()（原实现仅 Clear()，未释放缓存底层资源）。
    // 2. _keyedLockTable.Dispose() 与 KeyedLockTable 内部"不 Dispose SemaphoreSlim"决策一致：
    //    SemaphoreSlim.Dispose 与在途 WaitAsync/Release 并存会抛 ObjectDisposedException，
    //    破坏"Dispose 后允许在途请求完成、其 Release 不抛异常"的契约。
    // SR-H1（P1.3，D3）Dispose 链重构：可重入 + 每步幂等。
    // 原实现第一行 if (_disposed) return; 在派生类（如 UserTokenManagerBase）先行置位 _disposed 后
    // 调 base.Dispose 时直接早退，导致基类维护 Timer / 锁表 / 缓存永不释放（修复引入的回归）。
    // 新契约（写入基类 XML 文档，见本类 remarks）：
    //   1) Dispose(bool) 可重入、幂等；不以 _disposed 早退（派生类置位后仍须执行基类释放）。
    //   2) 派生类覆写时应在开头自行检查/置位 _disposed，
    //      且无论标志状态如何都必须调用 base.Dispose(disposing)。
    //   3) 各释放步骤自身幂等。
    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;                        // 幂等置位（volatile 写）

        if (!disposing)
            return;

        StopMaintenanceTimers();                 // 内部幂等（_timersStopped 一次性标志）
        _keyedLockTable.Dispose();               // 现有实现已幂等（全量置 Retired + Clear）
        _tokenCache.Dispose();                   // 两个缓存实现均自带 _disposed 守卫
    }

    /// <summary>
    /// SR-H1（P1.3）测试观测钩子：维护 Timer 是否仍在运行（经 InternalsVisibleTo 供测试断言 Dispose 后 Timer 停止）。
    /// </summary>
    internal bool TimersActive => !_timersStopped;

    /// <summary>
    /// 是否已释放。供 <c>TokenRefreshHelper</c>（Client 程序集）区分
    /// 「管理器已被释放（应反注册）」与「管理器<b>暂时</b>不可用（不应永久反注册）」。
    /// </summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// SR-M5（P3.3）测试观测钩子：作用域缓存当前条目数（供断言硬上限 LRU 收敛）。
    /// </summary>
    internal int CacheCountInternal => _tokenCache.Count;

    /// <summary>
    /// R-P2-03 测试观测钩子：当前是否已置下"待 LRU 收敛"脏标记（常态超限应置位、锁内不应已收敛）。
    /// </summary>
    internal bool CompactRequestedForTest => Volatile.Read(ref _compactRequested) == 1;

    /// <summary>
    /// R-P2-03 测试观测钩子：在<b>测试线程</b>上同步执行一次维护回调（等价于定时器触发一次），
    /// 使"锁外收敛"可被确定性断言，而不必等待真实定时器周期。
    /// </summary>
    internal void RunMaintenanceForTest() => CleanupExpiredTokens(null);

    /// <inheritdoc />
    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
