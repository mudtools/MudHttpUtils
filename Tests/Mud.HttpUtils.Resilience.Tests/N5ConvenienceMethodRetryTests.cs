using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// N-5 + 便捷方法路径 M2-#12/M2-#10 回归锁定。
/// </summary>
/// <remarks>
/// <para><b>N-5</b>：便捷方法（GetAsync/PostAsJsonAsync 等）闭包每次重试都会 re-invoke executeFunc
/// → 内部重新构建请求（<c>SendJsonWithBodyAsync</c> 每次 <c>new HttpRequestMessage</c>），
/// 不存在"共享已发送请求"问题 —— 本组用例锁定该语义，防止未来改为克隆/复用单实例请求。</para>
/// <para><b>M2-#12（便捷路径补口）</b>：非幂等方法（POST/PATCH）便捷方法默认不重试（防重复提交），
/// <c>AllowNonIdempotentRetry = true</c> 时恢复重试。</para>
/// <para><b>M2-#10（便捷路径补口）</b>：超时/熔断异常在策略边界外汇一为 <see cref="ApiRequestException"/>。</para>
/// </remarks>
public class N5ConvenienceMethodRetryTests
{
    private static ResilienceOptions RetryOptions(int maxRetries = 3, int delayMs = 1) => new()
    {
        Retry = { Enabled = true, MaxRetryAttempts = maxRetries, DelayMilliseconds = delayMs }
    };

    /// <summary>N-5：幂等便捷方法（GET）在重试策略下 re-invoke 闭包 → 重建请求，第二次成功。</summary>
    [Fact]
    public async Task GetAsync_RetryRebuildsRequest_SucceedsAfterRetry()
    {
        var innerCallCount = 0;
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((uri, ct) =>
            {
                innerCallCount++;
                return innerCallCount >= 2
                    ? Task.FromResult<string?>("success")
                    : Task.FromException<string?>(new HttpRequestException("Connection refused"));
            });

        var policyProvider = new PollyResiliencePolicyProvider(RetryOptions());
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var result = await client.GetAsync<string>("https://api.example.com/data");

        result.Should().Be("success");
        innerCallCount.Should().Be(2, "重试经闭包 re-invoke（重建请求），而非复用同一请求实例");
        mockInner.Verify(c => c.GetAsync<string>("https://api.example.com/data", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>M2-#12 便捷路径补口：POST 便捷方法默认不重试（只调用一次，异常直接抛出）。</summary>
    [Fact]
    public async Task PostAsJsonAsync_NonIdempotent_DoesNotRetryByDefault()
    {
        var innerCallCount = 0;
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.PostAsJsonAsync<string, string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((uri, data, ct) =>
            {
                innerCallCount++;
                return Task.FromException<string?>(new HttpRequestException("Connection refused"));
            });

        var policyProvider = new PollyResiliencePolicyProvider(RetryOptions(maxRetries: 3));
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var act = () => client.PostAsJsonAsync<string, string>("https://api.example.com/orders", "payload");

        await act.Should().ThrowAsync<HttpRequestException>();
        innerCallCount.Should().Be(1, "非幂等方法默认不重试（防重复提交），退化为超时+熔断");
    }

    /// <summary>M2-#12 便捷路径补口：AllowNonIdempotentRetry = true 时 POST 恢复重试。</summary>
    [Fact]
    public async Task PostAsJsonAsync_AllowNonIdempotentRetry_Retries()
    {
        var innerCallCount = 0;
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.PostAsJsonAsync<string, string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((uri, data, ct) =>
            {
                innerCallCount++;
                return innerCallCount >= 2
                    ? Task.FromResult<string?>("ok")
                    : Task.FromException<string?>(new HttpRequestException("Connection refused"));
            });

        var options = RetryOptions(maxRetries: 3);
        options.Retry.AllowNonIdempotentRetry = true;
        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider, options: options);

        var result = await client.PostAsJsonAsync<string, string>("https://api.example.com/orders", "payload");

        result.Should().Be("ok");
        innerCallCount.Should().Be(2);
    }

    /// <summary>M2-#12 便捷路径补口：幂等方法（DELETE）便捷方法默认可重试。</summary>
    [Fact]
    public async Task DeleteAsJsonAsync_Idempotent_RetriesByDefault()
    {
        var innerCallCount = 0;
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.DeleteAsJsonAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((uri, ct) =>
            {
                innerCallCount++;
                return innerCallCount >= 2
                    ? Task.FromResult<string?>("deleted")
                    : Task.FromException<string?>(new HttpRequestException("Connection refused"));
            });

        var policyProvider = new PollyResiliencePolicyProvider(RetryOptions());
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var result = await client.DeleteAsJsonAsync<string>("https://api.example.com/orders/1");

        result.Should().Be("deleted");
        innerCallCount.Should().Be(2, "DELETE 属于默认幂等方法集合，正常重试");
    }

    /// <summary>M2-#10 便捷路径补口：便捷方法超时 → TimeoutRejectedException 归一为 ApiRequestException（IsTimeout）。</summary>
    [Fact]
    public async Task GetAsync_TimeoutExceeded_NormalizedToApiRequestException()
    {
        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner
            .Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((uri, ct) => new TaskCompletionSource<string?>().Task);

        var options = new ResilienceOptions
        {
            Retry = { Enabled = false },
            Timeout = { Enabled = true, TimeoutSeconds = 1 }
        };
        var policyProvider = new PollyResiliencePolicyProvider(options);
        var client = new ResilientHttpClient(mockInner.Object, policyProvider);

        var act = () => client.GetAsync<string>("https://api.example.com/slow");

        var ex = await act.Should().ThrowAsync<ApiRequestException>();
        ex.Which.IsTimeout.Should().BeTrue("便捷路径同样要把 TimeoutRejectedException 归一，Polly 类型不得外泄");
        ex.Which.Message.Should().Contain("https://api.example.com/slow");
    }
}
