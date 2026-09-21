using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils.Client.Tests.Token.Recovery;

/// <summary>
/// TR-02（用例 #8）：401 恢复不得销毁 refresh_token —— 失效语义分层验收。
/// <para>
/// 先红记录（2026-09-21，修复前）：恢复链路调 InvalidateTokenAsync 整条清除缓存（含 refresh_token），
/// 随后的 RefreshTokenCoreAsync 读到 null RefreshToken 静默降级 client_credentials ⇒
/// refreshFlowCount == 0、fallbackFlowCount == 1，断言失败。
/// 修复后：恢复仅清访问令牌字段，RefreshToken 保留并被刷新流程消费。
/// </para>
/// </summary>
public class TokenRecoveryRefreshTokenPreservationTests
{
    /// <summary>
    /// 支持 refresh_token 流程的最小管理器：刷新时优先消费缓存中的 RefreshToken；
    /// 无 RefreshToken 则走"client_credentials 回退"计数。
    /// </summary>
    private sealed class RefreshFlowCountingManager : TokenManagerBase
    {
        public int RefreshTokenFlowCount;
        public int ClientCredentialsFallbackCount;

        public void SeedDefaultScope(CredentialToken token) => UpdateScopedToken("default", token);

        public CredentialToken? PeekDefaultScope() => GetCachedCredentialToken();

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            var cached = GetCachedCredentialToken();
            if (cached?.RefreshToken != null)
            {
                Interlocked.Increment(ref RefreshTokenFlowCount);
                return Task.FromResult(new CredentialToken
                {
                    AccessToken = "new-access-token",
                    Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                    RefreshToken = cached.RefreshToken
                });
            }

            Interlocked.Increment(ref ClientCredentialsFallbackCount);
            return Task.FromResult(new CredentialToken
            {
                AccessToken = "client-credentials-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
        }
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode) => new(statusCode)
    {
        Content = new StringContent(string.Empty)
    };

    [Fact]
    public async Task Recovery_ShouldPreserveRefreshToken_After401()
    {
        var manager = new RefreshFlowCountingManager();
        manager.SeedDefaultScope(new CredentialToken
        {
            AccessToken = "stale-access-token",
            Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            RefreshToken = "rt-must-survive",
            RefreshTokenExpire = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds()
        });

        var executor = new TokenRecoveryExecutor(
            manager, new TokenRecoveryOptions { RefreshDedupWindowSeconds = 0 }, NullLogger.Instance);

        var sendCount = 0;
        var response = await executor.ExecuteAsync(
            CreateAuthorizedRequest(),
            (_, _) =>
            {
                var n = Interlocked.Increment(ref sendCount);
                return Task.FromResult(n == 1 ? Response(HttpStatusCode.Unauthorized) : Response(HttpStatusCode.OK));
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "401 恢复应成功重试");
        manager.RefreshTokenFlowCount.Should().BeGreaterOrEqualTo(1,
            "恢复刷新必须走 refresh_token 流程（保留的凭据被消费）");
        manager.ClientCredentialsFallbackCount.Should().Be(0,
            "refresh_token 被保留时不得降级 client_credentials");

        var after = manager.PeekDefaultScope();
        after.Should().NotBeNull();
        after!.RefreshToken.Should().NotBeNullOrEmpty("401 恢复后 refresh_token 必须留存（TR-02 验收）");
    }

    [Fact]
    public async Task Recovery_OnRefreshOnlyIdP_ShouldSucceedOnSecondGrant()
    {
        // 自愈承诺：仅支持 refresh_token 的 IdP 上，401 恢复不销毁凭据即可自愈
        var manager = new RefreshFlowCountingManager();
        manager.SeedDefaultScope(new CredentialToken
        {
            AccessToken = "stale-access-token",
            Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            RefreshToken = "rt-refresh-only"
        });

        var executor = new TokenRecoveryExecutor(
            manager, new TokenRecoveryOptions { RefreshDedupWindowSeconds = 0 }, NullLogger.Instance);

        var sendCount = 0;
        var response = await executor.ExecuteAsync(
            CreateAuthorizedRequest(),
            (_, _) =>
            {
                var n = Interlocked.Increment(ref sendCount);
                return Task.FromResult(n == 1 ? Response(HttpStatusCode.Unauthorized) : Response(HttpStatusCode.Accepted));
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        manager.RefreshTokenFlowCount.Should().Be(1);
    }

    [Fact]
    public async Task Recovery_ShouldInvalidateOnlyAccessToken_KeepRefreshTokenForNextRecovery()
    {
        // 连续两轮 401：第二轮恢复仍可走 refresh_token 流程（凭据未被第一轮销毁）
        var manager = new RefreshFlowCountingManager();
        manager.SeedDefaultScope(new CredentialToken
        {
            AccessToken = "stale-access-token",
            Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            RefreshToken = "rt-two-rounds"
        });

        // 去重窗口归零：第二轮 401 不得复用第一轮刷新结果（否则两轮恢复仅走一次 refresh 流程）
        var executor = new TokenRecoveryExecutor(
            manager, new TokenRecoveryOptions { RefreshDedupWindowSeconds = 0 }, NullLogger.Instance);

        // 第一轮 401：恢复成功并重试
        var sendCount = 0;
        await executor.ExecuteAsync(
            CreateAuthorizedRequest(),
            (_, _) =>
            {
                var n = Interlocked.Increment(ref sendCount);
                return Task.FromResult(n == 1 ? Response(HttpStatusCode.Unauthorized) : Response(HttpStatusCode.OK));
            },
            CancellationToken.None);

        // 第二轮 401：新一轮恢复仍应成功走 refresh_token 流程
        var response2 = await executor.ExecuteAsync(
            CreateAuthorizedRequest(),
            (_, _) => Task.FromResult(Response(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        response2.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "第二轮仅一次发送（401），无更多重试预算时的真实 401");
        manager.ClientCredentialsFallbackCount.Should().Be(0, "两轮恢复均不得降级 client_credentials");
        manager.RefreshTokenFlowCount.Should().BeGreaterOrEqualTo(2, "第二轮恢复仍应消费 refresh_token");
    }

    private static HttpRequestMessage CreateAuthorizedRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/resource");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale-access-token");
        return request;
    }
}
