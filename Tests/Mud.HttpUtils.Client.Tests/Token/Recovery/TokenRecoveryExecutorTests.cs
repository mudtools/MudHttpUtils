// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// TokenRecoveryExecutor 的单元测试。
/// 重点验证 NEW-HC-10 修复：BuildRetryRequest 跳过 __mud_* 可观测性属性。
/// </summary>
public class TokenRecoveryExecutorTests
{
    private static TokenRecoveryExecutor CreateExecutor()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        return new TokenRecoveryExecutor(mockTokenManager.Object);
    }

    private static HttpRequestMessage InvokeBuildRetryRequest(
        HttpRequestMessage original,
        byte[]? contentBytes = null,
        TokenRecoveryContext? recoveryContext = null)
    {
        var method = typeof(TokenRecoveryExecutor).GetMethod(
            "BuildRetryRequest",
            BindingFlags.NonPublic | BindingFlags.Static);
        return (HttpRequestMessage)method!.Invoke(null, new object?[] { original, contentBytes, recoveryContext })!;
    }

    // ============================================================
    // NEW-HC-10：BuildRetryRequest 跳过 __mud_* 属性
    // ============================================================

    [Fact]
    public void BuildRetryRequest_ShouldSkipMudObservabilityProperties()
    {
        // Arrange：构造一个原始请求，包含 __mud_* 属性和普通属性
        var originalRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/api");
#pragma warning disable CS0618 // Properties 已过时但仍可用于测试
        originalRequest.Properties["__mud_client_name"] = "test-client";
        originalRequest.Properties["__mud_status_code"] = 401;
        originalRequest.Properties["__mud_content_length"] = 1024;
        originalRequest.Properties["custom_property"] = "should-be-copied";
        originalRequest.Properties["x-correlation-id"] = "abc-123";
#pragma warning restore CS0618

        // Act
        var retryRequest = InvokeBuildRetryRequest(originalRequest);

        // Assert：__mud_* 属性不应被复制，普通属性应被复制
#pragma warning disable CS0618
        retryRequest.Properties.Should().NotContainKey("__mud_client_name",
            "可观测性标记不应被复制到重试请求");
        retryRequest.Properties.Should().NotContainKey("__mud_status_code");
        retryRequest.Properties.Should().NotContainKey("__mud_content_length");
        retryRequest.Properties.Should().ContainKey("custom_property");
        retryRequest.Properties["custom_property"].Should().Be("should-be-copied");
        retryRequest.Properties.Should().ContainKey("x-correlation-id");
        retryRequest.Properties["x-correlation-id"].Should().Be("abc-123");
#pragma warning restore CS0618
    }

    [Fact]
    public void BuildRetryRequest_ShouldPreserveHttpMethodAndUri()
    {
        // Arrange
        var originalRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/api/resource")
        {
            Content = new StringContent("body content")
        };

        // Act
        var retryRequest = InvokeBuildRetryRequest(originalRequest);

        // Assert
        retryRequest.Method.Should().Be(HttpMethod.Post);
        retryRequest.RequestUri.Should().Be(new Uri("https://api.example.com/api/resource"));
    }

    /// <summary>
    /// SR-H2/H3（P1.4，D4）请求体"读取阶段限量 + 无体不重试"：Content-Length 超过上限（10MB）的
    /// 请求体应被跳过缓冲，且<b>不进行无体重试</b>——直接返回 401（数据完整性优先），
    /// 交由上层重试策略处理。
    /// </summary>
    [Fact]
    public async Task LargeBody_ShouldNotBufferTwice()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");
        var executor = new TokenRecoveryExecutor(mockTokenManager.Object);

        // 构造一个声明大小超过 10MB 的请求
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/upload");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old-token");
        request.Content = new ByteArrayContent(new byte[11 * 1024 * 1024]); // 11MB

        var sendCount = 0;

        var response = await executor.ExecuteAsync(
            request,
            (req, ct) =>
            {
                Interlocked.Increment(ref sendCount);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            },
            CancellationToken.None);

        // TMR-01 修订：超限请求仍正常发送一次，返回真实 401，不进入恢复重试
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sendCount.Should().Be(1, "超限请求仍正常发送一次（TMR-01：禁止重试 ≠ 禁止发送）");
        mockTokenManager.Verify(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()), Times.Never,
            "超限带体请求不进入恢复刷新链路");
        response.Dispose();
    }

    // ============================================================
    // M6-HC-23：401 恢复重试的 forceRefresh 语义
    // ============================================================

    /// <summary>
    /// 第 2 轮恢复必须绕过「结果复用窗口」：两次刷新拿到不同令牌，且最终发送所用令牌为第 2 次刷新的值。
    /// 首轮仍走 single-flight 去重（窗口内首次刷新）。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SecondRetry_ForceRefresh_RefreshesAgainWithNewToken()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        var refreshCount = 0;
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"token-{++refreshCount}");

        // 保留默认去重窗口（> 0）：验证强制刷新确实绕过窗口而非窗口恰好失效。
        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 2, RefreshDedupWindowSeconds = 30 };
        var executor = new TokenRecoveryExecutor(mockTokenManager.Object, options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old-token");

        var sentTokens = new List<string?>();
        var sendCount = 0;
        var response = await executor.ExecuteAsync(
            request,
            (req, _) =>
            {
                Interlocked.Increment(ref sendCount);
                sentTokens.Add(req.Headers.Authorization?.Parameter);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            },
            CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sendCount.Should().Be(3, "1 次首发送 + 2 次恢复重试");
        refreshCount.Should().Be(2, "第 2 轮强刷不得复用第 1 轮已完成的结果");
        sentTokens[1].Should().Be("token-1");
        sentTokens[2].Should().Be("token-2", "重试应使用第 2 次刷新得到的新令牌");
        response.Dispose();
    }

    /// <summary>
    /// 取消路径资源归还：在第 2 轮发送时取消 → 抛 <see cref="OperationCanceledException"/>，
    /// 且发送计数符合预期（不抛非预期异常）。
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RetrySendCancelled_ThrowsOperationCanceledException()
    {
        var mockTokenManager = new Mock<ITokenManager>();
        mockTokenManager
            .Setup(m => m.InvalidateTokenAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenResult.Empty);
        mockTokenManager
            .Setup(m => m.GetOrRefreshTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-token");

        var options = new TokenRecoveryOptions { RecoveryMaxRetries = 3 };
        var executor = new TokenRecoveryExecutor(mockTokenManager.Object, options);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/test");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "old-token");

        using var cts = new CancellationTokenSource();
        var sendCount = 0;

        var act = async () => await executor.ExecuteAsync(
            request,
            (_, ct) =>
            {
                var n = Interlocked.Increment(ref sendCount);
                if (n == 2)
                {
                    cts.Cancel();
                    ct.ThrowIfCancellationRequested();
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            },
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sendCount.Should().Be(2, "第 2 次发送（首轮重试）被取消，不再进入后续轮次");
    }
}
