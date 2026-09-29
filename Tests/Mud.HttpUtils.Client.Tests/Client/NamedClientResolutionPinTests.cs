// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯社会公共秩序等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests.Client;

using System.Text;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// G9-04：命名客户端解析胜者语义的运行期钉子（只测不改）。
/// </summary>
/// <remarks>
/// 实现类构造函数注入<b>类型级</b> <see cref="IEnhancedHttpClient"/>：多个命名客户端共存时
/// <c>RegisterNamedClient</c> 的 <c>TryAddTransient</c> <b>先注册者胜</b>，仅 <c>setAsDefault: true</c>
/// （<c>AddTransient</c> 后者覆盖）可改写默认解析。该语义此前只有文档与生成器侧 HTTPCLIENT033 Info，
/// 无运行期回归报警——运行库重构（如改 keyed）或文档漂移时本类即失败。
/// <para>
/// 观测手段（G9-04 方案 A）：在 <c>AddMudHttpClient</c> 返回的 <see cref="IHttpClientBuilder"/> 上
/// 追加 <c>ConfigurePrimaryHttpMessageHandler</c> 注入记录型 handler——工厂配置动作按注册顺序执行、
/// 后注册者胜出为 PrimaryHandler，请求经 EnhancedHttpClient 管道最终命中记录型 handler
/// （回 200 空响应，无真实网络），记录到的 clientName 即「该 IEnhancedHttpClient 实际绑定的命名客户端」。
/// </para>
/// </remarks>
public class NamedClientResolutionPinTests
{
    [Fact]
    public async Task TypeLevelEnhancedHttpClient_ResolvesToFirstRegisteredNamedClient()
    {
        var services = new ServiceCollection();
        var hits = new List<string>();

        // 本测试聚焦解析胜者语义（非 SSRF 契约）：开启 allowCustomBaseUrls 并固定 DNS 解析为公网 IP，
        // 绕过域名白名单 / 私有 IP 校验（.invalid 域在本机 DNS 下可能被劫持解析为私有地址）
        services.Configure<EnhancedHttpClientOptions>(o => o.AllowCustomBaseUrls = true);
        UrlValidator.DnsResolveOverride = _ => new[] { System.Net.IPAddress.Parse("93.184.216.34") };

        services.AddMudHttpClient("A_Client", c => c.BaseAddress = new Uri("https://a.invalid/"))
            .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler("A_Client", hits));
        services.AddMudHttpClient("B_Client", c => c.BaseAddress = new Uri("https://b.invalid/"))
            .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler("B_Client", hits));

        try
        {
            await using var provider = services.BuildServiceProvider();
            var client = provider.GetRequiredService<IEnhancedHttpClient>();
            await client.SendAsync<object?>(new HttpRequestMessage(HttpMethod.Get, "https://a.invalid/ping"));
        }
        finally
        {
            UrlValidator.DnsResolveOverride = null;
            UrlValidator.ClearDnsCache();
        }

        hits.Should().ContainSingle("实现类注入的类型级 IEnhancedHttpClient 按 TryAddTransient 先注册者胜解析")
            .Which.Should().Be("A_Client",
                "HTTPCLIENT033 / README『生成客户端命名（G7-04a）』文档化的胜者语义：仅第一个命名客户端生效");
    }

    [Fact]
    public async Task SetAsDefault_LaterRegistration_OverridesFirstWins()
    {
        var services = new ServiceCollection();
        var hits = new List<string>();

        // 本测试聚焦解析胜者语义（非 SSRF 契约）：开启 allowCustomBaseUrls 并固定 DNS 解析为公网 IP
        services.Configure<EnhancedHttpClientOptions>(o => o.AllowCustomBaseUrls = true);
        UrlValidator.DnsResolveOverride = _ => new[] { System.Net.IPAddress.Parse("93.184.216.34") };

        services.AddMudHttpClient("A_Client", c => c.BaseAddress = new Uri("https://a.invalid/"))
            .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler("A_Client", hits));
        services.AddMudHttpClient("B_Client", c => c.BaseAddress = new Uri("https://b.invalid/"), setAsDefault: true)
            .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler("B_Client", hits));

        try
        {
            await using var provider = services.BuildServiceProvider();
            var client = provider.GetRequiredService<IEnhancedHttpClient>();
            await client.SendAsync<object?>(new HttpRequestMessage(HttpMethod.Get, "https://b.invalid/ping"));
        }
        finally
        {
            UrlValidator.DnsResolveOverride = null;
            UrlValidator.ClearDnsCache();
        }

        hits.Should().ContainSingle("setAsDefault: true 走 AddTransient，后注册覆盖先注册")
            .Which.Should().Be("B_Client", "setAsDefault 是唯一能改写类型级默认解析的手段");
    }

    /// <summary>记录型主链路终端 handler：命中即记录 clientName 并回 200 空响应（无真实网络）。</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _clientName;
        private readonly List<string> _hits;

        public RecordingHandler(string clientName, List<string> hits)
        {
            _clientName = clientName;
            _hits = hits;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _hits.Add(_clientName);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
