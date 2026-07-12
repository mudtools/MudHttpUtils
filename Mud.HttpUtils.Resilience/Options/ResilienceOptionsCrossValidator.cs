// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// <see cref="ResilienceOptions"/> 的跨选项后置配置器，在选项绑定时检查
/// <c>HttpClient.Timeout</c>（通过 <see cref="MudHttpClientApplicationOptions"/> 配置）
/// 与 Polly 重试/超时策略之间的潜在冲突，并记录警告日志。
/// </summary>
/// <remarks>
/// <para>
/// 当 <c>HttpClient.Timeout</c> 小于重试总时间（含重试延迟和单次超时）时，
/// HttpClient 全局超时可能打断正常的重试流程，导致用户看到超时异常而非重试后的成功响应。
/// </para>
/// <para>
/// 此校验器仅记录警告日志，不阻止应用启动，因为某些场景下用户可能有意设置较短的全局超时。
/// </para>
/// </remarks>
internal sealed class ResilienceOptionsCrossValidator : IPostConfigureOptions<ResilienceOptions>
{
    private readonly MudHttpClientApplicationOptions? _appOptions;
    private readonly ILogger<ResilienceOptionsCrossValidator> _logger;

    /// <summary>
    /// 初始化 <see cref="ResilienceOptionsCrossValidator"/> 实例。
    /// </summary>
    /// <param name="appOptions">HttpClient 应用配置选项（可选，从 DI 解析）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public ResilienceOptionsCrossValidator(
        IOptions<MudHttpClientApplicationOptions>? appOptions = null,
        ILogger<ResilienceOptionsCrossValidator>? logger = null)
    {
        _appOptions = appOptions?.Value;
        _logger = logger ?? NullLogger<ResilienceOptionsCrossValidator>.Instance;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, ResilienceOptions? options)
    {
        if (options == null || _appOptions == null)
            return;

        // 仅当重试和超时均启用时才校验
        if (!options.Retry.Enabled || !options.Timeout.Enabled)
            return;

        // 查找所有已配置客户端中设置了 TimeoutSeconds 的最小值
        int? minHttpClientTimeoutSeconds = null;
        foreach (var kvp in _appOptions.Clients)
        {
            if (kvp.Value.TimeoutSeconds.HasValue)
            {
                if (!minHttpClientTimeoutSeconds.HasValue || kvp.Value.TimeoutSeconds.Value < minHttpClientTimeoutSeconds.Value)
                    minHttpClientTimeoutSeconds = kvp.Value.TimeoutSeconds.Value;
            }
        }

        if (!minHttpClientTimeoutSeconds.HasValue)
            return;

        // 计算重试总时间（含延迟 + 单次超时 × 重试次数）
        var maxRetryAttempts = options.Retry.MaxRetryAttempts;
        var delayMs = options.Retry.DelayMilliseconds;

        double totalRetryDelaySeconds;
        if (options.Retry.UseExponentialBackoff)
        {
            totalRetryDelaySeconds = delayMs * (Math.Pow(2, maxRetryAttempts) - 1) / 1000.0;
        }
        else
        {
            totalRetryDelaySeconds = delayMs * maxRetryAttempts / 1000.0;
        }

        // 总重试时间 = 重试延迟 + (重试次数 + 1) × 单次超时
        // 其中 +1 是首次请求
        var totalRetryTimeSeconds = totalRetryDelaySeconds + (maxRetryAttempts + 1) * options.Timeout.TimeoutSeconds;

        if (minHttpClientTimeoutSeconds.Value < totalRetryTimeSeconds)
        {
            _logger.LogWarning(
                "HttpClient.Timeout（{HttpClientTimeout} 秒）小于重试总时间预估（{TotalRetryTime:F1} 秒 = 重试延迟 {RetryDelay:F1} 秒 + {RetryAttempts} 次尝试 × 超时 {PerAttemptTimeout} 秒）。" +
                "HttpClient 全局超时可能打断正常的重试流程。建议将 MudHttpClientOptions.TimeoutSeconds 设置为略大于 Retry.MaxRetryAttempts × Timeout.TimeoutSeconds + 总重试延迟。",
                minHttpClientTimeoutSeconds.Value,
                totalRetryTimeSeconds,
                totalRetryDelaySeconds,
                maxRetryAttempts + 1,
                options.Timeout.TimeoutSeconds);
        }
    }
}
