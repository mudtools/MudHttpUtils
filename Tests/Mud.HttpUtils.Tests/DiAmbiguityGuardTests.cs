using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mud.HttpUtils;

// 本测试以反射遍历公共类型/构造函数（护栏本身依赖反射）；测试程序集不参与 AOT/裁剪分析器门禁。
#pragma warning disable IL2026, IL2075

namespace Mud.HttpUtils.Tests;

/// <summary>
/// DI 构造歧义护栏（2.0.8 / BC-30/31/32/33）。
/// <para>
/// 背景：<c>ActivatorUtilitiesConstructorAttribute</c> <b>不被容器默认构造选择尊重</b>
/// （只对 <c>ActivatorUtilities</c> 生效）。当同一类型存在两个元数相同、且都能被容器满足的公共构造时，
/// <c>GetRequiredService&lt;T&gt;()</c> / <c>AddSingleton&lt;T&gt;()</c> 会抛
/// "The following constructors are ambiguous"。2.0.6/2.0.7 的 TMX-19 只修了 3 个类型，
/// 漏掉了 <see cref="TokenRecoveryDelegatingHandler"/>（组件文档推荐的
/// <c>AddHttpMessageHandler&lt;T&gt;()</c> 用法直接失败）、<see cref="StandardOAuth2TokenManager"/>
/// 与 <c>PollyResiliencePolicyProvider</c>。
/// </para>
/// 本测试用「静态扫描 + 真实容器解析」双保险，防止同类缺陷再次静默出现。
/// </summary>
public class DiAmbiguityGuardTests
{
    /// <summary>
    /// 例外清单（逐条给出理由；任何**新增**违规都会让本测试失败）：
    /// <list type="bullet">
    /// <item><c>Mud.HttpUtils.ApiException</c>：异常类型，构造仅由 <c>throw</c> 使用，从不经 DI 激活。</item>
    /// <item><c>Mud.HttpUtils.Attributes.HttpMethodAttribute</c>：特性类型，编译器/生成器直接构造，不经 DI。</item>
    /// <item><c>Mud.HttpUtils.Observability.TokenRefreshHealthCheck</c>：经 <c>AddCheck&lt;T&gt;</c> →
    /// <c>ActivatorUtilities</c> 激活（该路径尊重 <c>[ActivatorUtilitiesConstructor]</c>，已标注）；
    /// 且容器路径下 <c>TokenRefreshHealthCheckOptions</c> 未注册 ⇒ 只有一个构造可用，不构成歧义
    /// （由 <see cref="TokenRefreshHealthCheck_ShouldBeActivatable_ByActivatorUtilities"/> 与容器解析用例共同覆盖）。</item>
    /// </list>
    /// </summary>
    private static readonly string[] ArityTieAllowList =
    [
        "Mud.HttpUtils.ApiException",
        "Mud.HttpUtils.Attributes.HttpMethodAttribute",
        "Mud.HttpUtils.Observability.TokenRefreshHealthCheck",
    ];

    private static readonly Assembly[] ComponentAssemblies =
    [
        Assembly.Load("Mud.HttpUtils.Abstractions"),
        Assembly.Load("Mud.HttpUtils.Attributes"),
        Assembly.Load("Mud.HttpUtils.Client"),
        Assembly.Load("Mud.HttpUtils.Resilience"),
    ];

    /// <summary>
    /// 公共类型不得存在「元数相同的多个公共实例构造函数」。
    /// 该条件是容器歧义的必要条件（同元数且均可满足即抛异常），静态可判定、无需容器。
    /// </summary>
    [Fact]
    public void PublicTypes_ShouldNotDeclareSameArityPublicConstructors()
    {
        var violations = new List<string>();

        foreach (var assembly in ComponentAssemblies)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsEnum || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                if (ArityTieAllowList.Contains(type.FullName))
                {
                    continue;
                }

                var tiedArities = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .GroupBy(c => c.GetParameters().Length)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key} 参 x{g.Count()}");

                foreach (var tie in tiedArities)
                {
                    violations.Add($"{type.FullName}（{tie}）");
                }
            }
        }

        violations.Should().BeEmpty(
            "同元数的公共构造会让容器默认构造选择抛 \"The following constructors are ambiguous\"；"
            + "请把非主构造（快照 IOptions<T> / 无 DI 的 POCO 重载）改为 internal，"
            + "或改用 ActivatorUtilities 路径并标注 [ActivatorUtilitiesConstructor]。实际违规: {0}",
            string.Join("; ", violations));
    }

    /// <summary>
    /// 曾经抛歧义的三个类型必须能被容器解析。
    /// </summary>
    [Fact]
    public void PreviouslyAmbiguousTypes_ShouldResolveFromContainer()
    {
        var services = CreateBaselineServices();
        services.AddSingleton<TokenRecoveryDelegatingHandler>();
        services.AddSingleton<StandardOAuth2TokenManager>();
        services.AddSingleton<TokenRefreshHostedService>();
        services.AddSingleton<TokenRecoveryExecutor>();
        services.AddSingleton<Mud.HttpUtils.Resilience.PollyResiliencePolicyProvider>();
        services.AddSingleton<Mud.HttpUtils.Observability.TokenRefreshHealthCheck>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TokenRecoveryDelegatingHandler>().Should().NotBeNull();
        provider.GetRequiredService<StandardOAuth2TokenManager>().Should().NotBeNull();
        provider.GetRequiredService<TokenRefreshHostedService>().Should().NotBeNull();
        provider.GetRequiredService<TokenRecoveryExecutor>().Should().NotBeNull();
        provider.GetRequiredService<Mud.HttpUtils.Resilience.PollyResiliencePolicyProvider>().Should().NotBeNull();
        provider.GetRequiredService<Mud.HttpUtils.Observability.TokenRefreshHealthCheck>().Should().NotBeNull();
    }

    /// <summary>
    /// 组件文档推荐的 <c>AddHttpMessageHandler&lt;TokenRecoveryDelegatingHandler&gt;()</c> 用法
    /// 必须能真正构造出 HttpClient（该扩展经容器解析处理器，2.0.7 在首个 CreateClient 即抛歧义）。
    /// </summary>
    [Fact]
    public void AddHttpMessageHandler_TokenRecoveryDelegatingHandler_ShouldCreateClient()
    {
        var services = CreateBaselineServices();
        services.AddTransient<TokenRecoveryDelegatingHandler>();
        services.AddHttpClient("di-ambiguity-guard")
            .AddHttpMessageHandler<TokenRecoveryDelegatingHandler>();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("di-ambiguity-guard");

        client.Should().NotBeNull();
    }

    /// <summary>
    /// 健康检查经 <c>AddCheck&lt;T&gt;</c> → <c>ActivatorUtilities.GetServiceOrCreateInstance&lt;T&gt;</c> 激活
    /// （该路径<b>尊重</b> <c>[ActivatorUtilitiesConstructor]</c>），两个同元数构造必须不抛"多个构造函数"。
    /// </summary>
    [Fact]
    public void TokenRefreshHealthCheck_ShouldBeActivatable_ByActivatorUtilities()
    {
        using var provider = CreateBaselineServices().BuildServiceProvider();

        var instance = Microsoft.Extensions.DependencyInjection.ActivatorUtilities
            .GetServiceOrCreateInstance<Mud.HttpUtils.Observability.TokenRefreshHealthCheck>(provider);

        instance.Should().NotBeNull();
    }

    private static ServiceCollection CreateBaselineServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddOptions();
        services.AddOptions<TokenRecoveryOptions>();
        services.AddOptions<TokenRefreshBackgroundOptions>();
        services.AddOptions<OAuth2Options>();
        services.AddOptions<Mud.HttpUtils.Resilience.ResilienceOptions>();
        services.AddOptions<Mud.HttpUtils.Observability.TokenRefreshHealthCheckOptions>();
        services.AddSingleton<ITokenManager, StubTokenManager>();
        services.AddMudHttpAppContextHolder();
        services.AddTokenProvider();
        // AddHttpClient 会注册一个默认 HttpClient（StandardOAuth2TokenManager 的必需依赖）。
        services.AddHttpClient();
        return services;
    }

    private sealed class StubTokenManager : ITokenManager
    {
        public bool SupportsBackgroundRefresh => true;

        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("guard-token");

        public Task<string> GetTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => Task.FromResult("guard-token");

        public Task<string> GetOrRefreshTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("guard-token");

        public Task<string> GetOrRefreshTokenAsync(string[]? scopes, CancellationToken cancellationToken = default)
            => Task.FromResult("guard-token");

        public Task<TokenResult> InvalidateTokenAsync(string[]? scopes = null, CancellationToken cancellationToken = default)
            => Task.FromResult(TokenResult.Empty);

        public void Dispose()
        {
        }
    }
}
