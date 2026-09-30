// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="TokenRecoveryOptions"/> 的校验器，在选项绑定时验证必填字段和取值范围。
/// </summary>
public class TokenRecoveryOptionsValidator : IValidateOptions<TokenRecoveryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TokenRecoveryOptions? options)
    {
        if (options == null)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        if (options.RecoveryMaxRetries < 0)
            failures.Add($"TokenRecoveryOptions: RecoveryMaxRetries 不能为负数，当前值为 {options.RecoveryMaxRetries}。");

        if (string.IsNullOrWhiteSpace(options.TokenScheme))
            failures.Add("TokenRecoveryOptions: TokenScheme 不能为 null 或空字符串。");

        // SR-H2/H3（P1.4，D4）请求体缓冲上限非负校验（属性 setter 已防御，此处覆盖配置绑定路径）
        if (options.MaxCachedRequestBodyBytes < 0)
            failures.Add($"TokenRecoveryOptions: MaxCachedRequestBodyBytes 不能为负数，当前值为 {options.MaxCachedRequestBodyBytes}。");

        // WX-01（Phase A）响应体捕获上限非负校验（属性 setter 已防御，此处覆盖配置绑定路径）
        if (options.MaxCapturedResponseBodyBytes < 0)
            failures.Add($"TokenRecoveryOptions: MaxCapturedResponseBodyBytes 不能为负数，当前值为 {options.MaxCapturedResponseBodyBytes}。");

        // I3（R-P0-02）等待硬超时非负校验（属性 setter 已防御，此处覆盖配置绑定路径）
        if (options.RefreshWaitHardTimeoutSeconds < 0)
            failures.Add($"TokenRecoveryOptions: RefreshWaitHardTimeoutSeconds 不能为负数，当前值为 {options.RefreshWaitHardTimeoutSeconds}。");

        // R-P3-02 ①：补齐既有属性的取值校验（此前仅校验 RecoveryMaxRetries / TokenScheme / 两个字节上限）
        if (options.RefreshTimeoutSeconds <= 0)
            failures.Add($"TokenRecoveryOptions: RefreshTimeoutSeconds 必须大于 0，当前值为 {options.RefreshTimeoutSeconds}。");

        if (options.RefreshDedupWindowSeconds < 0)
            failures.Add($"TokenRecoveryOptions: RefreshDedupWindowSeconds 不能为负数，当前值为 {options.RefreshDedupWindowSeconds}。");

        if (options.MaxDedupEntries <= 0)
            failures.Add($"TokenRecoveryOptions: MaxDedupEntries 必须大于 0，当前值为 {options.MaxDedupEntries}。");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
