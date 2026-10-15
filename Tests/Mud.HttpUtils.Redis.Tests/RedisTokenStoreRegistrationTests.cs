// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷与责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Mud.HttpUtils.Redis;
using StackExchange.Redis;

namespace Mud.HttpUtils.Redis.Tests;

/// <summary>
/// F-3：<c>AddMudHttpRedisTokenStore</c> 的 DI 注册钉死测试（幂等 / 守卫 / B-2 自动桥接衔接）。
/// </summary>
public class RedisTokenStoreRegistrationTests
{
    private static IConnectionMultiplexer CreateConnection()
        => new Mock<IConnectionMultiplexer>(MockBehavior.Loose).Object;

    [Fact]
    public void AddMudHttpRedisTokenStore_ShouldResolveTokenStoreWithConfiguredOptions()
    {
        var services = new ServiceCollection();
        var connection = CreateConnection();

        services.AddMudHttpRedisTokenStore(connection, o =>
        {
            o.KeyPrefix = "corp:tokens";
            o.AppKey = "app-a";
            o.DefaultTtl = TimeSpan.FromDays(7);
        });

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ITokenStore>();
        store.Should().BeOfType<RedisTokenStore>("F-3 官方实现接管 ITokenStore");

        // 选项经同一容器单例传递（键前缀注入可见）。
        var options = provider.GetRequiredService<RedisTokenStoreOptions>();
        options.KeyPrefix.Should().Be("corp:tokens");
        options.AppKey.Should().Be("app-a");
        options.DefaultTtl.Should().Be(TimeSpan.FromDays(7));

        provider.GetRequiredService<IConnectionMultiplexer>().Should().BeSameAs(connection, "连接复用器由宿主提供并复用");
    }

    [Fact]
    public void AddMudHttpRedisTokenStore_ShouldBeIdempotent_NotOverrideExistingRegistrations()
    {
        var services = new ServiceCollection();
        var connection = CreateConnection();

        services.AddMudHttpRedisTokenStore(connection, o => o.KeyPrefix = "first");
        services.AddMudHttpRedisTokenStore(connection, o => o.KeyPrefix = "second");

        using var provider = services.BuildServiceProvider();

        // TryAdd 幂等（先注册者胜）：与既有"显式注册优先"口径一致。
        provider.GetRequiredService<RedisTokenStoreOptions>().KeyPrefix.Should().Be("first");
    }

    [Fact]
    public void AddMudHttpRedisTokenStore_ShouldHonorExplicitlyRegisteredTokenStore()
    {
        var services = new ServiceCollection();
        var explicitStore = new Mock<ITokenStore>().Object;
        services.AddSingleton(explicitStore);

        services.AddMudHttpRedisTokenStore(CreateConnection());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITokenStore>().Should().BeSameAs(explicitStore,
            "TryAdd 不覆盖消费方显式注册的 ITokenStore");
    }

    [Fact]
    public void AddMudHttpRedisTokenStore_WithNullArguments_ShouldThrow()
    {
        var services = new ServiceCollection();
        var connection = CreateConnection();

        var act1 = () => ServiceCollectionExtensions.AddMudHttpRedisTokenStore(null!, connection);
        act1.Should().Throw<ArgumentNullException>();

        var act2 = () => services.AddMudHttpRedisTokenStore((IConnectionMultiplexer)null!);
        act2.Should().Throw<ArgumentNullException>();

        var act3 = () => services.AddMudHttpRedisTokenStore((string)null!);
        act3.Should().Throw<ArgumentNullException>();

        var act4 = () => services.AddMudHttpRedisTokenStore("   ");
        act4.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddMudHttpRedisTokenStore_ShouldAutoBridgeThroughB2StoreFallback()
    {
        // B-2：注册 ITokenStore 后，桥接器（TokenStoreBackedTokenCache）由 TokenCache 回退注册自动产出，
        // 无需手动包装 —— F-3 的核心衔接承诺。
        var services = new ServiceCollection();
        services.AddMudHttpRedisTokenStore(CreateConnection());

        // 触发 B-2 回退注册的基础设施入口（内含 TryAddTokenCacheStoreFallback）。
        services.AddMudHttpClientInfrastructure(new MudHttpClientApplicationOptions());

        using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<ITokenCache<CredentialToken>>();
        cache.Should().BeOfType<TokenStoreBackedTokenCache<CredentialToken>>(
            "ITokenStore 自动桥接到 TokenStoreBackedTokenCache（B-2）");
    }

    [Fact]
    public void WithoutRedisRegistration_B2FallbackShouldStayInMemory()
    {
        // 反向钉死：不注册 ITokenStore 时 B-2 维持现状（内建内存缓存），不误桥接。
        var services = new ServiceCollection();
        services.AddMudHttpClientInfrastructure(new MudHttpClientApplicationOptions());

        using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<ITokenCache<CredentialToken>>();
        cache.Should().BeOfType<ConcurrentDictionaryTokenCache<CredentialToken>>();
    }
}
