// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="MudHttpClientApplicationOptions"/> 的启动期校验器（CFG-02）。
/// </summary>
/// <remarks>
/// <para>
/// 仅对「确定不可用」的组合返回 <c>ValidateOptionsResult.Fail</c>：
/// <see cref="MudHttpClientApplicationOptions.DefaultClientName"/> 指向一个未配置
/// <see cref="MudHttpClientOptions.BaseAddress"/> 的客户端 —— 该客户端不会被注册，
/// 其 <c>TimeoutSeconds</c> / <c>DefaultHeaders</c> / <c>AllowCustomBaseUrls</c> 必然全部失效。
/// </para>
/// <para>
/// 其余「未配置 BaseAddress」的客户端不可判定为错误（可能是有意留空、由 <c>HttpClient</c> 默认基地址承载），
/// 故仅由 <see cref="MudHttpClientApplicationOptionsPostConfigure"/> 记录警告，不阻断启动。
/// </para>
/// </remarks>
internal sealed class MudHttpClientApplicationOptionsValidator
    : IValidateOptions<MudHttpClientApplicationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MudHttpClientApplicationOptions options)
    {
        if (options is null)
            return ValidateOptionsResult.Success;

        var defaultClientName = options.DefaultClientName;
        if (!string.IsNullOrWhiteSpace(defaultClientName)
            // 上一行的空白判定在 netstandard2.0 目标上没有 NotNullWhen 标注，
            // 故显式使用 ! 声明此处可空性已由守卫收敛。
            && options.Clients.TryGetValue(defaultClientName!, out var defaultClient)
            && string.IsNullOrWhiteSpace(defaultClient.BaseAddress))
        {
            // MT-12 后：无 BaseAddress 的客户端仍会被注册（Timeout/DefaultHeaders 生效），
            // 但它作为「默认客户端」时无法承载相对 URL 请求 ⇒ 仍视为不可用组合。
            return ValidateOptionsResult.Fail(
                $"MudHttpClients:DefaultClientName 指向的客户端 '{options.DefaultClientName}' 未配置 BaseAddress，" +
                "该客户端无法处理相对 URL 请求（其 TimeoutSeconds / DefaultHeaders 仍会生效）。" +
                "请补充 BaseAddress，或修正 DefaultClientName。");
        }

        // MT-24（BC-26）：原 MudHttpClientOptions.AppKey 已移除（死配置），此处对应的格式校验一并移除。
        // 应用标识的格式约束仍由 AppKeyValidator（AppManager 注册/查询入口）与
        // MudHttpAppManagementOptions.RegisteredAppKeys 校验承担。

        return ValidateOptionsResult.Success;
    }
}
