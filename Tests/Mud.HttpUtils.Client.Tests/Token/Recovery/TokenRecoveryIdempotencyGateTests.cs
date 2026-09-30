// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// R-P1-04：401 恢复的幂等门控 —— 默认不再重放非幂等方法，逃生门两条。
/// </summary>
/// <remarks>
/// 2.0.x 对任意方法（含 POST/PATCH）自动重放 401 请求，可能造成重复下单 / 重复扣款等业务副作用。
/// 本组用例固定三条语义：① 默认拒绝；② 全局开关放行；③ 契约级精确放行；
/// ④ 白名单本身（GET/HEAD/OPTIONS/TRACE/PUT/DELETE 放行，POST/PATCH 拒绝）。
/// </remarks>
public class TokenRecoveryIdempotencyGateTests
{
    private static Mock<ITokenManager> CreateAlwaysValidManager(Action? onRefresh = null)
    {
        var mock = new Mock<ITokenManager>();
        var setup = mock.Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()));
        if (onRefresh != null)
            setup.Callback(onRefresh);
        setup.ReturnsAsync("refreshed-token");
        return mock;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method, TokenRecoveryContext? context = null)
    {
        var request = new HttpRequestMessage(method, "https://api.example.com/items");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "stale-token");

        if (context != null)
        {
            request.Options.Set(
                new HttpRequestOptionsKey<TokenRecoveryContext>(TokenRecoveryContext.PropertyKey),
                context);
        }

        return request;
    }

    /// <summary>发一次请求并返回(是否刷新、发送次数、最终状态码)。</summary>
    private static async Task<(int RefreshCount, int SendCount, HttpStatusCode Status)> SendAsync(
        TokenRecoveryOptions? options, HttpMethod method, TokenRecoveryContext? context = null)
    {
        var refreshCount = 0;
        var manager = CreateAlwaysValidManager(() => Interlocked.Increment(ref refreshCount));
        var executor = new TokenRecoveryExecutor(manager.Object, options);

        var sendCount = 0;
        var response = await executor.ExecuteAsync(
            CreateRequest(method, context),
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                var auth = req.Headers.Authorization?.Parameter;
                return Task.FromResult(auth == "refreshed-token"
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    : new HttpResponseMessage(HttpStatusCode.Unauthorized));
            },
            CancellationToken.None);

        var status = response.StatusCode;
        response.Dispose();

        return (refreshCount, sendCount, status);
    }

    [Fact]
    public async Task Post_401_ShouldNotRetry_ByDefault()
    {
        var (refreshCount, sendCount, status) = await SendAsync(null, HttpMethod.Post);

        refreshCount.Should().Be(0, "默认不对非幂等方法执行 401 重放");
        sendCount.Should().Be(1, "仍正常发送一次并返回真实 401");
        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("POST")]
    public async Task NonIdempotent_401_ShouldNotRetry_ByDefault(string methodName)
    {
        var (refreshCount, sendCount, status) =
            await SendAsync(null, new HttpMethod(methodName));

        refreshCount.Should().Be(0);
        sendCount.Should().Be(1);
        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_401_ShouldRetry_WhenAllowNonIdempotentRecoveryEnabled()
    {
        var (refreshCount, sendCount, status) = await SendAsync(
            new TokenRecoveryOptions { AllowNonIdempotentRecovery = true },
            HttpMethod.Post);

        refreshCount.Should().Be(1, "全局逃生门放行后恢复 2.0.x 行为");
        sendCount.Should().Be(2, "首次 + 重试");
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_401_ShouldRetry_WhenContextExplicitlyAllows()
    {
        var (refreshCount, sendCount, status) = await SendAsync(
            null,
            HttpMethod.Post,
            new TokenRecoveryContext { IsRetryAllowedExplicitly = true });

        refreshCount.Should().Be(1, "契约级放行仅对确属幂等的 POST 开洞");
        sendCount.Should().Be(2);
        status.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Idempotent_401_ShouldRetry_ByDefault(string methodName)
    {
        var (refreshCount, _, status) = await SendAsync(null, new HttpMethod(methodName));

        refreshCount.Should().Be(1, "幂等方法默认允许恢复");
        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void RetryableMethodPolicy_ShouldBeIdempotentWhitelist()
    {
        // 断言顺序与语义（不做跨程序集反射比对：P1 阶段 Resilience 侧白名单刻意并存）。
        RetryableMethodPolicy.IsRetryable(HttpMethod.Get).Should().BeTrue();
        RetryableMethodPolicy.IsRetryable(HttpMethod.Head).Should().BeTrue();
        RetryableMethodPolicy.IsRetryable(HttpMethod.Options).Should().BeTrue();
        RetryableMethodPolicy.IsRetryable(HttpMethod.Trace).Should().BeTrue();
        RetryableMethodPolicy.IsRetryable(HttpMethod.Put).Should().BeTrue();
        RetryableMethodPolicy.IsRetryable(HttpMethod.Delete).Should().BeTrue();

        RetryableMethodPolicy.IsRetryable(HttpMethod.Post).Should().BeFalse("POST 非幂等");
        RetryableMethodPolicy.IsRetryable(new HttpMethod("PATCH")).Should().BeFalse("PATCH 非幂等");
        RetryableMethodPolicy.IsRetryable(null).Should().BeFalse("null 不得放行");
    }

    [Fact]
    public void RetryableMethodPolicy_ShouldCompareByMethodName()
    {
        // HttpMethod 的相等性按方法名（大小写不敏感）比较，故自定义实例与静态实例等价。
        RetryableMethodPolicy.IsRetryable(new HttpMethod("GET")).Should().BeTrue();
        RetryableMethodPolicy.IsRetryable(new HttpMethod("get")).Should().BeTrue(
            "与 System.Net.Http.HttpMethod 自身的相等语义保持一致（OrdinalIgnoreCase）");
        RetryableMethodPolicy.IsRetryable(new HttpMethod("CONNECT")).Should().BeFalse();
    }
}
