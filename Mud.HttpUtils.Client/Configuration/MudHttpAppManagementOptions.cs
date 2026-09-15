// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// 多应用管理接线自检选项。
/// </summary>
public sealed class MudHttpAppManagementOptions
{
    /// <summary>是否要求必须注册 <see cref="IAppContextHolder"/>。默认 <c>true</c>。</summary>
    public bool RequireAppContextHolder { get; set; } = true;

    /// <summary>是否要求必须注册 <see cref="IAppManager{IMudAppContext}"/>。默认 <c>false</c>（仅告警不阻断）。</summary>
    public bool RequireAppManager { get; set; }

    /// <summary>已注册应用（用于校验 DefaultClientName/AppKey 映射闭合）。默认为空。</summary>
    public IList<string> RegisteredAppKeys { get; } = new List<string>();
}

/// <summary>
/// 多应用管理接线自检选项校验器。
/// </summary>
internal sealed class MudHttpAppManagementOptionsValidator : IValidateOptions<MudHttpAppManagementOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MudHttpAppManagementOptions options)
    {
        if (options is null)
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        // 校验 RegisteredAppKeys 中的 key 格式
        foreach (var appKey in options.RegisteredAppKeys)
        {
            try
            {
                AppKeyValidator.Validate(appKey, nameof(appKey));
            }
            catch (ArgumentException ex)
            {
                errors.Add($"RegisteredAppKeys 包含非法 appKey '{appKey}'：{ex.Message}");
            }
        }

        if (errors.Count > 0)
            return ValidateOptionsResult.Fail(string.Join("; ", errors));

        return ValidateOptionsResult.Success;
    }
}
