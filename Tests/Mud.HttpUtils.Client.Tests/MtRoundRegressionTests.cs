// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using System.Text.Json;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// MT 轮（多应用与令牌管理）缺陷修复的回归护栏。
/// <para>
/// 本文件中的每条用例在修复前<b>必然失败</b>，作为对应缺陷不再复现的守卫。
/// 覆盖：MT-01（跨主机重定向放弃恢复）、MT-04（密钥缓存 TTL=0）、
/// MT-05（去重失败不产生未观察异常）、MT-06（去重表有界）、
/// MT-07（用户侧 TTL 感知阈值）、MT-10（白名单仍强制 HTTPS）、
/// MT-11（GetTokenAsync(scopes) 生效）、MT-02（AllowAllAppAccessAuthorizer）。
/// </para>
/// </summary>
public class MtRoundRegressionTests
{
    #region MT-04 ClientSecretCache

    [Fact]
    public async Task ClientSecretCache_ZeroTtl_ShouldNotCache()
    {
        var calls = 0;
        var cache = new ClientSecretCache(TimeSpan.Zero);

        for (var i = 0; i < 3; i++)
        {
            var value = await cache.GetAsync(() =>
            {
                calls++;
                return Task.FromResult<string?>("secret-" + calls);
            }, CancellationToken.None);

            value.Should().Be("secret-" + (i + 1));
        }

        // MT-04：TTL<=0 表示「不缓存」，必须每次重新解析。
        // 修复前把 _expiresAtTicks 写成 long.MaxValue ⇒ 永久缓存，calls == 1。
        calls.Should().Be(3, "TTL=0 表示不缓存（原实现语义反转，实际永久缓存）");
    }

    [Fact]
    public async Task ClientSecretCache_PositiveTtl_ShouldCacheWithinWindow()
    {
        var calls = 0;
        var cache = new ClientSecretCache(TimeSpan.FromMinutes(5));

        for (var i = 0; i < 3; i++)
        {
            await cache.GetAsync(() =>
            {
                calls++;
                return Task.FromResult<string?>("secret");
            }, CancellationToken.None);
        }

        calls.Should().Be(1, "TTL 窗口内应命中缓存");
    }

    [Fact]
    public async Task ClientSecretCache_EmptyResult_ShouldNotBeCached()
    {
        var calls = 0;
        var cache = new ClientSecretCache(TimeSpan.FromMinutes(5));

        await cache.GetAsync(() => { calls++; return Task.FromResult<string?>(null); }, CancellationToken.None);
        await cache.GetAsync(() => { calls++; return Task.FromResult<string?>("real"); }, CancellationToken.None);

        calls.Should().Be(2, "空结果（密钥源未就绪）不得被固化到缓存");
    }

    /// <summary>
    /// L-10：TTL 由委托按需读取，配置热更新后无需重建管理器即生效。
    /// 修复前 TTL 在构造时固化，`ClientSecretCacheTtlSeconds` 变更必须重建管理器
    /// （重建会连带丢失令牌缓存）。
    /// </summary>
    [Fact]
    public async Task ClientSecretCache_TtlHotReload_ShouldTakeEffectWithoutRecreation()
    {
        var ttl = TimeSpan.FromMinutes(5);
        var calls = 0;
        var cache = new ClientSecretCache(() => ttl);

        await cache.GetAsync(() => { calls++; return Task.FromResult<string?>("s"); }, CancellationToken.None);
        await cache.GetAsync(() => { calls++; return Task.FromResult<string?>("s"); }, CancellationToken.None);
        calls.Should().Be(1, "TTL 窗口内应命中缓存");

        // 模拟配置热更新：TTL 改为 0（不缓存）
        ttl = TimeSpan.Zero;

        await cache.GetAsync(() => { calls++; return Task.FromResult<string?>("s"); }, CancellationToken.None);
        await cache.GetAsync(() => { calls++; return Task.FromResult<string?>("s"); }, CancellationToken.None);

        calls.Should().Be(3, "L-10：TTL 热更新后应立即生效（此处期望每次都重新解析）");
    }

    #endregion

    #region MT-05 / MT-06 RefreshDedupTable

    [Fact]
    public async Task RefreshDedupTable_WithinWindow_ShouldShareSingleRefresh()
    {
        var calls = 0;
        var table = new RefreshDedupTable(64);

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => table.GetOrRefreshAsync("k", async () =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(30);
                return "token";
            }, dedupWindowSeconds: 5))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().AllBe("token");
        calls.Should().Be(1, "去重窗口内并发请求应共享同一次刷新（single-flight）");
    }

    /// <summary>
    /// MT-05：刷新失败时，赢者与等待者 await 同一个 Task，异常必然被观察。
    /// 修复前使用 TaskCompletionSource.SetException 且无等待者时会触发
    /// <see cref="TaskScheduler.UnobservedTaskException"/>。
    /// </summary>
    [Fact]
    public async Task RefreshDedupTable_FailureWithoutWaiters_ShouldNotRaiseUnobservedException()
    {
        var unobserved = 0;
        void Handler(object? _, UnobservedTaskExceptionEventArgs e)
        {
            Interlocked.Increment(ref unobserved);
            e.SetObserved();
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            var table = new RefreshDedupTable(64);

            var act = () => table.GetOrRefreshAsync("k", () => throw new InvalidOperationException("boom"), 5);
            await act.Should().ThrowAsync<InvalidOperationException>();

            // 强制回收：未被观察的任务异常会在此触发 UnobservedTaskException。
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            await Task.Delay(50);

            unobserved.Should().Be(0, "MT-05：去重刷新失败不得产生未观察任务异常");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }
    }

    /// <summary>MT-06：去重表条目数必须有硬上限（键含 userId 时为高基数）。</summary>
    [Fact]
    public async Task RefreshDedupTable_ExceedingLimit_ShouldStayBounded()
    {
        const int limit = 16;
        var table = new RefreshDedupTable(limit);

        for (var i = 0; i < 500; i++)
        {
            await table.GetOrRefreshAsync(
                "user-" + i,
                () => Task.FromResult<string?>("t"),
                dedupWindowSeconds: 600);   // 窗口很长，条目不会自然过期
        }

        table.Count.Should().BeLessThanOrEqualTo(limit, "MT-06：去重表必须有界（原实现无界增长）");
    }

    #endregion

    #region MT-01 跨主机重定向

    [Fact]
    public async Task TokenRecovery_CrossHostRedirect_ShouldAbandonRecovery()
    {
        var tokenManager = new Mock<ITokenManager>();
        var executor = new TokenRecoveryExecutor(
            tokenManager.Object,
            new TokenRecoveryOptions { RecoveryMaxRetries = 1 },
            NullLogger.Instance);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "old-token");

        var sends = 0;
        var response = await executor.ExecuteAsync(request, (_, _) =>
        {
            sends++;
            // 模拟「服务端 30x 到外部主机」：BCL 会把最终落点写回 ResponseMessage.RequestUri。
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://evil.test/data"),
            });
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sends.Should().Be(1, "MT-01：首跳已跨主机重定向时必须放弃恢复（禁止把令牌发往第三方）");
        tokenManager.Verify(
            m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never,
            "MT-01：跨主机重定向后不得触发任何令牌刷新");
    }

    [Fact]
    public async Task TokenRecovery_SameHost_ShouldStillRecover()
    {
        var tokenManager = new Mock<ITokenManager>();
        tokenManager.Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("fresh-token");
        tokenManager.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("fresh-token");

        var executor = new TokenRecoveryExecutor(
            tokenManager.Object,
            new TokenRecoveryOptions { RecoveryMaxRetries = 1 },
            NullLogger.Instance);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer old");

        var sends = 0;
        var response = await executor.ExecuteAsync(request, (req, _) =>
        {
            sends++;
            return Task.FromResult(new HttpResponseMessage(
                sends == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            {
                // 同源：最终落点与请求 URI 一致
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, req.RequestUri!),
            });
        }, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "同主机 401 恢复必须仍然生效（防过度修复）");
        sends.Should().Be(2);
    }

    #endregion

    #region MT-07 用户侧 TTL 感知阈值

    [Fact]
    public void UserTokenInfo_ShortTtl_ShouldBeValidWithIssuedAt()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // TTL 300s、配置阈值 300s：3 参判定下 expire-threshold <= now 恒成立（永不有效）；
        // 4 参 TTL 感知阈值把提前量钳位为 ttl/2=150s ⇒ 仍有效。
        var info = new UserTokenInfo
        {
            AccessToken = "t",
            AccessTokenExpireTime = now + 300_000,
            IssuedAt = now,
        };

        info.IsAccessTokenValid(300).Should().BeTrue(
            "MT-07：短 TTL 令牌应按 min(阈值, ttl/2) 判定，不应刚签发即被判为需刷新");
    }

    [Fact]
    public void UserTokenInfo_WithoutIssuedAt_FallsBackToConfiguredThreshold()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var info = new UserTokenInfo
        {
            AccessToken = "t",
            AccessTokenExpireTime = now + 300_000,
            IssuedAt = 0,   // 存量数据：无签发时间
        };

        info.IsAccessTokenValid(300).Should().BeFalse(
            "IssuedAt=0 时退化为配置阈值（与历史行为一致）");
    }

    #endregion

    #region MT-10 白名单仍强制 HTTPS

    [Fact]
    public void UrlValidator_WhitelistedDomain_ShouldStillRequireHttps()
    {
        UrlValidator.SetConfigurationDomains(new[] { "insecure.example.com" });
        try
        {
            var act = () => UrlValidator.ValidateUrl("http://insecure.example.com/api");
            act.Should().Throw<InvalidOperationException>(
                "MT-10：白名单只豁免 IP/内网域名检查，不得豁免 HTTPS 强制（否则令牌明文上网）");
        }
        finally
        {
            UrlValidator.SetConfigurationDomains(Array.Empty<string>());
        }
    }

    [Fact]
    public void UrlValidator_WhitelistedDomain_WithHttps_ShouldPass()
    {
        UrlValidator.SetConfigurationDomains(new[] { "secure.example.com" });
        try
        {
            var act = () => UrlValidator.ValidateUrl("https://secure.example.com/api");
            act.Should().NotThrow();
        }
        finally
        {
            UrlValidator.SetConfigurationDomains(Array.Empty<string>());
        }
    }

    [Fact]
    public void UrlValidator_AllowInsecureWhitelistedDomains_EscapeHatch()
    {
        UrlValidator.SetConfigurationDomains(new[] { "legacy.example.com" });
        UrlValidator.SetAllowInsecureWhitelistedDomains(true);
        try
        {
            var act = () => UrlValidator.ValidateUrl("http://legacy.example.com/api");
            act.Should().NotThrow("显式开启逃生门后允许白名单域名走 HTTP");
        }
        finally
        {
            UrlValidator.SetAllowInsecureWhitelistedDomains(false);
            UrlValidator.SetConfigurationDomains(Array.Empty<string>());
        }
    }

    #endregion

    #region MT-11 GetTokenAsync(scopes)

    [Fact]
    public async Task StandardOAuth2TokenManager_GetTokenAsyncWithScopes_ShouldSendScopes()
    {
        string? capturedBody = null;
        var handler = new CapturingHandler(async content =>
        {
            capturedBody = content is null ? null : await content.ReadAsStringAsync();
            return """
                   {"access_token":"tok","expires_in":3600,"token_type":"Bearer"}
                   """;
        });

        var httpClient = new HttpClient(handler);
        var options = new OAuth2Options
        {
            ClientId = "cid",
            ClientSecret = "secret",
            TokenEndpoint = "https://idp.example.com/token",
        };

        var manager = new StandardOAuth2TokenManager(
            httpClient, Microsoft.Extensions.Options.Options.Create(options));

        var token = await manager.GetTokenAsync(new[] { "read:admin" }, CancellationToken.None);

        token.Should().Be("tok");
        capturedBody.Should().NotBeNull();
        // MT-11：修复前 GetTokenAsync(scopes) 静默丢弃 scopes 并走默认作用域。
        capturedBody.Should().Contain(Uri.EscapeDataString("read:admin"),
            "MT-11：ITokenManager.GetTokenAsync(scopes) 必须把 scopes 传递给令牌端点");
    }

    #endregion

    #region MT-02 显式放行逃生门

    [Fact]
    public void AllowAllAppAccessAuthorizer_CanSwitchTo_AlwaysTrue()
    {
        var authorizer = new AllowAllAppAccessAuthorizer();
        authorizer.CanSwitchTo("any-app").Should().BeTrue();
        authorizer.CanSwitchTo(string.Empty).Should().BeTrue();
    }

    #endregion

    #region MT-16 ScopeKeyBuilder

    [Fact]
    public void ScopeKeyBuilder_CommaInsideElement_ShouldNotCollide()
    {
        ScopeKeyBuilder.Build(new[] { "a,b" })
            .Should().NotBe(ScopeKeyBuilder.Build(new[] { "a", "b" }),
                "MT-16：元素内含分隔符不得与多元素语义碰撞（原 ',' 拼接存在键碰撞）");
    }

    #endregion

    private sealed class CapturingHandler(Func<HttpContent?, Task<string>> responder) : HttpMessageHandler
    {
        private readonly Func<HttpContent?, Task<string>> _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await _responder(request.Content).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }
    }
}
