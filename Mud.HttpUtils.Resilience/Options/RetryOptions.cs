// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience;


/// <summary>
/// 重试策略配置选项。
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// 是否启用重试策略。默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 最大重试次数。默认 3。
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// 初始重试间隔（毫秒）。默认 1000。
    /// </summary>
    public int DelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// 是否使用指数退避策略。默认 true。
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 需要触发重试的 HTTP 状态码集合。为空时使用默认值（408, 429, 5xx）。
    /// </summary>
    public int[]? RetryStatusCodes { get; set; }

    /// <summary>
    /// 获取或设置重试前的回调委托。
    /// </summary>
    /// <remarks>
    /// 回调参数：
    /// - Exception: 导致重试的异常（可能为 null）
    /// - int: 当前重试次数（从 1 开始）
    /// - TimeSpan: 本次重试的延迟时间
    /// </remarks>
    public Func<Exception?, int, TimeSpan, Task>? OnRetry { get; set; }
}
