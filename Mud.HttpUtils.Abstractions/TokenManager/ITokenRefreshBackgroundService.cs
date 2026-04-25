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
}
