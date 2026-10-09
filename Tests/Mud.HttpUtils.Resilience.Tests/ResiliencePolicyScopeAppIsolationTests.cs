// -----------------------------------------------------------------------
//  P2-4：策略作用域键的应用维度隔离回归测试。
//  多应用共用同名客户端与同一 host 时，熔断/超时计数不得跨应用共享
// （作用域键追加 __mud_app_key 维度，与 F-01 层B 缓存键的应用维度同口径）。
// -----------------------------------------------------------------------

using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

public class ResiliencePolicyScopeAppIsolationTests
{
    private const string TestUri = "https://gateway.example.com/api/v1/users";

    [Fact]
    public void Resolve_WithAppKey_AppendsAppDimension()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, TestUri);
        request.Options.Set(new HttpRequestOptionsKey<string>(ResilienceConstants.AppKeyPropertyKey), "appA");

        var scope = ResiliencePolicyScopeResolver.Resolve(request, ResiliencePolicyScope.PerHost);

        scope.Should().Be("(default)|gateway.example.com|app:appA");
    }

    [Fact]
    public void Resolve_AppKeyWrittenToLegacyProperties_IsStillHonored()
    {
        // 兼容历史写入路径：netstandard2.0 资产的 StampResilienceAppKey（或旧版本库）经
        // request.Properties 写入 AppKey，运行在 .NET 5+ 时读取方必须经 Properties 回退兜底
        // （与 GetClientName 同口径），否则应用维度静默丢失 → 跨应用熔断隔离失效。
        var request = new HttpRequestMessage(HttpMethod.Get, TestUri);
#pragma warning disable CS0618 // HttpRequestMessage.Properties 已过时
        request.Properties[ResilienceConstants.AppKeyPropertyKey] = "appA";
#pragma warning restore CS0618 // HttpRequestMessage.Properties 已过时

        var scope = ResiliencePolicyScopeResolver.Resolve(request, ResiliencePolicyScope.PerHost);

        scope.Should().Be("(default)|gateway.example.com|app:appA",
            "Properties 历史写入路径必须与 Options 同等生效");
    }

    [Fact]
    public void Resolve_WithoutAppKey_KeepsLegacyFormat()
    {
        // 无应用上下文的既有消费方：作用域键与历史格式逐字节一致（缓存键零漂移）。
        var request = new HttpRequestMessage(HttpMethod.Get, TestUri);

        var scope = ResiliencePolicyScopeResolver.Resolve(request, ResiliencePolicyScope.PerHost);

        scope.Should().Be("(default)|gateway.example.com");
    }

    [Fact]
    public void Resolve_DifferentAppsSameHost_ProduceDistinctScopes()
    {
        var requestA = new HttpRequestMessage(HttpMethod.Get, TestUri);
        requestA.Options.Set(new HttpRequestOptionsKey<string>(ResilienceConstants.AppKeyPropertyKey), "appA");
        var requestB = new HttpRequestMessage(HttpMethod.Get, TestUri);
        requestB.Options.Set(new HttpRequestOptionsKey<string>(ResilienceConstants.AppKeyPropertyKey), "appB");

        var scopeA = ResiliencePolicyScopeResolver.Resolve(requestA, ResiliencePolicyScope.PerHost);
        var scopeB = ResiliencePolicyScopeResolver.Resolve(requestB, ResiliencePolicyScope.PerHost);

        scopeA.Should().NotBe(scopeB, "双应用共用同一网关 host 时作用域必须按 AppKey 隔离");
    }

    [Fact]
    public void Resolve_PerClientScope_AlsoCarriesAppDimension()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, TestUri);
        request.Options.Set(new HttpRequestOptionsKey<string>("__mud_client_name"), "shared-client");
        request.Options.Set(new HttpRequestOptionsKey<string>(ResilienceConstants.AppKeyPropertyKey), "appA");

        var scope = ResiliencePolicyScopeResolver.Resolve(request, ResiliencePolicyScope.PerClient);

        scope.Should().Be("shared-client|app:appA");
    }

    [Fact]
    public void SharedPolicyCache_DifferentAppScopes_GetIndependentCircuitBreakers()
    {
        // 端到端隔离断言：同一 provider（同一共享策略缓存）下，双应用作用域各自持有
        // 独立熔断实例——应用 A 打开的熔断不会拒绝应用 B 的流量。
        var options = new ResilienceOptions { CircuitBreaker = { Enabled = true } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policyA1 = provider.GetCircuitBreakerPolicy<object>("(default)|gateway.example.com|app:appA");
        var policyB = provider.GetCircuitBreakerPolicy<object>("(default)|gateway.example.com|app:appB");
        var policyA2 = provider.GetCircuitBreakerPolicy<object>("(default)|gateway.example.com|app:appA");

        policyA1.Should().NotBeSameAs(policyB, "不同应用的作用域必须命中不同熔断实例（隔离）");
        policyA1.Should().BeSameAs(policyA2, "同一应用的作用域必须命中同一熔断实例（计数合并）");
    }
}
