// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// U-3：跨进程刷新去重租约在 <see cref="TokenManagerBase"/> 刷新路径上的行为契约 ——
/// 默认未启用零行为变化 / 抢到租约刷新后释放 / 未抢到限时轮询读穿透 / 超时 fail-open / 释放异常不掩盖刷新结果。
/// </summary>
public class TokenManagerRefreshLeaseTests
{
    private static readonly long FutureExpire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();

    /// <summary>记录调用的假租约（可编程：是否抢到、抢到时模拟"另一实例已完成刷新"）。</summary>
    private sealed class FakeLease(
        Func<string, TimeSpan, ITokenRefreshLeaseHandle?> onTryAcquire) : ITokenRefreshLease
    {
        public int TryAcquireCalls { get; private set; }
        public List<string> AcquiredKeys { get; } = new();

        public Task<ITokenRefreshLeaseHandle?> TryAcquireAsync(string leaseKey, TimeSpan leaseTime, CancellationToken cancellationToken = default)
        {
            TryAcquireCalls++;
            AcquiredKeys.Add(leaseKey);
            return Task.FromResult(onTryAcquire(leaseKey, leaseTime));
        }
    }

    private sealed class FakeLeaseHandle(string leaseKey, Action? onRelease = null, bool throwOnRelease = false) : ITokenRefreshLeaseHandle
    {
        public int ReleaseCalls { get; private set; }

        public string LeaseKey { get; } = leaseKey;

        public Task ReleaseAsync(CancellationToken cancellationToken = default)
        {
            ReleaseCalls++;
            if (throwOnRelease)
                throw new InvalidOperationException("release failed");
            onRelease?.Invoke();
            return Task.CompletedTask;
        }
    }

    /// <summary>可注入租约与刷新计数的测试管理器。</summary>
    private class LeaseTestTokenManager(
        Func<string, TimeSpan, ITokenRefreshLeaseHandle?>? onTryAcquire = null) : TokenManagerBase
    {
        public int RefreshCount { get; private set; }

        public ITokenRefreshLease? Lease { get; set; } =
            onTryAcquire == null ? null : new FakeLease(onTryAcquire);

        protected override ITokenRefreshLease? RefreshLease => Lease;

        protected override int RefreshLeaseWaitTimeoutMilliseconds => 300;

        protected override int RefreshLeasePollIntervalMilliseconds => 50;

        protected override string MetricsKey => "LeaseTestManager";

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.FromResult(new CredentialToken { AccessToken = "refreshed-token", Expire = FutureExpire });
        }

        /// <summary>模拟"另一实例刷新完成并写入共享缓存"。</summary>
        public void SimulateOtherInstanceRefresh(string accessToken)
            => UpdateScopedToken(DefaultScopeKey, new CredentialToken { AccessToken = accessToken, Expire = FutureExpire });
    }

    [Fact]
    public async Task RefreshLease_DefaultManager_Should_BeNull()
    {
        // 默认（未覆写 RefreshLease）= 未启用，行为与 3.0.9 逐位一致
        var manager = new PlainTokenManager();

        manager.ExposeRefreshLease().Should().BeNull();

        var token = await manager.GetOrRefreshTokenAsync();
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LeaseAcquired_Should_Refresh_Then_Release_Handle_Once()
    {
        ITokenRefreshLeaseHandle? handle = null;
        var manager = new LeaseTestTokenManager((key, ttl) =>
        {
            handle = new FakeLeaseHandle(key);
            return handle;
        });

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("refreshed-token");
        handle.Should().NotBeNull();
        handle!.LeaseKey.Should().Be("LeaseTestManager:default", "租约键 = {管理器键}:{scopeKey}");
        ((FakeLeaseHandle)handle).ReleaseCalls.Should().Be(1, "刷新完成后恰好释放一次");
        manager.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task LeaseAcquired_RefreshFails_Should_Still_Release_Handle()
    {
        ITokenRefreshLeaseHandle? handle = null;
        var manager = new FailingLeaseTestTokenManager((key, ttl) =>
        {
            handle = new FakeLeaseHandle(key);
            return handle;
        });

        var act = async () => await manager.GetOrRefreshTokenAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        handle.Should().NotBeNull();
        ((FakeLeaseHandle)handle!).ReleaseCalls.Should().Be(1, "刷新失败也不得泄漏租约");
    }

    [Fact]
    public async Task LeaseNotAcquired_Should_Wait_Then_FailOpen_And_Refresh()
    {
        var manager = new LeaseTestTokenManager((key, ttl) => null);   // 永远抢不到（模拟其他实例长期持有）

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("refreshed-token", "等待超时后 fail-open 放行本机刷新（租约持有方崩溃场景）");
        manager.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task LeaseNotAcquired_Should_Return_Token_Written_During_Wait()
    {
        // 模拟另一实例在等待期间完成刷新并写入共享存储：轮询读穿透直接命中，免本机刷新
        LeaseTestTokenManager manager = null!;
        manager = new LeaseTestTokenManager((key, ttl) =>
        {
            manager.SimulateOtherInstanceRefresh("other-instance-token");
            return null;
        });

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("other-instance-token");
        manager.RefreshCount.Should().Be(0, "读穿透命中后不得再发起本机刷新");
    }

    [Fact]
    public async Task LeaseRelease_Failure_Should_Not_Mask_Refresh_Result()
    {
        var manager = new LeaseTestTokenManager((key, ttl) =>
            new FakeLeaseHandle(key, throwOnRelease: true));

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("refreshed-token", "释放异常必须吞掉，不得阻断主路径");
    }

    private sealed class PlainTokenManager : TokenManagerBase
    {
        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken { AccessToken = "plain-token", Expire = FutureExpire });

        public ITokenRefreshLease? ExposeRefreshLease() => RefreshLease;
    }

    private sealed class FailingLeaseTestTokenManager(
        Func<string, TimeSpan, ITokenRefreshLeaseHandle?> onTryAcquire) : LeaseTestTokenManager(onTryAcquire)
    {
        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("refresh failed");
    }
}
