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

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
