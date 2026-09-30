// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Mud.HttpUtils.Client.Tests.Infrastructure;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P2-05（C3）：<see cref="ClientSecretCache"/> 的密钥驻留清除，以及
/// <see cref="StandardOAuth2TokenManager"/> 的 Dispose 链完整性。
/// </summary>
/// <remarks>
/// 该区域历史上出过三次回归（TK-08 / NEW-TM-11 / SR-H1），故固定三条契约：
/// ① 释放后 client_secret 明文引用被解除；② 基类资源（定时器 / 令牌缓存 / 锁表）仍被释放；
/// ③ 重复 Dispose 安全（幂等）。
/// </remarks>
public class TokenManagerDisposeChainTests
{
    private const string TokenJson = "{\"access_token\":\"test-token\",\"expires_in\":3600,\"token_type\":\"Bearer\"}";

    private static StandardOAuth2TokenManager CreateManager(
        HttpClient httpClient,
        Action<OAuth2Options>? configureOptions = null,
        ISecretProvider? secretProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        var options = new OAuth2Options
        {
            TokenEndpoint = "https://auth.example.com/token",
            ClientId = "test-client",
            ClientSecret = "fallback-secret",
        };
        configureOptions?.Invoke(options);

        return new StandardOAuth2TokenManager(
            httpClient,
            Options.Create(options),
            loggerFactory?.CreateLogger<StandardOAuth2TokenManager>(),
            secretProvider);
    }

    private static HttpClient CreateHttpClientReturningToken()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TokenJson),
            });
        return new HttpClient(handler.Object);
    }

    [Fact]
    public async Task Dispose_ShouldClearClientSecretPlaintext_AndExpandBaseCleanup()
    {
        var secretProvider = new Mock<ISecretProvider>();
        secretProvider.Setup(p => p.GetSecretAsync("cid")).ReturnsAsync("provider-secret-value");

        using var httpClient = CreateHttpClientReturningToken();
        var manager = CreateManager(
            httpClient,
            o => o.ClientSecretProviderName = "cid",
            secretProvider.Object);

        await manager.GetTokenByClientCredentialsAsync(cancellationToken: CancellationToken.None);

        manager.ClientSecretCacheHasValueForTest.Should().BeTrue("密钥解析后被 TTL 缓存");
        manager.TimersActive.Should().BeTrue("释放前维护定时器在运行");

        manager.Dispose();

        manager.ClientSecretCacheHasValueForTest.Should().BeFalse("释放后不得再持有 client_secret 明文引用");
        manager.TimersActive.Should().BeFalse("基类定时器必须仍被释放（Dispose 链未被派生类打断）");
    }

    [Fact]
    public async Task Dispose_ShouldBeIdempotent()
    {
        using var httpClient = CreateHttpClientReturningToken();
        var manager = CreateManager(httpClient);

        await manager.GetTokenByClientCredentialsAsync(cancellationToken: CancellationToken.None);

        manager.Dispose();
        var act = () => manager.Dispose();

        act.Should().NotThrow("Dispose 可重入、幂等（基类契约）");
        manager.ClientSecretCacheHasValueForTest.Should().BeFalse();
    }

    [Fact]
    public async Task SecretProviderFailure_ShouldNotLogExceptionObject()
    {
        const string leakedSecret = "super-secret-token-value";
        var collector = new RecordingLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { collector });

        var secretProvider = new Mock<ISecretProvider>();
        secretProvider.Setup(p => p.GetSecretAsync("cid"))
            .ThrowsAsync(new InvalidOperationException($"vault returned: {leakedSecret}"));

        using var httpClient = CreateHttpClientReturningToken();
        var manager = CreateManager(
            httpClient,
            o => o.ClientSecretProviderName = "cid",
            secretProvider.Object,
            loggerFactory);

        await manager.GetTokenByClientCredentialsAsync(cancellationToken: CancellationToken.None);

        collector.HasEventId(152).Should().BeTrue("回退事件仍须被记录（可观测性不降级）");
        collector.ContainsText(nameof(InvalidOperationException))
            .Should().BeTrue("只记录异常类型名，便于定位故障类别");
        collector.ContainsText(leakedSecret)
            .Should().BeFalse("R-P2-05（C4）：异常对象不得进日志（其消息可能内嵌密钥服务的敏感报文）");
    }

    [Fact]
    public void ClientSecretCache_Clear_ShouldDropPlaintext()
    {
        var cache = new ClientSecretCache(TimeSpan.FromMinutes(5));

        cache.GetAsync(_ => Task.FromResult<string?>("secret"), CancellationToken.None)
            .GetAwaiter().GetResult();
        cache.HasCachedValueForTest.Should().BeTrue();

        cache.Clear();
        cache.HasCachedValueForTest.Should().BeFalse();
    }

    [Fact]
    public async Task ClientSecretCache_FactoryFailure_ShouldClearStaleValue()
    {
        // 短 TTL：必须先过期才会重新解析（未过期时命中缓存，根本不进工厂 —— 这是缓存的本职）。
        var cache = new ClientSecretCache(TimeSpan.FromMilliseconds(30));

        await cache.GetAsync(_ => Task.FromResult<string?>("stale-secret"), CancellationToken.None);
        cache.HasCachedValueForTest.Should().BeTrue();

        await Task.Delay(200);

        // 模拟密钥源故障：工厂回退为空 → 已过期的旧密钥必须被清除，而不是继续驻留内存。
        await cache.GetAsync(_ => Task.FromResult<string?>(null), CancellationToken.None);

        cache.HasCachedValueForTest.Should().BeFalse("解析失败时陈旧密钥不得继续驻留（R-P2-05 C3）");
    }

    [Fact]
    public async Task ClientSecretCache_Dispose_ShouldClearPlaintext()
    {
        var cache = new ClientSecretCache(TimeSpan.FromMinutes(5));
        await cache.GetAsync(_ => Task.FromResult<string?>("secret"), CancellationToken.None);

        cache.Dispose();

        cache.HasCachedValueForTest.Should().BeFalse();
    }

    [Fact]
    public async Task ClientSecretCache_TtlZero_ShouldNotRetainValue()
    {
        var ttlSeconds = 300;
        var cache = new ClientSecretCache(() => TimeSpan.FromSeconds(ttlSeconds));

        await cache.GetAsync(_ => Task.FromResult<string?>("secret"), CancellationToken.None);
        cache.HasCachedValueForTest.Should().BeTrue();

        // TTL 热更新为"不缓存"：不得把上一次的值继续留在字段里。
        ttlSeconds = 0;
        await cache.GetAsync(_ => Task.FromResult<string?>("secret"), CancellationToken.None);

        cache.HasCachedValueForTest.Should().BeFalse();
    }
}
