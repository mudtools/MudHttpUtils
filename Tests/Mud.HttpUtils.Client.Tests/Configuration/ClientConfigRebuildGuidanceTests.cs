// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests.Configuration;

/// <summary>
/// 配置热更新的注册期固化项提示：BaseAddress / TimeoutSeconds / DefaultHeaders 变更时
/// 逐客户端给出"仅失效缓存不能使其生效"的 Info 提示与两种生效方式；运行期生效项变更不提示。
/// </summary>
public class ClientConfigRebuildGuidanceTests
{
    private const string GuidanceMarker = "注册期固化项";

    private static (ServiceProvider Provider, CollectingLoggerProvider Logs, UpdatableMemoryProvider Config, IConfigurationRoot Root) BuildHost()
    {
        var updatable = new UpdatableMemoryProvider(new Dictionary<string, string?>
        {
            ["MudHttpClients:Clients:a:BaseAddress"] = "https://a.example.com",
            ["MudHttpClients:Clients:a:TimeoutSeconds"] = "30",
            ["MudHttpClients:Clients:a:AllowCustomBaseUrls"] = "true",
        });
        var config = new ConfigurationBuilder().Add(updatable).Build();
        var logProvider = new CollectingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logProvider));
        services.AddMudHttpClientsFromConfiguration(config);
        var provider = services.BuildServiceProvider();

        // 构造通知器：建立 IOptionsMonitor.OnChange 订阅（AddMudHttpClientInfrastructure 注册）。
        _ = provider.GetRequiredService<EnhancedHttpClientFactoryChangeNotifier>();
        return (provider, logProvider, updatable, config);
    }

    [Fact]
    public async Task RegistrationBoundItemChanged_LogsRebuildGuidanceWithAffectedItem()
    {
        var (provider, logs, updatable, root) = BuildHost();
        using (provider)
        {
            updatable.Set("MudHttpClients:Clients:a:TimeoutSeconds", "60");
            root.Reload();
            await Task.Delay(100);

            var guidance = logs.GetLogRecords(LogLevel.Information)
                .Where(r => r.Message.Contains(GuidanceMarker, StringComparison.Ordinal))
                .ToList();
            guidance.Should().ContainSingle("注册期固化项变更必须给出生效方式提示");
            guidance[0].Message.Should().Contain("TimeoutSeconds", "须列出受影响的固化项");
            guidance[0].Message.Should().Contain("WithBaseAddress", "须给出两种生效方式之一");
        }
    }

    [Fact]
    public async Task RuntimeEffectiveItemChanged_NoRebuildGuidance()
    {
        var (provider, logs, updatable, root) = BuildHost();
        using (provider)
        {
            // AllowCustomBaseUrls 是运行期生效项（缓存失效即可生效）—— 不得记重建提示。
            updatable.Set("MudHttpClients:Clients:a:AllowCustomBaseUrls", "false");
            root.Reload();
            await Task.Delay(100);

            logs.GetLogRecords(LogLevel.Information)
                .Should().NotContain(r => r.Message.Contains(GuidanceMarker, StringComparison.Ordinal),
                    "运行期生效项不需要重建提示（避免噪音）");
        }
    }
}
