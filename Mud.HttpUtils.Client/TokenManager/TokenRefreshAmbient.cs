// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 架构不变式 I2（R-P0-01）：令牌刷新环境标记 —— 刷新的 HTTP 调用链上禁止再次进入令牌恢复。
/// </summary>
/// <remarks>
/// <para>
/// 缺陷路径：若 OAuth2 令牌端点所使用的 <see cref="System.Net.Http.HttpClient"/> 也挂载了
/// <see cref="TokenRecoveryDelegatingHandler"/>，令牌端点的 401 会重新进入
/// <see cref="TokenRecoveryExecutor"/>，计算出<b>同一个去重键</b>并在
/// <c>RefreshDedupTable</c> 命中"在途未完成"条目 → <c>await existing.Task</c> 即等待自身 → <b>永久挂起</b>。
/// </para>
/// <para>
/// 本标记以 <see cref="AsyncLocal{T}"/> 深度计数实现跨层（HTTP 管道 ↔ 令牌管理）熔断：
/// 随异步流向下传播，对任意 <see cref="ITokenManager"/> 实现与任意 <see cref="System.Net.Http.HttpClient"/>
/// （含第三方注入）统一生效，且<b>零公共 API 变化</b>、零堆分配。
/// </para>
/// <para>
/// 语义即"我正处于刷新调用栈内"，因此守卫只能置于<b>刷新入口</b>（<c>Refresh*WithIsolationAsync</c>）、
/// 短路只能置于<b>恢复入口</b>（<c>TokenRecoveryExecutor.ExecuteAsync</c> 首行）。
/// </para>
/// <para>
/// <b>残留缺口（已记录，非缺陷）</b>：若第三方 <see cref="ITokenManager"/> 实现不响应
/// <see cref="CancellationToken"/>，硬超时只能让<b>等待者</b>离开，刷新任务本身仍挂起，
/// 该异步流上的深度直到任务结束才释放 —— 因该流已被挂起且不会被后续请求复用，无正确性影响。
/// </para>
/// </remarks>
internal static class TokenRefreshAmbient
{
    private static readonly AsyncLocal<int> s_depth = new();

    /// <summary>当前异步流是否处于令牌刷新调用栈内。</summary>
    internal static bool InRefresh => s_depth.Value > 0;

    /// <summary>
    /// 进入一次刷新作用域；<see cref="Scope"/> 释放后恢复进入前的深度。
    /// </summary>
    /// <returns>应在刷新全程（含失效 + 刷新）以 <c>using</c> 持有的作用域句柄。</returns>
    internal static Scope Enter()
    {
        var previous = s_depth.Value;
        s_depth.Value = previous + 1;
        return new Scope(previous);
    }

    /// <summary>
    /// 刷新作用域句柄。<see cref="Dispose"/> 恢复<b>捕获的</b>先前深度（而非无条件递减），
    /// 保证父上下文与并行子分支的深度互不污染。
    /// </summary>
    internal readonly struct Scope : IDisposable
    {
        private readonly int _previous;

        internal Scope(int previous) => _previous = previous;

        /// <inheritdoc />
        public void Dispose() => s_depth.Value = _previous;
    }
}
