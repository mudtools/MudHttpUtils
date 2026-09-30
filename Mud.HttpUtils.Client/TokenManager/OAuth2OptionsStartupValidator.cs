// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// R-P2-01：OAuth2 选项的<b>启动期</b>强制校验（fail-fast）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OAuth2OptionsValidator"/> 只在选项<b>被首次访问</b>时触发（懒校验）。
/// 若宿主配置了错误的端点（如漏配 <c>TokenEndpoint</c>），故障会推迟到"首次取令牌"时才暴露 ——
/// 此时通常已在处理真实业务请求，排障成本更高。本宿主服务在应用启动期主动读取一次选项，
/// 使配置错误在启动阶段即以 <see cref="OptionsValidationException"/> 暴露。
/// </para>
/// <para>
/// <b>适用边界</b>：仅在存在 Host 的应用（ASP.NET Core / Worker / Generic Host）中生效 ——
/// 非 Host 场景（直接 <c>ServiceCollection.BuildServiceProvider()</c>）不会启动托管服务，
/// 此时仍由懒校验兜底，行为不劣于 2.0.x。
/// </para>
/// </remarks>
internal sealed class OAuth2OptionsStartupValidator : IHostedService
{
    private readonly IOptions<OAuth2Options> _options;

    /// <summary>初始化启动期校验器。</summary>
    /// <param name="options">OAuth2 选项（读取 <c>Value</c> 即触发 <c>IValidateOptions</c> 链）。</param>
    public OAuth2OptionsStartupValidator(IOptions<OAuth2Options> options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 触发校验；校验失败会抛 OptionsValidationException（fail-fast，符合启动期语义）。
        _ = _options.Value;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
