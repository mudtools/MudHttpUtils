// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio  2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils.Client.Tests.Token;

/// <summary>
/// B-2：<c>ITokenCache&lt;T&gt;</c> DI 回退注册（ITokenStore 自动桥接）的行为用例。
/// </summary>
/// <remarks>
/// 测试口径（方案钉死）：三种注册组合（只 Store / 只 Cache / 两者）× 桥接结果断言 ——
/// 显式 <c>ITokenCache&lt;T&gt;</c> 优先（TryAdd，与顺序无关）；无显式缓存但有 store 时自动桥接为
/// <c>TokenStoreBackedTokenCache&lt;T&gt;</c>；两者皆无则维持现状（内建内存缓存同款实现）。
/// </remarks>
public class TokenCacheStoreAutoBridgeTests
{
    // ---- 测试替身 ----

    private sealed class StubTokenManager : TokenManagerBase
    {
        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "stub-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });
    }

    /// <summary>经构造注入缓存的管理器：验证自动桥接确实进入管理器消费路径。</summary>
    private sealed class CacheInjectedTokenManager : TokenManagerBase
    {
        public ITokenCache<CredentialToken> InjectedCache { get; }

        public CacheInjectedTokenManager(ITokenCache<CredentialToken> tokenCache)
            : base(tokenCache)
            => InjectedCache = tokenCache;

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "stub-token",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            });
    }

    /// <summary>用户维度 store：无 userId 成员一律不应被租户维度桥接触及（C7）。</summary>
    private sealed class StubUserTokenStore : IUserTokenStore
    {
        public Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("租户维度桥接不得调用无 userId 成员（C7）");

        public Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("租户维度桥接不得调用无 userId 成员（C7）");

        public Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("租户维度桥接不得调用无 userId 成员（C7）");

        public Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("租户维度桥接不得调用无 userId 成员（C7）");

        public Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("租户维度桥接不得调用无 userId 成员（C7）");

        public Task<IEnumerable<string>> GetTokenTypesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("租户维度桥接不得调用无 userId 成员（C7）");

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> GetAccessTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task SetAccessTokenAsync(string userId, string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string?> GetRefreshTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task SetRefreshTokenAsync(string userId, string tokenType, string refreshToken, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string userId, string tokenType, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IEnumerable<string>> GetTokenTypesAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<string>());

        public Task ClearUserAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    // ---- 组合一：只注册 Store → 自动桥接 ----

    [Fact]
    public void TenantStoreOnly_TenantCacheAutoBridged_UserCacheDefault()
    {
        using var sp = Build(s =>
        {
            s.AddSingleton<ITokenStore, MemoryTokenStore>();
            s.AddMudHttpTokenManager<StubTokenManager>();
        });

        sp.GetService<ITokenCache<CredentialToken>>().Should()
            .BeOfType<TokenStoreBackedTokenCache<CredentialToken>>("租户维度 store 存在且无显式缓存 ⇒ 自动桥接");
        sp.GetService<ITokenCache<UserTokenInfo>>().Should()
            .BeOfType<MemoryCacheTokenCache<UserTokenInfo>>("无 IUserTokenStore ⇒ 用户维度维持内建内存缓存现状");
    }

    // ---- 组合二：只注册 Cache → 显式生效（无 store 可桥接）----

    [Fact]
    public void ExplicitCacheOnly_ExplicitInstanceReturned()
    {
        var explicitCache = new ConcurrentDictionaryTokenCache<CredentialToken>();
        using var sp = Build(s =>
        {
            s.AddSingleton<ITokenCache<CredentialToken>>(explicitCache);
            s.AddMudHttpTokenManager<StubTokenManager>();
        });

        sp.GetService<ITokenCache<CredentialToken>>().Should()
            .BeSameAs(explicitCache, "显式 ITokenCache 注册必须原样生效（TryAdd 显式优先）");
    }

    // ---- 组合三：Store + Cache 两者 → 显式优先，自动桥接不生效 ----

    [Fact]
    public void StoreAndCache_ExplicitCacheWins()
    {
        var explicitCache = new ConcurrentDictionaryTokenCache<CredentialToken>();
        using var sp = Build(s =>
        {
            s.AddSingleton<ITokenStore, MemoryTokenStore>();
            s.AddSingleton<ITokenCache<CredentialToken>>(explicitCache);
            s.AddMudHttpTokenManager<StubTokenManager>();
        });

        sp.GetService<ITokenCache<CredentialToken>>().Should()
            .BeSameAs(explicitCache, "显式注册优先，自动桥接不生效（store 由宿主自理，EventId 191 提示）");
    }

    // ---- 维度分流：IUserTokenStore → 用户维度；租户维度排除（C7）----

    [Fact]
    public void UserStoreOnly_UserCacheBridged_TenantCacheNotBridged()
    {
        using var sp = Build(s =>
        {
            s.AddSingleton<IUserTokenStore, StubUserTokenStore>();
            s.AddMudHttpTokenManager<StubTokenManager>();
        });

        sp.GetService<ITokenCache<UserTokenInfo>>().Should()
            .BeOfType<TokenStoreBackedTokenCache<UserTokenInfo>>("用户维度 store ⇒ 自动桥接");
        sp.GetService<ITokenCache<CredentialToken>>().Should()
            .BeOfType<ConcurrentDictionaryTokenCache<CredentialToken>>("IUserTokenStore 的无 userId 成员语义未定义（C7），不得用于租户维度桥接");
    }

    [Fact]
    public void UserStoreAlsoRegisteredAsTokenStore_TenantDimensionSkipped()
    {
        using var sp = Build(s =>
        {
            var store = new StubUserTokenStore();
            s.AddSingleton<ITokenStore>(store);
            s.AddSingleton<IUserTokenStore>(store);
            s.AddMudHttpTokenManager<StubTokenManager>();
        });

        sp.GetService<ITokenCache<UserTokenInfo>>().Should()
            .BeOfType<TokenStoreBackedTokenCache<UserTokenInfo>>();
        sp.GetService<ITokenCache<CredentialToken>>().Should()
            .BeOfType<ConcurrentDictionaryTokenCache<CredentialToken>>("即使以 ITokenStore 注册，IUserTokenStore 实现仍须跳过租户维度（C7）");
    }

    // ---- 组合外：两者皆无 → 维持现状（内建内存缓存同款）----

    [Fact]
    public void NoStoreNoCache_InMemoryDefaults()
    {
        using var sp = Build(s => s.AddMudHttpTokenManager<StubTokenManager>());

        sp.GetService<ITokenCache<CredentialToken>>().Should()
            .BeOfType<ConcurrentDictionaryTokenCache<CredentialToken>>("租户维度现状 = ConcurrentDictionaryTokenCache");
        sp.GetService<ITokenCache<UserTokenInfo>>().Should()
            .BeOfType<MemoryCacheTokenCache<UserTokenInfo>>("用户维度现状 = MemoryCacheTokenCache（无 IEncryptionProvider）");
    }

    // ---- 生命周期：Transient（每管理器实例各自缓存，防跨管理器串扰）----

    [Fact]
    public void FallbackIsTransient_PerResolutionIsolated()
    {
        using var sp = Build(s =>
        {
            s.AddSingleton<ITokenStore, MemoryTokenStore>();
            s.AddMudHttpTokenManager<StubTokenManager>();
        });

        var first = sp.GetService<ITokenCache<CredentialToken>>();
        var second = sp.GetService<ITokenCache<CredentialToken>>();

        first.Should().NotBeSameAs(second,
            "缓存键为作用域键（不含管理器身份）：共享实例会让多管理器共写同一键造成令牌串扰，故回退注册为 Transient");
    }

    // ---- 自动桥接确实进入管理器消费路径（构造注入）----

    [Fact]
    public void ManagerWithCacheConstructor_ReceivesBridgedCache()
    {
        using var sp = Build(s =>
        {
            s.AddSingleton<ITokenStore, MemoryTokenStore>();
            s.AddMudHttpTokenManager<CacheInjectedTokenManager>();
        });

        var manager = sp.GetRequiredService<CacheInjectedTokenManager>();

        manager.InjectedCache.Should()
            .BeOfType<TokenStoreBackedTokenCache<CredentialToken>>("经构造注入 ITokenCache 的管理器消费自动桥接结果");
    }

    // ---- 启动期提示（EventId 191）：store 未接入桥接的两种成因区分 ----

    private const string BridgeMissMarker = "未接入自动桥接";

    private static ServiceProvider BuildWiredHost(CollectingLoggerProvider logProvider, Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logProvider));
        configure(services);
        // 配齐多应用接线自检的前置注册，使 ValidateMudHttpAppManagement 能走到存储提示分支。
        services.AddMudHttpAppContextHolder();
        services.AddSingleton<IAppManager<IMudAppContext>, DefaultAppManager<IMudAppContext>>();
        services.AddSingleton<IAppAccessAuthorizer, AllowAllAppAccessAuthorizer>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ExplicitCacheOverridesStore_WarningDistinguishesExplicitOverrideCause()
    {
        var logProvider = new CollectingLoggerProvider();
        using var sp = BuildWiredHost(logProvider, s =>
        {
            s.AddSingleton<ITokenStore, MemoryTokenStore>();
            s.AddSingleton<ITokenCache<CredentialToken>>(new ConcurrentDictionaryTokenCache<CredentialToken>());
            s.AddMudHttpTokenManager<StubTokenManager>();
        });

        sp.ValidateMudHttpAppManagement();

        var warning = logProvider.GetLogRecords(LogLevel.Warning)
            .Single(w => w.Message.Contains(BridgeMissMarker, StringComparison.Ordinal));
        warning.Message.Should().Contain("显式注册了 ITokenCache<T>",
            "成因须区分为「显式覆盖」（自动桥接被有意旁路）");
    }

    [Fact]
    public void StoreWithoutTokenManager_WarningDistinguishesNoCacheConsumerCause()
    {
        var logProvider = new CollectingLoggerProvider();
        using var sp = BuildWiredHost(logProvider, s =>
        {
            s.AddSingleton<ITokenStore, MemoryTokenStore>();
        });

        sp.ValidateMudHttpAppManagement();

        var warning = logProvider.GetLogRecords(LogLevel.Warning)
            .Single(w => w.Message.Contains(BridgeMissMarker, StringComparison.Ordinal));
        warning.Message.Should().Contain("解析不到 ITokenCache<T>",
            "成因须区分为「尚无缓存消费解析」（自动桥接未落地）");
    }

    [Fact]
    public void StoreAutoBridged_NoRegistrationIgnoredWarning()
    {
        var logProvider = new CollectingLoggerProvider();
        using var sp = BuildWiredHost(logProvider, s =>
        {
            s.AddSingleton<ITokenStore, MemoryTokenStore>();
            s.AddMudHttpTokenManager<StubTokenManager>();
        });

        sp.ValidateMudHttpAppManagement();

        logProvider.GetLogRecords(LogLevel.Warning)
            .Should().NotContain(w => w.Message.Contains(BridgeMissMarker, StringComparison.Ordinal),
                "仅注册 store 仍自动接入 ⇒ 不提示（既有自动桥接用例不回退）");
    }
}
