// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P0-03（TR-06）：用户级刷新串行化 —— 同一 userId 的刷新不得并发使用同一个 refresh_token。
/// </summary>
/// <remarks>
/// 缺陷（B3）：锁键为"用户 × 作用域"复合键，而刷新调用 <see cref="UserTokenManagerBase.RefreshUserTokenAsync"/>
/// 是<b>用户级</b>的 —— 同用户两个作用域并发时持有两把不同锁，却会并发使用同一 refresh_token，
/// 轮换型 IdP 下触发 <c>invalid_grant</c> / 令牌丢失。
/// <para>
/// 修订后的验收语义：串行化（<c>maxConcurrentRefresh == 1</c>），而非"只刷新一次"——
/// 后者要求跨作用域复用刷新结果，会破坏既有的 D7 作用域隔离（见修复方案 §3.3 评审修订 9）。
/// </para>
/// </remarks>
public class UserTokenRefreshSerializationTests
{
    [Fact]
    public async Task SameUser_DifferentScopes_ConcurrentRefresh_ShouldSerializeRefresh()
    {
        using var manager = new ProbeUserTokenManager(TimeSpan.FromMilliseconds(50));
        var scopeSets = new[]
        {
            new[] { "read:admin" },
            new[] { "read:basic" },
        };

        var tasks = Enumerable.Range(0, 20)
            .Select(i => Task.Run(() => manager.GetOrRefreshTokenAsync("u1", scopeSets[i % 2])))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == "token-for-u1");
        manager.MaxConcurrentRefresh.Should().Be(1,
            "同一 userId 的刷新必须互相串行 —— 否则 refresh_token 会被并发使用（B3）");
        manager.RefreshCount.Should().BeLessThanOrEqualTo(2,
            "每个作用域至多刷新一次（同作用域内的并发由复合键锁收敛）");
    }

    [Fact]
    public async Task SameUser_LogoutDuringRefresh_ShouldNotReviveToken()
    {
        using var manager = new ProbeUserTokenManager(TimeSpan.Zero, gateRefresh: true);
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OnRefreshStarted = () => refreshStarted.TrySetResult(true);

        var pending = manager.GetOrRefreshTokenAsync("u1", new[] { "read:admin" });

        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 刷新在途时登出
        await manager.RemoveTokenAsync("u1");

        manager.ReleaseRefresh();

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("登出后写回必须被 TR-04 代际守卫丢弃（新增用户闸不得削弱该语义）");
        (await manager.HasValidTokenAsync("u1")).Should().BeFalse();
    }

    [Fact]
    public async Task DifferentUsers_ShouldNotSerialize()
    {
        using var manager = new ProbeUserTokenManager(TimeSpan.FromMilliseconds(200));

        var tasks = Enumerable.Range(0, 4)
            .Select(i => Task.Run(() => manager.GetOrRefreshTokenAsync($"user-{i}")))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r != null && r.StartsWith("token-for-user-"));
        manager.RefreshCount.Should().Be(4, "不同用户应各自刷新一次");
        manager.MaxConcurrentRefresh.Should().Be(4,
            "用户闸必须按 userId 键控，不得退化为全局串行（否则多用户场景被无谓串行化）");
    }

    [Fact]
    public async Task UserRefreshGate_ShouldNotLeakEntries()
    {
        using var manager = new ProbeUserTokenManager(TimeSpan.Zero);

        foreach (var i in Enumerable.Range(0, 20))
        {
            await manager.GetOrRefreshTokenAsync($"gate-user-{i}");
        }

        manager.UserRefreshGateCountForTest.Should().Be(0,
            "刷新结束后闸条目必须被退休回收，避免按 userId 的无界增长");
    }

    [Fact]
    public async Task SameUser_SameScope_Concurrent_ShouldRefreshOnce()
    {
        using var manager = new ProbeUserTokenManager(TimeSpan.FromMilliseconds(50));
        var scopes = new[] { "read:admin" };

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => manager.GetOrRefreshTokenAsync("u2", scopes)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r == "token-for-u2");
        manager.RefreshCount.Should().Be(1, "同一作用域内的并发必须被收敛为一次刷新");
        manager.MaxConcurrentRefresh.Should().Be(1);
    }

    private sealed class ProbeUserTokenManager : UserTokenManagerBase
    {
        private readonly TimeSpan _refreshDelay;
        private readonly bool _gateRefresh;

        private int _refreshCount;
        private int _concurrent;
        private int _maxConcurrent;
        private TaskCompletionSource<bool> _refreshGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProbeUserTokenManager(TimeSpan refreshDelay, bool gateRefresh = false)
        {
            _refreshDelay = refreshDelay;
            _gateRefresh = gateRefresh;
        }

        public int RefreshCount => Volatile.Read(ref _refreshCount);

        public int MaxConcurrentRefresh => Volatile.Read(ref _maxConcurrent);

        public Action? OnRefreshStarted { get; set; }

        public void ReleaseRefresh() => _refreshGate.TrySetResult(true);

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "tenant-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
            string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override async Task<UserTokenInfo?> RefreshUserTokenAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _refreshCount);
            RecordMaxConcurrent(Interlocked.Increment(ref _concurrent));
            OnRefreshStarted?.Invoke();

            try
            {
                if (_gateRefresh)
                {
                    await _refreshGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (_refreshDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_refreshDelay, cancellationToken).ConfigureAwait(false);
                }

                return new UserTokenInfo
                {
                    UserId = userId,
                    AccessToken = $"token-for-{userId}",
                    AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                    RefreshToken = "refresh-token",
                };
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        public override Task<bool> HasValidTokenAsync(string userId, CancellationToken cancellationToken = default)
            => base.HasValidTokenAsync(userId, cancellationToken);

        public override Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        private void RecordMaxConcurrent(int candidate)
        {
            int current;
            while ((current = Volatile.Read(ref _maxConcurrent)) < candidate)
            {
                if (Interlocked.CompareExchange(ref _maxConcurrent, candidate, current) == current)
                    return;
            }
        }
    }
}
