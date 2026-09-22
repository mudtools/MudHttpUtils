using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Resilience;
using Polly.CircuitBreaker;

namespace Mud.HttpUtils.Resilience.Tests;

public class PollyResiliencePolicyProviderTests
{
    [Fact]
    public void Constructor_WithOptions_ShouldCreateInstance()
    {
        var options = new ResilienceOptions();

        var provider = new PollyResiliencePolicyProvider(options);

        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldUseDefaults()
    {
        var provider = new PollyResiliencePolicyProvider((ResilienceOptions?)null);

        provider.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicy_WhenEnabled_ShouldReturnNonNullPolicy()
    {
        var options = new ResilienceOptions { Retry = { Enabled = true } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetRetryPolicy_WhenDisabled_ShouldReturnNoOpPolicy()
    {
        var options = new ResilienceOptions { Retry = { Enabled = false } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetTimeoutPolicy_WhenEnabled_ShouldReturnNonNullPolicy()
    {
        var options = new ResilienceOptions { Timeout = { Enabled = true } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetTimeoutPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetTimeoutPolicy_WhenDisabled_ShouldReturnNoOpPolicy()
    {
        var options = new ResilienceOptions { Timeout = { Enabled = false } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetTimeoutPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_WhenEnabled_ShouldReturnNonNullPolicy()
    {
        var options = new ResilienceOptions { CircuitBreaker = { Enabled = true } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_WhenDisabled_ShouldReturnNoOpPolicy()
    {
        var options = new ResilienceOptions { CircuitBreaker = { Enabled = false } };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_WithSamplingDuration_UsesAdvancedCircuitBreaker()
    {
        // SamplingDurationSeconds > 0 时应使用高级熔断策略
        var options = new ResilienceOptions
        {
            CircuitBreaker =
            {
                Enabled = true,
                FailureThreshold = 50,
                BreakDurationSeconds = 30,
                SamplingDurationSeconds = 60,
                MinimumThroughput = 10
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreakerPolicy_WithoutSamplingDuration_UsesSimpleCircuitBreaker()
    {
        // SamplingDurationSeconds = 0 时应使用简单熔断策略（连续失败计数�?
        var options = new ResilienceOptions
        {
            CircuitBreaker =
            {
                Enabled = true,
                FailureThreshold = 5,
                BreakDurationSeconds = 30,
                SamplingDurationSeconds = 0
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCircuitBreakerPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCombinedPolicy_ShouldReturnNonNullPolicy()
    {
        var options = new ResilienceOptions();
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCombinedPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public void GetCombinedPolicy_WithAllEnabled_ShouldReturnCombinedPolicy()
    {
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 3 },
            Timeout = { Enabled = true, TimeoutSeconds = 30 },
            CircuitBreaker = { Enabled = true, FailureThreshold = 5 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetCombinedPolicy<object>(It.IsAny<string>());

        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task RetryPolicy_WithOnRetryCallback_InvokesCallback()
    {
        var callbackInvocations = new List<(Exception? Ex, int RetryCount, TimeSpan Delay)>();
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 2,
                DelayMilliseconds = 1,
                OnRetry = (ex, retryCount, delay) =>
                {
                    callbackInvocations.Add((ex, retryCount, delay));
                    return Task.CompletedTask;
                }
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<HttpResponseMessage>(It.IsAny<string>());

        Func<Task<HttpResponseMessage>> action = () =>
            throw new HttpRequestException("test error");

        var act = async () => await policy.ExecuteAsync(action);

        await act.Should().ThrowAsync<HttpRequestException>();
        callbackInvocations.Should().HaveCount(2);
        callbackInvocations[0].RetryCount.Should().Be(1);
        callbackInvocations[1].RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task RetryPolicy_OnRetryCallbackThrows_DoesNotPropagate()
    {
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 1,
                DelayMilliseconds = 1,
                OnRetry = (ex, retryCount, delay) => throw new InvalidOperationException("callback error")
            }
        };
        var provider = new PollyResiliencePolicyProvider(Options.Create(options));

        var policy = provider.GetRetryPolicy<HttpResponseMessage>(It.IsAny<string>());

        Func<Task<HttpResponseMessage>> action = () =>
            throw new HttpRequestException("test error");

        var act = async () => await policy.ExecuteAsync(action);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RetryPolicy_WithoutOnRetryCallback_WorksNormally()
    {
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 1,
                DelayMilliseconds = 1
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var policy = provider.GetRetryPolicy<HttpResponseMessage>(It.IsAny<string>());

        Func<Task<HttpResponseMessage>> action = () =>
            throw new HttpRequestException("test error");

        var act = async () => await policy.ExecuteAsync(action);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void RetryOptions_OnRetry_DefaultIsNull()
    {
        var options = new RetryOptions();

        options.OnRetry.Should().BeNull();
    }

    #region M6-HC-22：熔断/超时策略缓存键拆分 + 超限 LRU 驱逐

    /// <summary>
    /// M6-HC-22：同一 scope 下不同结果类型的熔断策略共享同一个底层熔断器，
    /// 失败计数跨结果类型合并 —— A 类型累计到阈值并打开后，
    /// B 类型调用直接命中该熔断器（<see cref="BrokenCircuitException"/>）。
    /// </summary>
    /// <remarks>
    /// 注意：<c>GetCircuitBreakerPolicy&lt;TResult&gt;</c> 返回的是按 <c>TResult</c> 适配的
    /// <c>IAsyncPolicy&lt;TResult&gt;</c> 包装器，因此不同 <c>TResult</c> 的<b>引用并不相同</b>
    /// （Polly 的 <c>AsAsyncPolicy&lt;TResult&gt;()</c> 会生成适配包装），
    /// 但包装器委托到的底层 <c>AsyncCircuitBreakerPolicy</c> 是同一实例，故计数仍合并。
    /// </remarks>
    [Fact]
    public async Task CircuitBreakerPolicy_SameScope_DifferentResultTypes_MergesFailureCount()
    {
        var options = new ResilienceOptions
        {
            CircuitBreaker = { Enabled = true, FailureThreshold = 2, BreakDurationSeconds = 30, SamplingDurationSeconds = 0 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var forMessage = provider.GetCircuitBreakerPolicy<HttpResponseMessage>("hc22-merge");
        var forObject = provider.GetCircuitBreakerPolicy<object>("hc22-merge");

        Func<Task<HttpResponseMessage>> failMessage = () => throw new HttpRequestException("boom");
        Func<Task<object>> failObject = () => throw new HttpRequestException("boom");

        // 前两次失败落在 HttpResponseMessage 策略上（阈值 2），第 2 次触发熔断
        var act1 = async () => await forMessage.ExecuteAsync(failMessage);
        await act1.Should().ThrowAsync<HttpRequestException>();

        var act2 = async () => await forMessage.ExecuteAsync(failMessage);
        await act2.Should().ThrowAsync<HttpRequestException>();

        // 计数若未合并，object 策略会以全新熔断器接受请求并抛 HttpRequestException；
        // 合并后共享熔断器已 Open，直接拒绝。
        var act3 = async () => await forObject.ExecuteAsync(failObject);
        await act3.Should().ThrowAsync<BrokenCircuitException>();
    }

    /// <summary>
    /// M6-HC-22：Retry 策略键仍保留 <c>ResultType</c>，同 scope 不同结果类型必须是不同实例。
    /// </summary>
    [Fact]
    public void RetryPolicy_SameScope_DifferentResultTypes_RemainDistinct()
    {
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 2, DelayMilliseconds = 1 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        var forMessage = provider.GetRetryPolicy<HttpResponseMessage>("hc22-retry");
        var forObject = provider.GetRetryPolicy<object>("hc22-retry");

        forMessage.Should().NotBeSameAs(forObject);
    }

    /// <summary>
    /// M6-HC-22：策略缓存超上限改为按插入序淘汰（每轮 <c>max/8</c> 条），
    /// 不再整体放弃缓存。断言：超限后重新解析仍返回策略，且共享熔断器的失败计数未丢失。
    /// </summary>
    [Fact]
    public async Task CircuitBreakerPolicy_CacheOverLimit_EvictsOldestButKeepsBreakerState()
    {
        var options = new ResilienceOptions
        {
            MaxPolicyCacheSize = 8,
            CircuitBreaker = { Enabled = true, FailureThreshold = 2, BreakDurationSeconds = 30, SamplingDurationSeconds = 0 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        Func<Task<HttpResponseMessage>> fail = () => throw new HttpRequestException("boom");

        // 首个 scope 记 1 次失败（阈值 2，尚未熔断）
        var first = provider.GetCircuitBreakerPolicy<HttpResponseMessage>("hc22-evict-0");
        var act1 = async () => await first.ExecuteAsync(fail);
        await act1.Should().ThrowAsync<HttpRequestException>();

        // 填充超过容量上限（8），触发多轮淘汰（最旧的 hc22-evict-0 会被逐出缓存）
        for (var i = 1; i <= 12; i++)
        {
            provider.GetCircuitBreakerPolicy<HttpResponseMessage>($"hc22-evict-{i}").Should().NotBeNull();
        }

        // 重新解析原 scope：不得返回 null / 抛异常
        var again = provider.GetCircuitBreakerPolicy<HttpResponseMessage>("hc22-evict-0");
        again.Should().NotBeNull();

        // 共享熔断器状态仍在：再 1 次失败即达阈值 2 并打开 → 下一次调用被拒绝。
        // 若状态随淘汰丢失，第 2 次失败只会是 HttpRequestException（计数从头开始）。
        var act2 = async () => await again.ExecuteAsync(fail);
        await act2.Should().ThrowAsync<HttpRequestException>();

        var act3 = async () => await again.ExecuteAsync(fail);
        await act3.Should().ThrowAsync<BrokenCircuitException>();
    }

    /// <summary>
    /// M6-HC-22：共享策略册虽与适配器册分册（以保住熔断状态），但仍须有容量兜底，
    /// 否则 scope 基数异常膨胀时会无界增长。断言：越界后最旧的共享熔断器被淘汰（计数从头开始）。
    /// </summary>
    [Fact]
    public async Task SharedPolicyCache_ScopeCardinalityBeyondFloor_EvictsOldestBreakerState()
    {
        var options = new ResilienceOptions
        {
            MaxPolicyCacheSize = 8,
            CircuitBreaker = { Enabled = true, FailureThreshold = 2, BreakDurationSeconds = 30, SamplingDurationSeconds = 0 }
        };
        var provider = new PollyResiliencePolicyProvider(options);

        Func<Task<HttpResponseMessage>> fail = () => throw new HttpRequestException("boom");

        // 首个 scope 记 1 次失败（阈值 2，尚未熔断）
        var first = provider.GetCircuitBreakerPolicy<HttpResponseMessage>("hc22-bound-0");
        var act1 = async () => await first.ExecuteAsync(fail);
        await act1.Should().ThrowAsync<HttpRequestException>();

        // 越过共享册下限（256）：最旧的 hc22-bound-0 共享条目被兜底淘汰
        for (var i = 1; i <= 300; i++)
        {
            provider.GetCircuitBreakerPolicy<HttpResponseMessage>($"hc22-bound-{i}").Should().NotBeNull();
        }

        // 重新解析拿到全新熔断器（计数 0）：第 2 次失败仍为 HttpRequestException，
        // 而非达阈值后的 BrokenCircuitException —— 证明共享册确实做了有界回收。
        var again = provider.GetCircuitBreakerPolicy<HttpResponseMessage>("hc22-bound-0");
        var act2 = async () => await again.ExecuteAsync(fail);
        await act2.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion
}
