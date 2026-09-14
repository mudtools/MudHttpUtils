using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// M2-#8 SSRF 防护回归：DNS 缓存 TTL/单飞/清空 + 连接期 IP 准入（SsrfSafeSocketsHttpHandler）。
/// </summary>
public class SsrfProtectionTests
{
    #region M2-#8.1 DNS 缓存（TTL / 单飞 / 清空）

    [Fact]
    public void DnsCache_ConcurrentValidateUrl_ResolveOncePerHost()
    {
        var counter = 0;
        UrlValidator.DnsResolveOverride = _ => { Interlocked.Increment(ref counter); return [IPAddress.Parse("93.184.216.34")]; };
        try
        {
            UrlValidator.ClearDnsCache();

            // 10 个新域名 × 并发 100 次 —— 同 key 经 stripe 锁 + 双检只解析一次
            Parallel.For(0, 1000, i =>
                UrlValidator.ValidateUrl($"http://dns-conc-{i % 10}.example.com", allowCustomBaseUrls: true));

            counter.Should().Be(10);
        }
        finally
        {
            UrlValidator.DnsResolveOverride = null;
            UrlValidator.ClearDnsCache();
        }
    }

    [Fact]
    public void DnsCache_TtlExpired_ResolveAgain()
    {
        var counter = 0;
        UrlValidator.DnsResolveOverride = _ => { Interlocked.Increment(ref counter); return [IPAddress.Parse("93.184.216.34")]; };
        UrlValidator.DnsCacheTtl = TimeSpan.FromMilliseconds(50);
        try
        {
            UrlValidator.ClearDnsCache();
            UrlValidator.ValidateUrl("http://dns-ttl.example.com", allowCustomBaseUrls: true);
            counter.Should().Be(1);

            Thread.Sleep(150);
            UrlValidator.ValidateUrl("http://dns-ttl.example.com", allowCustomBaseUrls: true);
            counter.Should().Be(2);  // TTL 过期后重新解析（缓存未永生）
        }
        finally
        {
            UrlValidator.DnsResolveOverride = null;
            UrlValidator.DnsCacheTtl = TimeSpan.FromMinutes(5);
            UrlValidator.ClearDnsCache();
        }
    }

    [Fact]
    public void ClearDnsCache_ForcesNextResolution()
    {
        var counter = 0;
        UrlValidator.DnsResolveOverride = _ => { Interlocked.Increment(ref counter); return [IPAddress.Parse("93.184.216.34")]; };
        try
        {
            UrlValidator.ClearDnsCache();
            UrlValidator.ValidateUrl("http://dns-clear.example.com", allowCustomBaseUrls: true);
            UrlValidator.ValidateUrl("http://dns-clear.example.com", allowCustomBaseUrls: true);
            counter.Should().Be(1);  // 命中缓存

            UrlValidator.ClearDnsCache();
            UrlValidator.ValidateUrl("http://dns-clear.example.com", allowCustomBaseUrls: true);
            counter.Should().Be(2);  // 清空后重新解析
        }
        finally
        {
            UrlValidator.DnsResolveOverride = null;
            UrlValidator.ClearDnsCache();
        }
    }

    #endregion

    #region M2-#8.2 连接期校验（SsrfSafeSocketsHttpHandler）

    public static IEnumerable<object[]> SsrfForbiddenHosts
        => new[] { "127.0.0.1", "169.254.169.254", "10.1.2.3", "192.168.1.1" }.Select(h => new object[] { h });

    /// <summary>默认策略（fail-closed）：回环 / 云元数据 / 私网地址在<b>建连前</b>被拒。</summary>
    [Theory]
    [MemberData(nameof(SsrfForbiddenHosts))]
    public async Task Handler_DefaultPolicy_RejectsPrivateAddresses(string host)
    {
        // 端口 9（discard）无监听 —— 若未被策略拒绝，将表现为连接失败而非"不允许连接"，
        // 因此断言异常消息含策略拒绝文案即可证明拒绝发生在建连前。
        using var handler = new SsrfSafeSocketsHttpHandler(new DefaultIpAddressPolicy());
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        var act = () => client.GetAsync($"http://{host}:9/");

        var ex = await FluentActions.Awaiting(act).Should().ThrowAsync<Exception>();
        ex.Which.ToString().Should().Contain("不允许连接到目标地址");
    }

    /// <summary>按策略放行回环 → 真实建连成功并拿到响应（连接期校验不误伤合法目标）。</summary>
    [Fact]
    public async Task Handler_CustomPolicy_AllowsLoopback_EndToEnd()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serverTask = Task.Run(async () =>
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        var stream = client.GetStream();
                        // 先读完请求头再响应 —— 未读数据就关闭会发 RST，客户端表现为"连接被中止"
                        var buffer = new byte[4096];
                        var total = 0;
                        while (total < buffer.Length)
                        {
                            var n = await stream.ReadAsync(buffer.AsMemory(total));
                            if (n == 0)
                                return;
                            total += n;
                            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n"))
                                break;
                        }

                        var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
                        await stream.WriteAsync(response);
                        await stream.FlushAsync();
                    }
                });
            }
        });

        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var handler = new SsrfSafeSocketsHttpHandler(new LoopbackOnlyPolicy());
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            var body = await client.GetStringAsync($"http://127.0.0.1:{port}/");

            body.Should().Be("ok");
        }
        finally
        {
            listener.Stop();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(1)).ContinueWith(_ => { });
        }
    }

    /// <summary>DI 集成：AddMudHttpClientSsrfProtection 注册策略 + 启用连接期校验。</summary>
    [Fact]
    public async Task DiRegistration_SsrfProtection_RejectsPrivateAddress()
    {
        var services = new ServiceCollection();
        services.AddMudHttpClientSsrfProtection();
        services.AddHttpClient("ssrf-test").AddMudHttpClientSsrfProtection();
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ssrf-test");

        var act = () => client.GetAsync("http://127.0.0.1:9/");

        var ex = await FluentActions.Awaiting(act).Should().ThrowAsync<Exception>();
        ex.Which.ToString().Should().Contain("不允许连接到目标地址");
    }

    private sealed class LoopbackOnlyPolicy : IIpAddressPolicy
    {
        public bool IsAllowed(IPAddress address) => IPAddress.IsLoopback(address);
    }

    #endregion
}
