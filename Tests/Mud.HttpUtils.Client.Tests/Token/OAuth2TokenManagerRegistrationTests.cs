// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P2-01：OAuth2 安全默认接线 —— 端点收紧为 opt-in（默认不打断内部 IdP）、
/// 默认禁用自动重定向、且令牌端点客户端<b>不</b>挂载恢复处理器（架构不变式 I2）。
/// </summary>
public class OAuth2EndpointValidatorRestrictionTests
{
    [Theory]
    [InlineData("https://10.0.0.5/oauth2/token")]
    [InlineData("https://10.255.255.254/oauth2/token")]
    [InlineData("https://172.16.0.1/oauth2/token")]
    [InlineData("https://172.31.255.255/oauth2/token")]
    [InlineData("https://192.168.1.1/oauth2/token")]
    [InlineData("https://127.0.0.1/oauth2/token")]
    [InlineData("https://0.0.0.0/oauth2/token")]
    [InlineData("https://169.254.169.254/latest/meta-data")]      // 云元数据
    [InlineData("https://100.64.0.1/oauth2/token")]                // CGNAT
    [InlineData("https://100.127.255.255/oauth2/token")]
    [InlineData("https://[::1]/oauth2/token")]                     // IPv6 回环（带方括号）
    [InlineData("https://[fc00::1]/oauth2/token")]                 // IPv6 唯一本地
    [InlineData("https://[fd12:3456::1]/oauth2/token")]
    [InlineData("https://[fe80::1]/oauth2/token")]                 // IPv6 链路本地
    [InlineData("https://[::ffff:10.0.0.1]/oauth2/token")]         // IPv4 映射型 IPv6（必须还原后判定）
    public void IsSecure_ShouldRejectPrivateAndMetadataHosts_WhenRestricted(string endpoint)
    {
        OAuth2EndpointValidator.IsSecure(endpoint, restrictToPublicEndpoints: true)
            .Should().BeFalse($"{endpoint} 属私网 / 元数据 / CGNAT 字面量地址");
    }

    [Theory]
    [InlineData("https://idp.example.com/oauth2/token")]
    [InlineData("https://8.8.8.8/oauth2/token")]
    [InlineData("https://[2606:4700::1111]/oauth2/token")]
    [InlineData("https://172.32.0.1/oauth2/token")]                // 172.32/16 已不在 172.16/12 内
    [InlineData("https://100.128.0.1/oauth2/token")]               // 100.128/16 已不在 CGNAT 内
    public void IsSecure_ShouldPermitPublicHosts_WhenRestricted(string endpoint)
    {
        OAuth2EndpointValidator.IsSecure(endpoint, restrictToPublicEndpoints: true)
            .Should().BeTrue($"{endpoint} 属公网地址");
    }

    /// <summary>守护"默认不打断内部 IdP"（评审修订 7 的核心取舍）。</summary>
    [Theory]
    [InlineData("https://idp.internal/oauth2/token")]
    [InlineData("https://10.0.0.5/oauth2/token")]
    [InlineData("https://192.168.1.1/oauth2/token")]
    [InlineData("https://169.254.169.254/oauth2/token")]
    [InlineData("https://[::1]/oauth2/token")]
    public void IsSecure_DefaultShouldPermitPrivateHosts(string endpoint)
    {
        OAuth2EndpointValidator.IsSecure(endpoint)
            .Should().BeTrue($"{endpoint}：默认（不收紧）下与 2.0.x 行为逐字节一致");

        OAuth2EndpointValidator.IsSecure(endpoint, restrictToPublicEndpoints: false)
            .Should().BeTrue();
    }

    [Fact]
    public void IsSecure_ShouldStillRejectMalformedOrInsecureSchemes()
    {
        OAuth2EndpointValidator.IsSecure("https://").Should().BeFalse("畸形字符串（Uri.TryCreate 只能拒绝真正非法的形态）");
        OAuth2EndpointValidator.IsSecure("not-a-url").Should().BeFalse("非绝对 URI");
        OAuth2EndpointValidator.IsSecure("ftp://idp.example.com/token").Should().BeFalse("非 http(s) 方案");
        OAuth2EndpointValidator.IsSecure("http://idp.example.com/token").Should().BeFalse("远程明文 HTTP");
        OAuth2EndpointValidator.IsSecure("http://localhost:5000/token").Should().BeTrue("回环 HTTP 豁免（开发环境）");
        OAuth2EndpointValidator.IsSecure(null).Should().BeFalse();
        OAuth2EndpointValidator.IsSecure(string.Empty).Should().BeFalse();
    }

    /// <summary>
    /// 实施期断言更正：单标签主机名（<c>https://foo</c>）**通过**校验 ——
    /// <see cref="Uri.IsWellFormedOriginalString"/> 校验的是转义/格式合法性而非主机可达性或公网性，
    /// 且 <c>foo</c> 也可能由内网 DNS 解析（合法内网部署形态）。
    /// 原方案"<c>Uri.TryCreate</c> 会拒绝 <c>https://foo</c>"的表述不准确，此处以事实固定语义。
    /// </summary>
    [Fact]
    public void IsSecure_ShouldAcceptSingleLabelHost()
    {
        OAuth2EndpointValidator.IsSecure("https://foo").Should().BeTrue();
        OAuth2EndpointValidator.IsSecure("https://foo", restrictToPublicEndpoints: true)
            .Should().BeTrue("非 IP 字面量交由连接期 IP 准入兜底，字符串层不误拒内网单标签域名");
    }
}

/// <summary>
/// R-P2-01：<c>AddMudHttpOAuth2TokenManager</c> 的接线断言（重定向禁用、I2 边界、opt-in 收紧）。
/// </summary>
public class OAuth2TokenManagerRegistrationTests
{
    private const string PublicTokenEndpoint = "https://idp.example.com/oauth2/token";

    /// <summary>
    /// 构造容器。<paramref name="configure"/> 先于必填字段兜底执行，故可覆写 TokenEndpoint / ClientId。
    /// </summary>
    private static ServiceProvider BuildProvider(Action<OAuth2Options>? configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpOAuth2TokenManager(o =>
        {
            // 校验器要求 ClientId 非空、TokenEndpoint 安全 —— 先给最小合法基线，再交给用例覆写。
            o.ClientId = "test-client";
            o.TokenEndpoint = PublicTokenEndpoint;
            configure?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    /// <summary>取命名客户端链路的<b>主处理器</b>（最深处的非 DelegatingHandler）。</summary>
    private static HttpMessageHandler ResolvePrimaryHandler(IServiceProvider provider)
    {
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(StandardOAuth2TokenManager.HttpClientName);

        var current = handler;
        while (current is DelegatingHandler delegating && delegating.InnerHandler != null)
        {
            current = delegating.InnerHandler;
        }
        return current;
    }

    [Fact]
    public void OAuth2HttpClient_ShouldNotFollowRedirects_ByDefault()
    {
        using var provider = BuildProvider(o => o.TokenEndpoint = PublicTokenEndpoint);

        var primary = ResolvePrimaryHandler(provider);

        // 默认（不收紧）为 HttpClientHandler；关键是 AllowAutoRedirect == false
        // —— client_secret 位于 Basic 头，随 30x 外发即凭据泄漏。
        primary.Should().BeOfType<HttpClientHandler>();
        ((HttpClientHandler)primary).AllowAutoRedirect.Should().BeFalse("默认即禁用自动重定向");
    }

    [Fact]
    public void OAuth2HttpClient_ShouldNotUseSsrfSafeHandler_ByDefault()
    {
        using var provider = BuildProvider(o => o.TokenEndpoint = "https://10.0.0.5/oauth2/token");

        var primary = ResolvePrimaryHandler(provider);

        // 评审修订 7：默认**不**安装 SsrfSafeSocketsHttpHandler
        // —— 其 DefaultIpAddressPolicy 为 fail-closed，默认安装会直接打断内网 IdP。
        primary.Should().BeOfType<HttpClientHandler>(
            "默认必须放行内网 IdP（否则属高概率破坏性变更）");
    }

#if NET6_0_OR_GREATER
    [Fact]
    public void OAuth2HttpClient_WhenRestricted_ShouldUseSsrfSafeHandler()
    {
        using var provider = BuildProvider(o =>
        {
            o.TokenEndpoint = PublicTokenEndpoint;
            o.RestrictOAuth2EndpointsToPublicAddresses = true;
        });

        var primary = ResolvePrimaryHandler(provider);

        primary.Should().BeOfType<SsrfSafeSocketsHttpHandler>(
            "显式收紧时安装连接期 IP 准入（阻断 DNS 重绑定）");
    }
#endif

    [Fact]
    public void OAuth2HttpClient_ShouldNotCarryTokenRecoveryHandler()
    {
        using var provider = BuildProvider(o => o.TokenEndpoint = PublicTokenEndpoint);

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(StandardOAuth2TokenManager.HttpClientName);

        var chain = new List<Type>();
        var current = handler;
        while (current != null)
        {
            chain.Add(current.GetType());
            current = (current as DelegatingHandler)?.InnerHandler;
        }

        chain.Should().NotContain(typeof(TokenRecoveryDelegatingHandler),
            "架构不变式 I2：令牌端点客户端不得挂载恢复处理器（否则刷新链路重入自身）");
        chain.Should().NotContain(typeof(TokenRecoveryEnhancedClient));
    }

    [Fact]
    public void AddMudHttpOAuth2TokenManager_ShouldRegisterManagerAndAliasesAsSameInstance()
    {
        using var provider = BuildProvider(o => o.TokenEndpoint = PublicTokenEndpoint);

        var manager = provider.GetRequiredService<StandardOAuth2TokenManager>();
        provider.GetRequiredService<ITokenManager>().Should().BeSameAs(manager,
            "ITokenManager 必须转发到同一实例（避免缓存/单飞锁分裂）");
    }

    [Fact]
    public void AddMudHttpOAuth2TokenManager_ShouldReuseExistingOptionsPipeline()
    {
        using var provider = BuildProvider(o => o.TokenEndpoint = PublicTokenEndpoint);

        // 端点校验器仍生效（未重复实现、未绕开既有接线）
        var options = provider.GetRequiredService<IOptions<OAuth2Options>>().Value;
        options.TokenEndpoint.Should().Be(PublicTokenEndpoint);
    }

    [Fact]
    public async Task StartupValidator_ShouldFailFast_WhenTokenEndpointMissing()
    {
        // 刻意绕开 BuildProvider 的合法基线：只给 ClientId，TokenEndpoint 缺失 ⇒ 校验失败。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMudHttpOAuth2TokenManager(o => o.ClientId = "test-client");

        using var provider = services.BuildServiceProvider();

        var validator = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<OAuth2OptionsStartupValidator>()
            .Single();

        var act = () => validator.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<OptionsValidationException>(
            "启动期必须 fail-fast，而非推迟到首次取令牌");
    }

    [Fact]
    public async Task StartupValidator_ShouldSucceed_WithValidOptions()
    {
        using var provider = BuildProvider(null);

        var validator = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<OAuth2OptionsStartupValidator>()
            .Single();

        await validator.StartAsync(CancellationToken.None);
    }
}
