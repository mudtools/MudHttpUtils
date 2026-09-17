// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Observability;

/// <summary>
/// <see cref="TokenRefreshHealthCheckOptions"/> 的校验器，在选项绑定时验证属性范围。
/// </summary>
/// <remarks>
/// 将原 <see cref="TokenRefreshHealthCheck"/> 构造函数中的运行时校验提前到配置绑定阶段，
/// 使无效配置在应用启动时即被检测，而非延迟到健康检查首次执行时。
/// </remarks>
public class TokenRefreshHealthCheckOptionsValidator : IValidateOptions<TokenRefreshHealthCheckOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TokenRefreshHealthCheckOptions options)
    {
        if (options == null)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        if (options.WindowSeconds <= 0)
            failures.Add("TokenRefreshHealthCheckOptions: WindowSeconds 必须为正数。");

        if (options.DegradedThreshold < 0 || options.DegradedThreshold > 1)
            failures.Add("TokenRefreshHealthCheckOptions: DegradedThreshold 必须在 0~1 范围内。");

        if (options.CriticalThreshold < 0 || options.CriticalThreshold > 1)
            failures.Add("TokenRefreshHealthCheckOptions: CriticalThreshold 必须在 0~1 范围内。");

        if (options.CriticalThreshold < options.DegradedThreshold)
            failures.Add("TokenRefreshHealthCheckOptions: CriticalThreshold 必须 >= DegradedThreshold。");

        if (options.MinSampleSize < 0)
            failures.Add("TokenRefreshHealthCheckOptions: MinSampleSize 不能为负数。");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
