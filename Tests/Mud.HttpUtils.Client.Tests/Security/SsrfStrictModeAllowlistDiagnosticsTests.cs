// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Client.Tests;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// SSRF 严格模式 + 空白名单组合的启动期提示（fail-closed 默认值的可观测性兜底）：
/// ① 严格模式客户端 + 全局空白名单 ⇒ 一次性 Warning（含客户端名与三个登记入口）；
/// ② 任一口径登记白名单（运行期追加）/ 容器级自定义 <see cref="IUrlValidator"/> / 无非严格客户端 ⇒ 不提示。
/// </summary>
/// <remarks>
/// 提示组件按<b>容器单例</b>注册并由首个客户端创建驱动 —— 本组用例以独立容器验证
/// 「每宿主一次」语义，同时天然隔离进程级静态白名单状态在不同宿主间的串味。
/// </remarks>
public class SsrfStrictModeAllowlistDiagnosticsTests
{
    private const string GuidanceMarker = "SSRF 严格模式";

    /// <summary>容器级自定义验证器替身：白名单策略由宿主实现内自管。</summary>
    private sealed class HostManagedUrlValidator : IUrlValidator
    {
        public void ConfigureAllowedDomains(IEnumerable<string> domains) { }
        public void SetConfigurationDomains(IEnumerable<string> domains) { }
        public IReadOnlyCollection<string> GetAllowedDomains() => Array.Empty<string>();
        public void AddAllowedDomain(string domain) { }
        public void RemoveAllowedDomain(string domain) { }
        public void ValidateUrl(string? url, bool allowCustomBaseUrls = false) { }
        public ValueTask ValidateUrlAsync(string? url, bool allowCustomBaseUrls = false, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public void ValidateBaseUrl(string? baseUrl, bool allowCustomBaseUrls = false) { }
        public void ClearDnsCache() { }
    }

    private static (ServiceProvider Provider, CollectingLoggerProvider Logs) BuildHost(
        Action<IServiceCollection>? configure = null)
    {
        var logProvider = new CollectingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logProvider));
        configure?.Invoke(services);
        services.AddMudHttpClient("strict-a");
        services.AddMudHttpClient("strict-b");
        return (services.BuildServiceProvider(), logProvider);
    }

    private static List<string> GuidanceWarnings(CollectingLoggerProvider logs)
        => logs.GetLogRecords(LogLevel.Warning)
            .Select(r => r.Message)
            .Where(m => m.Contains(GuidanceMarker, StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void StrictClients_WithEmptyAllowlist_LogsWarningOnce_WithClientNames()
    {
        ClearAllowlist();
        try
        {
            var (sp, logs) = BuildHost();

            // 两个客户端创建均触发强制解析；单例语义保证只提示一次。
            _ = sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("strict-a");
            _ = sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("strict-b");

            var warnings = GuidanceWarnings(logs);
            warnings.Should().ContainSingle("每宿主只提示一次（组件为容器单例）");
            warnings[0].Should().Contain("strict-a");
            warnings[0].Should().Contain("strict-b");
            warnings[0].Should().Contain("MudHttpClients:AllowedDomains", "消息须给出配置节登记入口");
            warnings[0].Should().Contain("UrlValidator.AddAllowedDomain", "消息须给出运行期追加登记入口");
            warnings[0].Should().Contain("IUrlValidator", "消息须给出容器级实现登记入口");
        }
        finally
        {
            ClearAllowlist();
        }
    }

    [Fact]
    public void StrictClients_WithRuntimeAllowlist_NoWarning()
    {
        ClearAllowlist();
        UrlValidator.AddAllowedDomain("allowlisted.example.com");
        try
        {
            var (sp, logs) = BuildHost();

            _ = sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("strict-a");

            GuidanceWarnings(logs).Should().BeEmpty("已登记白名单 ⇒ 组合不成立");
        }
        finally
        {
            UrlValidator.RemoveAllowedDomain("allowlisted.example.com");
            ClearAllowlist();
        }
    }

    [Fact]
    public void StrictClients_WithHostManagedValidator_NoWarning()
    {
        ClearAllowlist();
        try
        {
            var (sp, logs) = BuildHost(services =>
                services.AddSingleton<IUrlValidator, HostManagedUrlValidator>());

            _ = sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("strict-a");

            GuidanceWarnings(logs).Should().BeEmpty("容器级自定义 IUrlValidator ⇒ 白名单策略由宿主自管，不代判");
        }
        finally
        {
            ClearAllowlist();
        }
    }

    [Fact]
    public void NonStrictClients_WithEmptyAllowlist_NoWarning()
    {
        ClearAllowlist();
        try
        {
            var (sp, logs) = BuildHost(services =>
                services.Configure<EnhancedHttpClientOptions>(o => o.AllowCustomBaseUrls = true));

            _ = sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("strict-a");

            GuidanceWarnings(logs).Should().BeEmpty("无非严格客户端 ⇒ 不存在被 fail-closed 拦下的请求面");
        }
        finally
        {
            ClearAllowlist();
        }
    }

    /// <summary>清空进程级静态白名单（配置桶 + 运行期桶整体替换为空），保证用例起点确定。</summary>
    private static void ClearAllowlist() => UrlValidator.ConfigureAllowedDomains(Array.Empty<string>());
}
