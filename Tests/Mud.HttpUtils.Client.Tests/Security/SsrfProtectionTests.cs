using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mud.HttpUtils.Client.Tests;

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

    /// <summary>
    /// P3（M6 阶段五）：拒连异常消息只回显主机名，不回显 DNS 解析出的候选 IP 列表 ——
    /// 该消息会进入异常 / 日志链路，披露内网解析结果等于泄漏内网拓扑。
    /// </summary>
    [Fact]
    public async Task Handler_DefaultPolicy_RejectMessage_ShouldNotDiscloseResolvedAddresses()
    {
        using var handler = new SsrfSafeSocketsHttpHandler(new DefaultIpAddressPolicy());
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

        var act = () => client.GetAsync("http://localhost:9/");

        var ex = await FluentActions.Awaiting(act).Should().ThrowAsync<Exception>();
        var message = ex.Which.ToString();
        message.Should().Contain("不允许连接到目标地址: localhost", "主机名仍需保留以支撑排障");
        message.Should().NotContain("127.0.0.1");
        message.Should().NotContain("::1");
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

    #region M4-C/H-6 连接期校验启用引导（一次性 Info 日志）

    /// <summary>
    /// 已注册 <see cref="IIpAddressPolicy"/>（AddMudHttpClientSsrfProtection(IServiceCollection)）
    /// 且客户端为<b>非严格模式</b>（<c>AllowCustomBaseUrls=true</c>，未走 M6-HC-03 自动接线）时，
    /// 首次创建客户端记录一次 Info 引导日志（进程级 Interlocked 门控，多次创建仅一条）。
    /// </summary>
    [Fact]
    public void SsrfGuidance_RegisteredPolicy_NonStrictMode_LogsOnce()
    {
        var gate = typeof(HttpClientServiceCollectionExtensions)
            .GetField("_ssrfGuidanceLogged", BindingFlags.Static | BindingFlags.NonPublic)!;
        gate.SetValue(null, 0);

        var logger = new CapturingLogger<HttpClientFactoryEnhancedClient>();
        try
        {
            var services = new ServiceCollection();
            services.AddMudHttpClientSsrfProtection();
            services.AddSingleton<ILogger<HttpClientFactoryEnhancedClient>>(logger);
            // M6-HC-03（D1-A）：严格模式（默认）已由注册路径自动接线连接期校验 → 不再提示；
            // 引导日志仅对非严格模式客户端保留，故此处经配置节显式放开自定义 BaseUrl。
            services.Configure<MudHttpClientApplicationOptions>(o =>
                o.Clients["no-handler-protection"] = new MudHttpClientOptions { AllowCustomBaseUrls = true });
            services.AddMudHttpClient("no-handler-protection",
                client => client.BaseAddress = new Uri("https://api.example.com"));

            using var sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<IEnhancedHttpClientFactory>();

            _ = factory.CreateClient("no-handler-protection");
            _ = factory.CreateClient("no-handler-protection");
            _ = factory.CreateClient("no-handler-protection");

            // 多次创建仅触发一次引导日志
            logger.Messages.Should().ContainSingle(m => m.Contains("连接期校验"));
        }
        finally
        {
            gate.SetValue(null, 0);
        }
    }

    /// <summary>
    /// M6-HC-03（D1-A）：严格模式（<c>AllowCustomBaseUrls=false</c>，默认）下连接期 SSRF 校验
    /// 已由注册路径自动接线，不再输出引导日志 —— 避免误导用户以为未启用。
    /// </summary>
    [Fact]
    public void SsrfGuidance_StrictMode_AutoWired_DoesNotLog()
    {
        var gate = typeof(HttpClientServiceCollectionExtensions)
            .GetField("_ssrfGuidanceLogged", BindingFlags.Static | BindingFlags.NonPublic)!;
        gate.SetValue(null, 0);

        var logger = new CapturingLogger<HttpClientFactoryEnhancedClient>();
        try
        {
            var services = new ServiceCollection();
            services.AddMudHttpClientSsrfProtection();
            services.AddSingleton<ILogger<HttpClientFactoryEnhancedClient>>(logger);
            services.AddMudHttpClient("strict-auto-wired",
                client => client.BaseAddress = new Uri("https://api.example.com"));

            using var sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<IEnhancedHttpClientFactory>();

            _ = factory.CreateClient("strict-auto-wired");
            _ = factory.CreateClient("strict-auto-wired");

            logger.Messages.Should().NotContain(m => m.Contains("连接期校验"));
        }
        finally
        {
            gate.SetValue(null, 0);
        }
    }

    /// <summary>未注册策略 → 不记录引导日志。</summary>
    [Fact]
    public void SsrfGuidance_NoPolicyRegistered_DoesNotLog()
    {
        var gate = typeof(HttpClientServiceCollectionExtensions)
            .GetField("_ssrfGuidanceLogged", BindingFlags.Static | BindingFlags.NonPublic)!;
        gate.SetValue(null, 0);

        var logger = new CapturingLogger<HttpClientFactoryEnhancedClient>();
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILogger<HttpClientFactoryEnhancedClient>>(logger);
            services.AddMudHttpClient("no-policy",
                client => client.BaseAddress = new Uri("https://api.example.com"));

            using var sp = services.BuildServiceProvider();
            _ = sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("no-policy");

            logger.Messages.Should().NotContain(m => m.Contains("连接期校验"));
        }
        finally
        {
            gate.SetValue(null, 0);
        }
    }

    #endregion

    #region M6-HC-01 IPv4 映射地址

    /// <summary>
    /// M6-HC-01：IPv4 映射型 IPv6（::ffff:a.b.c.d）必须先归一为 IPv4 再判定。
    /// 修复前 ::ffff:10.0.0.1 不命中 IPv4 网段（IPNetwork.Contains 按 AddressFamily 短路），
    /// 两道 SSRF 防线（URL 校验期 + 连接期 DefaultIpAddressPolicy）同时被绕过。
    /// </summary>
    [Theory]
    [InlineData("::ffff:10.0.0.1")]      // 私网 10/8
    [InlineData("::ffff:172.16.0.1")]    // 私网 172.16/12
    [InlineData("::ffff:192.168.1.1")]   // 私网 192.168/16
    [InlineData("::ffff:169.254.1.1")]   // 链路本地 169.254/16（云元数据段）
    [InlineData("::ffff:0.0.0.0")]       // 0.0.0.0/8
    [InlineData("::ffff:127.0.0.1")]     // 回环：IPAddress.IsLoopback 对映射地址返回 False，修复后经归一应 true
    public void IsPrivateIpAddress_Ipv4MappedPrivateAddress_ReturnsTrue(string addressText)
    {
        var address = IPAddress.Parse(addressText);

        UrlValidator.IsPrivateIpAddress(address).Should().BeTrue(
            $"IPv4 映射地址 {addressText} 归一后属私网/回环，必须判为 private（SSRF 防线不得绕过）");
    }

    /// <summary>非映射型公网/文档地址不得误伤。</summary>
    [Theory]
    [InlineData("::ffff:8.8.8.8")]   // 映射型公网地址
    [InlineData("2001:db8::1")]      // 纯 IPv6 文档地址
    public void IsPrivateIpAddress_NonPrivateAddress_ReturnsFalse(string addressText)
    {
        var address = IPAddress.Parse(addressText);

        UrlValidator.IsPrivateIpAddress(address).Should().BeFalse(
            $"地址 {addressText} 不属私网/回环，不应被判为 private");
    }

    /// <summary>连接期准入策略（DefaultIpAddressPolicy 直接委托 IsPrivateIpAddress）自动受益于归一化。</summary>
    [Fact]
    public void DefaultIpAddressPolicy_Ipv4MappedAddress_BlocksPrivate_AllowsPublic()
    {
        var policy = new DefaultIpAddressPolicy();

        policy.IsAllowed(IPAddress.Parse("::ffff:10.0.0.1")).Should().BeFalse("映射型私网地址必须在连接期被拒绝");
        policy.IsAllowed(IPAddress.Parse("::ffff:8.8.8.8")).Should().BeTrue("映射型公网地址不应被误拒");
    }

    #endregion
}
