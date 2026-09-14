// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// 订阅 <see cref="MudHttpClientApplicationOptions"/> 变更并将 <see cref="MudHttpClientApplicationOptions.AllowedDomains"/>
/// 重放到 <see cref="UrlValidator"/> 静态白名单（CFG-08，幂等）。
/// </summary>
/// <remarks>
/// <para>
/// 注册期一次性应用无法响应 <c>appsettings.json</c> 变更（<c>section.Bind</c> 得到的是局部快照）；
/// 本组件在构造时立即应用一次（幂等），随后订阅 <see cref="IOptionsMonitor{TOptions}.OnChange"/> 重放。
/// </para>
/// <para>
/// 生命周期：单例，随容器释放取消订阅。
/// </para>
/// <para>
/// 上位方案：<c>03-P2</c> Roadmap R-3（引入 <c>IUrlValidator</c> 实例 + DI 注入，去除静态可变状态）落地后，
/// 删除本组件。
/// </para>
/// </remarks>
internal sealed class AllowedDomainsReloader : IDisposable
{
    private readonly ILogger<AllowedDomainsReloader> _logger;
    private readonly IDisposable? _subscription;

    public AllowedDomainsReloader(
        IOptionsMonitor<MudHttpClientApplicationOptions> monitor,
        ILogger<AllowedDomainsReloader>? logger = null)
    {
        _logger = logger ?? NullLogger<AllowedDomainsReloader>.Instance;

        // 首次立即应用（幂等：与注册期的一次性应用结果一致）
        Apply(monitor.CurrentValue);
        // 变更重放
        _subscription = monitor.OnChange(Apply);
    }

    private void Apply(MudHttpClientApplicationOptions? options)
    {
        var domains = options?.AllowedDomains ?? new List<string>();

        // CFG-34：只替换「配置来源」桶，保留运行期经 UrlValidator.AddAllowedDomain 新增的域名；
        // 若改用 ConfigureAllowedDomains（整体替换两桶），运行期增量会在每次 Reload 后消失（不变量 I-13）。
        UrlValidator.SetConfigurationDomains(domains);
        MudHttpClientLog.AllowedDomainsApplied(_logger, domains.Count);
    }

    /// <inheritdoc />
    public void Dispose() => _subscription?.Dispose();
}
