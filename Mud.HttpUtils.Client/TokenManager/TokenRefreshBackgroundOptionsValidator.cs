// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="TokenRefreshBackgroundOptions"/> 的校验器，检查刷新间隔与重试延迟之间的潜在冲突。
/// </summary>
/// <remarks>
/// 当 <see cref="TokenRefreshBackgroundOptions.RetryDelaySeconds"/> 大于等于
/// <see cref="TokenRefreshBackgroundOptions.RefreshIntervalSeconds"/> 时，
/// 重试延迟会跨越下一个刷新周期，可能导致刷新逻辑混乱。此校验器记录警告但不阻止启动。
/// </remarks>
public class TokenRefreshBackgroundOptionsValidator : IValidateOptions<TokenRefreshBackgroundOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TokenRefreshBackgroundOptions? options)
    {
        if (options == null)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        // 交叉校验：重试延迟不应超过刷新间隔
        if (options.Enabled && options.RetryDelaySeconds >= options.RefreshIntervalSeconds)
        {
            failures.Add(
                $"TokenRefreshBackgroundOptions: RetryDelaySeconds（{options.RetryDelaySeconds} 秒）大于等于 RefreshIntervalSeconds（{options.RefreshIntervalSeconds} 秒）。" +
                "重试延迟会跨越下一个刷新周期，可能导致刷新逻辑混乱。建议将 RetryDelaySeconds 设置为小于 RefreshIntervalSeconds 的值。");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
