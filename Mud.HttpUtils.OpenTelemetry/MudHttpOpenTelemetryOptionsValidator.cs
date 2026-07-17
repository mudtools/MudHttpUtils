// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.OpenTelemetry;

/// <summary>
/// <see cref="MudHttpOpenTelemetryOptions"/> 的校验器，在选项绑定时验证属性范围。
/// </summary>
/// <remarks>
/// 将原 <see cref="MudHttpOpenTelemetryExtensions.AddMudHttpOpenTelemetryCore"/> 中的运行时
/// <see cref="MudHttpOpenTelemetryOptions.SamplingRatio"/> 范围检查提前到配置绑定阶段，
/// 并补充对 <see cref="MudHttpOpenTelemetryOptions.ExportBatchSize"/>、
/// <see cref="MudHttpOpenTelemetryOptions.ExportIntervalMilliseconds"/>、
/// <see cref="MudHttpOpenTelemetryOptions.ServiceName"/> 等属性的校验。
/// </remarks>
public class MudHttpOpenTelemetryOptionsValidator : IValidateOptions<MudHttpOpenTelemetryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MudHttpOpenTelemetryOptions? options)
    {
        if (options == null)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        if (options.SamplingRatio < 0 || options.SamplingRatio > 1)
            failures.Add($"MudHttpOpenTelemetryOptions: SamplingRatio 必须在 0.0~1.0 范围内，当前值为 {options.SamplingRatio}。");

        if (options.ExportBatchSize.HasValue && options.ExportBatchSize.Value <= 0)
            failures.Add($"MudHttpOpenTelemetryOptions: ExportBatchSize 必须大于 0（null 使用 SDK 默认值），当前值为 {options.ExportBatchSize.Value}。");

        if (options.ExportIntervalMilliseconds.HasValue && options.ExportIntervalMilliseconds.Value <= 0)
            failures.Add($"MudHttpOpenTelemetryOptions: ExportIntervalMilliseconds 必须大于 0（null 使用 SDK 默认值），当前值为 {options.ExportIntervalMilliseconds.Value}。");

        if (string.IsNullOrWhiteSpace(options.ServiceName))
            failures.Add("MudHttpOpenTelemetryOptions: ServiceName 不能为 null 或空白字符串。");

        if (string.IsNullOrWhiteSpace(options.ServiceVersion))
            failures.Add("MudHttpOpenTelemetryOptions: ServiceVersion 不能为 null 或空白字符串。");

        if (string.IsNullOrWhiteSpace(options.DeploymentEnvironment))
            failures.Add("MudHttpOpenTelemetryOptions: DeploymentEnvironment 不能为 null 或空白字符串。");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
