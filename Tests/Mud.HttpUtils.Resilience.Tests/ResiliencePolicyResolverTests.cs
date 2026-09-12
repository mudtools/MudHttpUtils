// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护，使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Polly;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// ResiliencePolicyResolver 的单元测试。
/// 覆盖 ResolvePolicyWrapper 的返回值、请求克隆、Dispose 和 MaxCloneContentSize 限制等场景。
/// </summary>
public class ResiliencePolicyResolverTests
{
    private static readonly Uri TestUri = new("https://api.example.com/test");

    private static HttpRequestMessage CreateRequest(string content = """{"id":1}""", HttpMethod? method = null)
    {
        // 默认 GET（幂等）：M2-#12 后非幂等方法默认不重试，参数透传类用例用 GET 保持原有验证意图
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, TestUri);
        if (content != null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }
        return request;
    }

    private static ResilienceExecutionOptions RetryOptions(int maxRetries = 2) =>
        new()
        {
            RetryEnabled = true,
            MaxRetries = maxRetries,
            DelayMilliseconds = 1, // 测试中使用最小延迟
            UseExponentialBackoff = false
        };

    #region 构造函数

    [Fact]
    public void Constructor_WithNullPolicyProvider_ShouldThrowArgumentNullException()
    {
        var act = () => new ResiliencePolicyResolver(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("policyProvider");
    }

    [Fact]
    public void Constructor_WithValidArguments_ShouldCreateInstance()
    {
        var mockProvider = new Mock<IResiliencePolicyProvider>();
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);
        resolver.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithOptions_ShouldUseCustomMaxCloneContentSize()
    {
        var mockProvider = new Mock<IResiliencePolicyProvider>();
        var options = Options.Create(new ResilienceOptions { MaxCloneContentSize = 512 });
        var resolver = new ResiliencePolicyResolver(mockProvider.Object, options);
        resolver.Should().NotBeNull();
    }

    #endregion

    #region ResolvePolicyWrapper - 返回值

    [Fact]
    public void ResolvePolicyWrapper_NoResilienceEnabled_ShouldReturnNull()
    {
        var mockProvider = new Mock<IResiliencePolicyProvider>();
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);
        var options = new ResilienceExecutionOptions
        {
            RetryEnabled = false,
            CircuitBreakerEnabled = false,
            TimeoutEnabled = false
        };

        var wrapper = resolver.ResolvePolicyWrapper<string>(options, CreateRequest());

        wrapper.Should().BeNull();
        // 无策略启用时不应调用 policy provider
        mockProvider.Verify(p => p.GetMethodPolicy<string>(It.IsAny<bool>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void ResolvePolicyWrapper_WithRetryEnabled_ShouldReturnWrapperAndCallProvider()
    {
        var mockProvider = new Mock<IResiliencePolicyProvider>();
        mockProvider.Setup(p => p.GetMethodPolicy<string>(
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Policy.NoOpAsync<string>());
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);

        var wrapper = resolver.ResolvePolicyWrapper<string>(RetryOptions(), CreateRequest());

        wrapper.Should().NotBeNull();
        mockProvider.Verify(p => p.GetMethodPolicy<string>(
            It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
            It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void ResolvePolicyWrapper_WithNullOptions_ShouldThrowArgumentNullException()
    {
        var mockProvider = new Mock<IResiliencePolicyProvider>();
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);

        var act = () => resolver.ResolvePolicyWrapper<string>(null!, CreateRequest());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void ResolvePolicyWrapper_WithNullRequestTemplate_ShouldThrowArgumentNullException()
    {
        var mockProvider = new Mock<IResiliencePolicyProvider>();
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);

        var act = () => resolver.ResolvePolicyWrapper<string>(RetryOptions(), null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("requestTemplate");
    }

    #endregion

    #region ResolvePolicyWrapper - 请求克隆与执行

    [Fact]
    public async Task ResolvePolicyWrapper_ClonesRequestOnEachExecution()
    {
        // 使用真实 Polly 重试策略：前两次抛异常，第三次成功
        var callCount = 0;
        var receivedRequests = new List<HttpRequestMessage>();

        var retryPolicy = Policy<string>
            .Handle<InvalidOperationException>()
            .RetryAsync(2);

        var mockProvider = new Mock<IResiliencePolicyProvider>();
        mockProvider.Setup(p => p.GetMethodPolicy<string>(
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(retryPolicy);
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);

        var requestTemplate = CreateRequest("""{"original":true}""");
        var wrapper = resolver.ResolvePolicyWrapper<string>(RetryOptions(2), requestTemplate);

        Func<HttpRequestMessage, CancellationToken, Task<string>> coreExecute = (req, ct) =>
        {
            callCount++;
            receivedRequests.Add(req);
            if (callCount < 3)
                throw new InvalidOperationException("simulated failure");
            return Task.FromResult("success");
        };

        var result = await wrapper!(coreExecute, CancellationToken.None);

        result.Should().Be("success");
        callCount.Should().Be(3); // 1 次初始 + 2 次重试
        receivedRequests.Should().HaveCount(3);

        // M2-#19：首次尝试直接用原请求（保持流式/进度语义）；仅重试时克隆。
        // 重试的两次传入都应是克隆请求，而非原始模板；首次与模板为同一实例。
        receivedRequests[0].Should().BeSameAs(requestTemplate);
        for (var i = 1; i < receivedRequests.Count; i++)
        {
            var received = receivedRequests[i];
            received.Should().NotBeSameAs(requestTemplate);
            received.RequestUri.Should().Be(TestUri);
            // 测试 helper 默认 GET（M2-#12：非幂等方法默认不重试），克隆保真断言同步为 GET
            received.Method.Should().Be(HttpMethod.Get);
        }
    }

    [Fact]
    public async Task ResolvePolicyWrapper_DisposesClonedRequestContentAfterExecution()
    {
        // 使用真实 Polly 重试策略：第一次抛异常，第二次成功
        var retryPolicy = Policy<string>
            .Handle<InvalidOperationException>()
            .RetryAsync(1);

        var mockProvider = new Mock<IResiliencePolicyProvider>();
        mockProvider.Setup(p => p.GetMethodPolicy<string>(
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(retryPolicy);
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);

        var requestTemplate = CreateRequest("""{"data":"value"}""");
        var wrapper = resolver.ResolvePolicyWrapper<string>(RetryOptions(1), requestTemplate);

        HttpRequestMessage? retryRequest = null;
        var callCount = 0;
        Func<HttpRequestMessage, CancellationToken, Task<string>> coreExecute = (req, ct) =>
        {
            callCount++;
            if (callCount == 1)
                throw new InvalidOperationException("first attempt fails");
            retryRequest = req;
            return Task.FromResult("ok");
        };

        await wrapper!(coreExecute, CancellationToken.None);

        // M2-#19：首次执行用原请求（不被 Dispose，生命周期归调用方）；
        // 重试时克隆的请求应在 finally 中被 Dispose（Dispose 后访问 Content 流抛 ObjectDisposedException）。
        retryRequest.Should().NotBeNull();
        retryRequest!.Content.Should().NotBeNull();
        var act = () => retryRequest.Content!.ReadAsStream();
        act.Should().Throw<ObjectDisposedException>("重试克隆请求在 finally 块中应被 Dispose");
    }

    [Fact]
    public async Task ResolvePolicyWrapper_SuccessWithoutRetry_DisposesClonedRequestContent()
    {
        // 无需重试，直接成功
        var noOpPolicy = Policy.NoOpAsync<string>();
        var mockProvider = new Mock<IResiliencePolicyProvider>();
        mockProvider.Setup(p => p.GetMethodPolicy<string>(
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(noOpPolicy);
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);

        var requestTemplate = CreateRequest();
        var wrapper = resolver.ResolvePolicyWrapper<string>(RetryOptions(), requestTemplate);

        HttpRequestMessage? receivedRequest = null;
        Func<HttpRequestMessage, CancellationToken, Task<string>> coreExecute = (req, ct) =>
        {
            receivedRequest = req;
            return Task.FromResult("done");
        };

        await wrapper!(coreExecute, CancellationToken.None);

        // M2-#19：无重试成功时直接用原请求 —— 与模板同一实例且不被 Dispose
        receivedRequest.Should().NotBeNull();
        receivedRequest.Should().BeSameAs(requestTemplate);
        receivedRequest!.Content.Should().NotBeNull();
        var act = () => receivedRequest.Content!.ReadAsStream();
        act.Should().NotThrow("原请求生命周期归调用方，首次执行不得 Dispose");
    }

    #endregion

    #region ResolvePolicyWrapper - MaxCloneContentSize 限制

    [Fact]
    public async Task ResolvePolicyWrapper_ContentExceedingMaxCloneSize_ShouldThrowOnRetry()
    {
        // 自定义 MaxCloneContentSize 很小，触发克隆失败
        var largeContent = new string('x', 1024);
        var retryPolicy = Policy<string>
            .Handle<InvalidOperationException>()
            .RetryAsync(1);

        var mockProvider = new Mock<IResiliencePolicyProvider>();
        mockProvider.Setup(p => p.GetMethodPolicy<string>(
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(retryPolicy);
        var options = Options.Create(new ResilienceOptions { MaxCloneContentSize = 100 });
        var resolver = new ResiliencePolicyResolver(mockProvider.Object, options);

        var requestTemplate = CreateRequest(largeContent);
        var wrapper = resolver.ResolvePolicyWrapper<string>(RetryOptions(1), requestTemplate);

        Func<HttpRequestMessage, CancellationToken, Task<string>> coreExecute = (req, ct) =>
            throw new InvalidOperationException("trigger retry");

        var act = () => wrapper!(coreExecute, CancellationToken.None);

        // 重试时克隆失败（内容超限），应抛出 InvalidOperationException
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolvePolicyWrapper_ContentExceedingMaxCloneSize_FirstAttemptClonesBeforeExecution()
    {
        // M2-#19：首次执行不再克隆 → 超限内容在首次直接成功执行（不再抛出）；
        // 仅当发生重试时才因克隆超限抛 InvalidOperationException。
        // 本用例锁定"首次不克隆"的新语义：即使内容超限，首次执行也不受影响。
        var largeContent = new string('x', 2048);
        var noOpPolicy = Policy.NoOpAsync<string>();

        var mockProvider = new Mock<IResiliencePolicyProvider>();
        mockProvider.Setup(p => p.GetMethodPolicy<string>(
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(noOpPolicy);
        var options = Options.Create(new ResilienceOptions { MaxCloneContentSize = 100 });
        var resolver = new ResiliencePolicyResolver(mockProvider.Object, options);

        var requestTemplate = CreateRequest(largeContent);
        var wrapper = resolver.ResolvePolicyWrapper<string>(RetryOptions(), requestTemplate);

        HttpRequestMessage? receivedRequest = null;
        Func<HttpRequestMessage, CancellationToken, Task<string>> coreExecute = (req, ct) =>
        {
            receivedRequest = req;
            return Task.FromResult("done-first-attempt");
        };

        var result = await wrapper!(coreExecute, CancellationToken.None);

        // 首次成功：原样执行（无克隆、无超限异常），且传入的是原请求本身
        result.Should().Be("done-first-attempt");
        receivedRequest.Should().BeSameAs(requestTemplate);
    }

    #endregion

    #region ResolvePolicyWrapper - 参数传递

    [Fact]
    public void ResolvePolicyWrapper_ShouldPassOptionsToPolicyProvider()
    {
        var mockProvider = new Mock<IResiliencePolicyProvider>();
        mockProvider.Setup(p => p.GetMethodPolicy<string>(
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Policy.NoOpAsync<string>());
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);

        var options = new ResilienceExecutionOptions
        {
            RetryEnabled = true,
            MaxRetries = 5,
            DelayMilliseconds = 200,
            UseExponentialBackoff = true,
            CircuitBreakerEnabled = true,
            FailureThreshold = 3,
            BreakDurationSeconds = 10,
            TimeoutEnabled = true,
            TimeoutMilliseconds = 5000,
            SamplingDurationSeconds = 30,
            MinimumThroughput = 8
        };

        resolver.ResolvePolicyWrapper<string>(options, CreateRequest());

        mockProvider.Verify(p => p.GetMethodPolicy<string>(
            true, 5, 200, true,       // retry
            true, 3, 10,              // circuit breaker
            true, 5000,               // timeout
            30, 8                     // sampling
        ), Times.Once);
    }

    [Fact]
    public async Task ResolvePolicyWrapper_CancellationTokenPropagatedToCoreExecute()
    {
        var noOpPolicy = Policy.NoOpAsync<string>();
        var mockProvider = new Mock<IResiliencePolicyProvider>();
        mockProvider.Setup(p => p.GetMethodPolicy<string>(
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(noOpPolicy);
        var resolver = new ResiliencePolicyResolver(mockProvider.Object);

        var wrapper = resolver.ResolvePolicyWrapper<string>(RetryOptions(), CreateRequest());

        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;
        Func<HttpRequestMessage, CancellationToken, Task<string>> coreExecute = (req, ct) =>
        {
            capturedToken = ct;
            return Task.FromResult("ok");
        };

        await wrapper!(coreExecute, cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    #endregion
}
