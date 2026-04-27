// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌刷新失败事件参数。
/// </summary>
public class TokenRefreshFailedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化令牌刷新失败事件参数。
    /// </summary>
    /// <param name="exception">导致刷新失败的异常。</param>
    /// <param name="tokenType">令牌类型。</param>
    /// <param name="retryCount">已重试次数。</param>
    public TokenRefreshFailedEventArgs(Exception exception, string? tokenType = null, int retryCount = 0)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        TokenType = tokenType;
        RetryCount = retryCount;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 导致刷新失败的异常。
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// 令牌类型。
    /// </summary>
    public string? TokenType { get; }

    /// <summary>
    /// 已重试次数。
    /// </summary>
    public int RetryCount { get; }

    /// <summary>
    /// 失败时间戳。
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// 是否应该重试（由事件处理器设置）。
    /// </summary>
    public bool ShouldRetry { get; set; }

    /// <summary>
    /// 降级令牌（由事件处理器设置，用于降级策略）。
    /// </summary>
    public string? FallbackToken { get; set; }
}
