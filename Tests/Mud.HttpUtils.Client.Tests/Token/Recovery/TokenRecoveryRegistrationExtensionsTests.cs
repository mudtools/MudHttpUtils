// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// F2 + G4：令牌恢复的 DI 接入与"带恢复能力的客户端"创建入口。
/// </summary>
public class TokenRecoveryRegistrationExtensionsTests
{
    private static Mock<ITokenManager> NewTokenManagerMock()
    {
        var mock = new Mock<ITokenManager>();
        mock.Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mock.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");
        return mock;
    }

    // ── F2：执行器注册 ─────────────────────────────────────────────────

    [Fact]
    public void AddTokenRecoveryExecutor_ShouldBeResolvable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(NewTokenManagerMock().Object);

        services.AddTokenRecoveryExecutor();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TokenRecoveryExecutor>().Should().NotBeNull(
            "注册必须使用工厂委托构造（多 public 构造 ⇒ 容器默认构造会抛 ambiguous，TMX-19）");
    }

    [Fact]
    public void AddTokenRecoveryExecutor_ShouldNotDuplicateManualRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(NewTokenManagerMock().Object);
        // 模拟下游手工注册（DiAmbiguityGuardTests 的既有场景）
        services.AddSingleton(sp => new TokenRecoveryExecutor(
            sp.GetRequiredService<ITokenManager>(),
            sp.GetRequiredService<IOptionsMonitor<TokenRecoveryOptions>>()));

        services.AddTokenRecoveryExecutor();
        services.AddTokenRecoveryExecutor();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<TokenRecoveryExecutor>().Should().HaveCount(1,
            "TryAdd 语义：不得与下游手工注册叠加出双实例");
    }

    [Fact]
    public void AddTokenRecoveryExecutor_DisableUserInference_ShouldBeResolvable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(NewTokenManagerMock().Object);

        services.AddTokenRecoveryExecutor(disableUserTokenInference: true);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TokenRecoveryExecutor>().Should().NotBeNull(
            "禁用用户级令牌推断（TMA-02 的 null 语义）时仍可解析");
    }

    [Fact]
    public void AddTokenRecoveryExecutor_NullServices_Throws()
    {
        var act = () => ((IServiceCollection)null!).AddTokenRecoveryExecutor();

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    // ── G4：带恢复能力的客户端 ─────────────────────────────────────────

    [Fact]
    public void AddTokenRecoveryClient_FactoryShouldReturnRecoveryClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(NewTokenManagerMock().Object);
        services.AddMudHttpClient("recovery-api", client => client.BaseAddress = new Uri("https://api.example.com"));
        services.AddTokenRecoveryClient("recovery-api");

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IEnhancedHttpClientFactory>();

        var client = factory.CreateClient("recovery-api");

        client.Should().BeOfType<TokenRecoveryEnhancedClient>(
            "G4：登记后经既有工厂接口 CreateClient 即返回带恢复能力的客户端");
    }

    [Fact]
    public void AddTokenRecoveryClient_WithExplicitExecutor_ShouldNotDuplicateExecutor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(NewTokenManagerMock().Object);
        services.AddMudHttpClient("recovery-api", client => client.BaseAddress = new Uri("https://api.example.com"));
        services.AddTokenRecoveryExecutor();

        var executor = ResolveExecutorProbe(services);
        services.AddTokenRecoveryClient("recovery-api", executor);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<TokenRecoveryExecutor>().Should().HaveCount(1,
            "显式传入下游自造执行器时同样必须 TryAdd（不产生双实例）");
        provider.GetRequiredService<IEnhancedHttpClientFactory>()
            .CreateClient("recovery-api").Should().BeOfType<TokenRecoveryEnhancedClient>();
    }

    /// <summary>构建一次临时容器取出执行器，用于模拟"下游已手工持有执行器实例"的场景。</summary>
    private static TokenRecoveryExecutor ResolveExecutorProbe(IServiceCollection services)
    {
        using var probe = services.BuildServiceProvider();
        return probe.GetRequiredService<TokenRecoveryExecutor>();
    }

    [Fact]
    public void CreateTokenRecoveryClient_ShouldReturnRecoveryClient_WithoutTouchingFactoryCache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(NewTokenManagerMock().Object);
        services.AddMudHttpClient("recovery-api", client => client.BaseAddress = new Uri("https://api.example.com"));
        services.AddTokenRecoveryExecutor();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IEnhancedHttpClientFactory>();
        var executor = provider.GetRequiredService<TokenRecoveryExecutor>();

        var client = factory.CreateTokenRecoveryClient(provider, "recovery-api", executor);

        client.Should().BeOfType<TokenRecoveryEnhancedClient>();
        client.Should().NotBeSameAs(factory.CreateClient("recovery-api"),
            "本扩展方法直接构造、不经工厂缓存（调用方自持实例）");
    }

    [Fact]
    public void AddTokenRecoveryClient_InvalidClientName_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTokenRecoveryClient("   ");

        act.Should().Throw<ArgumentException>().WithParameterName("clientName");
    }

    /// <summary>
    /// G4 改案（E9）护栏：<see cref="IEnhancedHttpClientFactory"/> 公共面<b>不得</b>被改动 ——
    /// 该接口位于 Abstractions，无法引用 Client 的 <see cref="TokenRecoveryExecutor"/>；
    /// 且加成员对实现方是二进制破坏性变更。
    /// </summary>
    [Fact]
    public void IEnhancedHttpClientFactory_InterfaceShouldRemainUnchanged()
    {
        typeof(IEnhancedHttpClientFactory).GetMethods().Should().HaveCount(3,
            "CreateClient / Invalidate / InvalidateAll —— G4 必须落在 Client 侧扩展方法上");
    }
}
