// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="OAuth2Options"/> 的校验器，在选项绑定时验证必填字段和端点 HTTPS 一致性。
/// </summary>
public class OAuth2OptionsValidator : IValidateOptions<OAuth2Options>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OAuth2Options options)
    {
        if (options == null)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
            failures.Add("OAuth2Options: ClientId 不能为空。");

        if (string.IsNullOrWhiteSpace(options.TokenEndpoint))
            failures.Add("OAuth2Options: TokenEndpoint 不能为空。");
        else if (options.RequireHttps &&
                !options.TokenEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            failures.Add($"OAuth2Options: RequireHttps 为 true 但 TokenEndpoint（{options.TokenEndpoint}）不是 HTTPS 端点。");

        if (!string.IsNullOrWhiteSpace(options.RevocationEndpoint) &&
            options.RequireHttps &&
            !options.RevocationEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            failures.Add($"OAuth2Options: RequireHttps 为 true 但 RevocationEndpoint（{options.RevocationEndpoint}）不是 HTTPS 端点。");

        if (!string.IsNullOrWhiteSpace(options.IntrospectionEndpoint) &&
            options.RequireHttps &&
            !options.IntrospectionEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            failures.Add($"OAuth2Options: RequireHttps 为 true 但 IntrospectionEndpoint（{options.IntrospectionEndpoint}）不是 HTTPS 端点。");

        if (options.ExpirySafetyMarginSeconds < 0)
            failures.Add("OAuth2Options: ExpirySafetyMarginSeconds 不能为负数。");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
