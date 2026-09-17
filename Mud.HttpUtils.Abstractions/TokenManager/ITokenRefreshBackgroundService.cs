// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌主动刷新后台服务接口。
/// </summary>
public interface ITokenRefreshBackgroundService
{
    /// <summary>
    /// 启动后台令牌刷新。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止后台令牌刷新。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册需要后台刷新的令牌管理器。
    /// </summary>
    /// <param name="tokenManager">令牌管理器实例。</param>
    /// <param name="name">令牌管理器名称（可选，用于日志标识）。</param>
    void RegisterTokenManager(ITokenManager tokenManager, string? name = null);

    /// <summary>
    /// L-9：后台刷新是否已因 <see cref="TokenRefreshBackgroundOptions.StopOnError"/>
    /// 或 <see cref="TokenRefreshBackgroundOptions.MaxConsecutiveFailures"/> 而<b>主动停止调度</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 停止是"优雅退出循环"而非"抛异常停止宿主"，因此进程仍健康、日志中只有一条 Critical ——
    /// 若不主动探测，运维侧无法区分"服务在跑但没活干"与"服务已停止调度"。
    /// 可在健康检查中读取本属性。
    /// </para>
    /// <para>正常的宿主停止（<c>StopAsync</c>）<b>不</b>使其为 <c>true</c>。</para>
    /// </remarks>
    bool IsStopped { get; }

    /// <summary>
    /// L-9：重新启动已停止的后台刷新调度（复位连续失败计数后继续按间隔刷新）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    /// <remarks>
    /// <para>
    /// 用于"IdP 短时故障导致连续失败达阈值 → 服务停止调度 → 故障恢复后人工/探针触发恢复"的场景，
    /// 避免必须重启进程。修复前不存在任何恢复通道：一次网络抖动即永久停止该管理器的后台刷新。
    /// </para>
    /// <para><b>幂等</b>：服务仍在运行时调用本方法为无操作。</para>
    /// <para>宿主正在停止（<c>StopAsync</c> 已触发）时不生效。</para>
    /// </remarks>
    Task RestartAsync(CancellationToken cancellationToken = default);
}
