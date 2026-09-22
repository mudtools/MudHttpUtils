// -----------------------------------------------------------------------
//  M5-HC-07：ValidateUrlAsync 异步 DNS 测试
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Net;
using Mud.HttpUtils.Tests;

namespace Mud.HttpUtils.Client.Tests;

public class UrlValidatorAsyncTests : IDisposable
{
    private readonly UrlValidatorFixture _fixture = new();

    public void Dispose()
    {
        UrlValidator.DnsResolveOverride = null;
        UrlValidator.ClearDnsCache();
        _fixture.RestoreDomains();
    }

    [Fact]
    public async Task ValidateUrlAsync_WhitelistedDomain_Succeeds_WithoutDns()
    {
        var dnsCalls = 0;
        UrlValidator.DnsResolveOverride = _ =>
        {
            Interlocked.Increment(ref dnsCalls);
            return new[] { IPAddress.Parse("93.184.216.34") };
        };

        // api.example.com 在白名单中 → 直接放行，零 DNS
        await UrlValidator.ValidateUrlAsync("https://api.example.com/data");

        dnsCalls.Should().Be(0, "白名单域名不应触发 DNS 解析");
    }

    [Fact]
    public async Task ValidateUrlAsync_StrictMode_RejectsNonWhitelist_WithoutDns()
    {
        var dnsCalls = 0;
        UrlValidator.DnsResolveOverride = _ =>
        {
            Interlocked.Increment(ref dnsCalls);
            return new[] { IPAddress.Parse("93.184.216.34") };
        };

        var act = async () => await UrlValidator.ValidateUrlAsync("https://evil.example.org/x", allowCustomBaseUrls: false);
        await act.Should().ThrowAsync<InvalidOperationException>();

        dnsCalls.Should().Be(0, "严格模式非白名单应在 DNS 前拒绝");
    }

    [Fact]
    public async Task ValidateUrlAsync_CustomMode_PrivateIp_Throws()
    {
        UrlValidator.DnsResolveOverride = _ => new[] { IPAddress.Parse("10.0.0.1") };

        var act = async () => await UrlValidator.ValidateUrlAsync("http://internal.corp.example/x", allowCustomBaseUrls: true);
        // 内网后缀 .corp 会先被 IsInternalDomain 拦截；若不匹配则走私有 IP
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ValidateUrlAsync_CustomMode_PublicIp_Succeeds()
    {
        UrlValidator.DnsResolveOverride = _ => new[] { IPAddress.Parse("93.184.216.34") };

        await UrlValidator.ValidateUrlAsync("http://public-api.test/x", allowCustomBaseUrls: true);
    }

    [Fact]
    public async Task ValidateUrlAsync_InvalidUrl_ThrowsArgumentException()
    {
        var act = async () => await UrlValidator.ValidateUrlAsync("not-a-url");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ValidateUrlAsync_Null_ThrowsArgumentNullException()
    {
        var act = async () => await UrlValidator.ValidateUrlAsync(null);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #region M6-HC-08：白名单命中的传输安全校验改走异步回环判定

    [Fact]
    public async Task ValidateUrlAsync_WhitelistedDomain_HttpNonLoopback_ThrowsInsecureProtocol()
    {
        // HC-08：白名单 + HTTP → 异步回环判定（DNS 解析主机名）→ 非回环一律拒绝
        UrlValidator.ClearDnsCache();
        UrlValidator.DnsResolveOverride = _ => new[] { IPAddress.Parse("93.184.216.34") };

        var act = async () => await UrlValidator.ValidateUrlAsync("http://api.example.com/data");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*仅允许 HTTPS*");
    }

    [Fact]
    public async Task ValidateUrlAsync_WhitelistedDomain_HttpLoopback_Allowed()
    {
        // HC-08：回环地址豁免（保留本地开发）—— 异步路径与同步版语义一致
        UrlValidator.ClearDnsCache();
        UrlValidator.DnsResolveOverride = _ => new[] { IPAddress.Parse("127.0.0.1") };

        var act = async () => await UrlValidator.ValidateUrlAsync("http://api.example.com/data");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateUrlAsync_WhitelistedDomain_Https_SkipsLoopbackCheck()
    {
        // HTTPS 白名单命中直接放行，不触发任何 DNS 解析（异步版保持零 DNS）
        var dnsCalls = 0;
        UrlValidator.DnsResolveOverride = _ =>
        {
            Interlocked.Increment(ref dnsCalls);
            return new[] { IPAddress.Parse("93.184.216.34") };
        };

        await UrlValidator.ValidateUrlAsync("https://api.example.com/data");

        dnsCalls.Should().Be(0, "HTTPS 白名单命中无需回环判定");
    }

    #endregion
}
