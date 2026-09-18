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
}
