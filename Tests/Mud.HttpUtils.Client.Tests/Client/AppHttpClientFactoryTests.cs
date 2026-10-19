// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Mud.HttpUtils.Client.Tests;

namespace Mud.HttpUtils.Client.Tests.Client;

/// <summary>
/// 多应用 HTTP 客户端工厂（<see cref="IAppHttpClientFactory{TAppContext}"/> 默认实现）：
/// 按 appKey 创建/缓存/失效三态；令牌恢复挂接；未注册且无执行器时的引导异常。
/// </summary>
public class AppHttpClientFactoryTests
{
    private sealed class TestAppContext : IMudAppContext
    {
        public TestAppContext(string appKey) => AppKey = appKey;

        public string AppKey { get; }

        public IEnhancedHttpClient HttpClient => throw new NotSupportedException("测试替身不承载客户端");

        public ITokenManager GetTokenManager(string tokenType) => throw new NotSupportedException();

        public T GetTokenManager<T>() where T : class, ITokenManager => throw new NotSupportedException();

        public T? GetService<T>() where T : class => null;
    }

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

    private static IAppHttpClientFactory<TestAppContext> ResolveFactory(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        services.AddAppHttpClientFactory<TestAppContext>("app");
        return services.BuildServiceProvider()
            .GetRequiredService<IAppHttpClientFactory<TestAppContext>>();
    }

    [Fact]
    public void GetClient_SameAppKey_ReturnsSameInstance_DifferentKeys_DifferentInstances()
    {
        var factory = ResolveFactory(s => s.AddMudHttpClient("app-a1"));

        var first = factory.GetClient(new TestAppContext("a1"));
        var second = factory.GetClient(new TestAppContext("a1"));

        first.Should().BeSameAs(second, "同一 appKey 走缓存（创建/缓存三态之二）");
    }

    [Fact]
    public void GetClient_WithTokenRecoveryExecutor_ReturnsRecoverableClient()
    {
        var factory = ResolveFactory(s =>
        {
            s.AddHttpClient(); // 底层 HttpClient 工厂（宿主基线；AddMudHttpClient 内含）
            s.AddSingleton<ITokenManager, StubTokenManager>();
            s.AddTokenRecoveryExecutor();
        });

        var client = factory.GetClient(new TestAppContext("a1"));

        client.Should().BeOfType<TokenRecoveryEnhancedClient>(
            "容器注册了 TokenRecoveryExecutor ⇒ 自动挂接令牌恢复能力（复用既有 CreateTokenRecoveryClient）");
    }

    [Fact]
    public void GetClient_UnregisteredWithoutExecutor_ThrowsWithRegistrationGuidance()
    {
        var factory = ResolveFactory();

        var act = () => factory.GetClient(new TestAppContext("missing"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*app-missing*", "异常消息须给出约定命名的客户端名");
    }

    [Fact]
    public void Invalidate_RemovesFromCache_NextGetCreatesNewInstance()
    {
        var factory = ResolveFactory(s => s.AddMudHttpClient("app-a1"));

        var first = factory.GetClient(new TestAppContext("a1"));
        factory.Invalidate("a1");
        var second = factory.GetClient(new TestAppContext("a1"));

        second.Should().NotBeSameAs(first, "失效后重新创建（应用配置热更新场景）");
    }

    [Fact]
    public void GetClient_NullAppContext_Throws()
    {
        var factory = ResolveFactory();

        var act = () => factory.GetClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetOrCreateClient_Registered_ReturnsResolved_Unregistered_InvokesFactory()
    {
        var services = new ServiceCollection();
        services.AddMudHttpClient("registered-client");
        using var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IHttpClientResolver>();

        resolver.GetOrCreateClient("registered-client", () => throw new InvalidOperationException("不应调用创建委托"))
            .Should().NotBeNull("已注册 ⇒ 解析优先");

        var created = false;
        resolver.GetOrCreateClient("not-registered", () =>
        {
            created = true;
            return sp.GetRequiredService<IEnhancedHttpClientFactory>().CreateClient("registered-client");
        });

        created.Should().BeTrue("未注册 ⇒ 走创建委托");
    }
}
