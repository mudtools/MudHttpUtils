// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// <see cref="ResilienceOptions"/> 的跨选项校验器。
/// 检查重试策略与超时策略之间的潜在冲突，例如重试总延迟超过单次超时时间。
/// </summary>
/// <remarks>
/// <b>已知限制</b>：此校验器仅能访问 <see cref="ResilienceOptions"/>，无法校验
/// <c>HttpClient.Timeout</c>（通过 <c>MudHttpClientOptions.TimeoutSeconds</c> 配置）与 Polly 超时的跨选项冲突。
/// 建议用户手动确保 <c>HttpClient.Timeout</c> 略大于 <c>Retry.MaxRetryAttempts × Timeout.TimeoutSeconds + 总重试延迟</c>，
/// 避免 HttpClient 超时打断正常的重试流程。详见 Mud.HttpUtils.Resilience README 中的"超时配置说明"。
/// </remarks>
public class ResilienceOptionsValidator : IValidateOptions<ResilienceOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ResilienceOptions options)
    {
        if (options == null)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        // 跨选项校验：重试总延迟 vs 单次超时
        if (options.Retry.Enabled && options.Timeout.Enabled)
        {
            var maxRetryAttempts = options.Retry.MaxRetryAttempts;
            var delayMs = options.Retry.DelayMilliseconds;

            // 计算重试总延迟（仅延迟时间，不含请求执行时间）
            double totalDelaySeconds;
            if (options.Retry.UseExponentialBackoff)
            {
                // 指数退避：D * (2^N - 1) ms
                totalDelaySeconds = delayMs * (Math.Pow(2, maxRetryAttempts) - 1) / 1000.0;
            }
            else
            {
                // 固定延迟：D * N ms
                totalDelaySeconds = delayMs * maxRetryAttempts / 1000.0;
            }

            if (totalDelaySeconds > options.Timeout.TimeoutSeconds)
            {
                failures.Add(
                    $"重试总延迟（{totalDelaySeconds:F1} 秒）超过单次超时时间（{options.Timeout.TimeoutSeconds} 秒）。" +
                    $"这可能导致用户等待时间过长。考虑减少 MaxRetryAttempts、DelayMilliseconds 或增大 TimeoutSeconds。");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
