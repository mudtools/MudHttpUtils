using Microsoft.Extensions.DependencyInjection;

namespace Mud.HttpUtils.Client.Tests.Token;

/// <summary>
/// TR-01（用例 #1）：组合根正确性 —— AddMudHttpTokenManager 注册的三个服务类型
/// 必须解析到同一实例（令牌管理器是有状态组件：缓存 / 锁表 / 后台刷新登记均不可分裂）。
/// 先红记录（2026-09-21，修复前）：三条 implementationType 注册使 MS.DI 按 ServiceIdentifier
/// 分别缓存单例 ⇒ ITokenManager 与 IUserTokenManager 分属两个实例，BeSameAs 断言失败。
/// </summary>
public class DiTokenManagerSingletonGuardTests
{
    private sealed class DualUserTokenManager : UserTokenManagerBase
    {
        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "dual-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);
    }

    [Fact]
    public void AddMudHttpTokenManager_ShouldResolveSameInstance()
    {
        var services = new ServiceCollection();
        services.AddMudHttpTokenManager<DualUserTokenManager>();

        using var provider = services.BuildServiceProvider();

        var byConcrete = provider.GetRequiredService<DualUserTokenManager>();
        var byTokenManager = provider.GetRequiredService<ITokenManager>();
        var byUserTokenManager = provider.GetRequiredService<IUserTokenManager>();

        byTokenManager.Should().BeSameAs(byConcrete, "ITokenManager 必须转发到具体类型单例");
        byUserTokenManager.Should().BeSameAs(byConcrete, "IUserTokenManager 必须转发到具体类型单例");
    }

    [Fact]
    public void AddMudHttpTokenManager_ShouldRegisterExactlyOneInstancePerServiceType()
    {
        var services = new ServiceCollection();
        services.AddMudHttpTokenManager<DualUserTokenManager>();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<ITokenManager>().Should().ContainSingle();
        provider.GetServices<IUserTokenManager>().Should().ContainSingle();

        // 多次解析仍为同一实例（单例缓存语义）
        provider.GetRequiredService<ITokenManager>()
            .Should().BeSameAs(provider.GetRequiredService<ITokenManager>());
    }

    [Fact]
    public void AddMudHttpTokenManager_NonUserManager_ShouldNotRegisterUserInterface()
    {
        var services = new ServiceCollection();
        services.AddMudHttpTokenManager<TenantOnlyManager>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITokenManager>().Should().BeOfType<TenantOnlyManager>();
        provider.GetService<IUserTokenManager>().Should().BeNull("未实现 IUserTokenManager 的管理器不得注册该接口");
    }

    private sealed class TenantOnlyManager : TokenManagerBase
    {
        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "tenant-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("tenant-token");
    }
}
