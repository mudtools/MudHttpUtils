// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
// -----------------------------------------------------------------------

using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// M4-D/H-9：流式枚举"连接建立期"超时守卫（<see cref="TimeoutOptions.StreamConnectTimeoutSeconds"/>）。
/// </summary>
/// <remarks>
/// 守护目标：<see cref="ResilientHttpClient.SendAsAsyncEnumerable{TResult}"/> 的首次 MoveNextAsync
/// （连接建立 + 首个元素产出）不能被无限期挂起；首个元素一旦产出即解除计时，后续读取不受连接期限制。
/// </remarks>
public class StreamConnectTimeoutTests
{
    /// <summary>首元素长期不产出且连接期超时已配置 → OperationCanceledException 及时抛出。</summary>
    [Fact]
    public async Task FirstElementDelayed_BeyondConnectTimeout_ThrowsOperationCanceled()
    {
        var options = new ResilienceOptions
        {
            Timeout = new TimeoutOptions { StreamConnectTimeoutSeconds = 1 }
        };

        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendAsAsyncEnumerable<string>(
                It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns((HttpRequestMessage _, object? _, CancellationToken ct) =>
                DelayedStream<string>(TimeSpan.FromSeconds(10), ct));

        var client = new ResilientHttpClient(mockInner.Object, Mock.Of<IResiliencePolicyProvider>(), options: options);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var act = async () =>
        {
            await foreach (var _ in client.SendAsAsyncEnumerable<string>(request)) { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>连接在限期内建立（首元素产出）→ 读取期超过连接期限制也不被强制断路，完整遍历成功。</summary>
    [Fact]
    public async Task FirstElementYields_ThenReadExceedsConnectTimeout_CompletesNormally()
    {
        var options = new ResilienceOptions
        {
            Timeout = new TimeoutOptions { StreamConnectTimeoutSeconds = 1 }
        };

        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendAsAsyncEnumerable<string>(
                It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns((HttpRequestMessage _, object? _, CancellationToken ct) => FirstFastThenSlowStream(ct));

        var client = new ResilientHttpClient(mockInner.Object, Mock.Of<IResiliencePolicyProvider>(), options: options);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var items = new List<string>();
        await foreach (var item in client.SendAsAsyncEnumerable<string>(request))
        {
            items.Add(item);
        }

        items.Should().Equal("first", "second");
    }

    /// <summary>连接期超时未配置（默认 0 = 禁用）→ 首元素超时后仍正常产出（不做任何限制）。</summary>
    [Fact]
    public async Task ConnectTimeoutDisabled_BeyondTimeout_StillYields()
    {
        var options = new ResilienceOptions { Timeout = new TimeoutOptions { StreamConnectTimeoutSeconds = 0 } };

        var mockInner = new Mock<IEnhancedHttpClient>();
        mockInner.Setup(c => c.SendAsAsyncEnumerable<string>(
                It.IsAny<HttpRequestMessage>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns((HttpRequestMessage _, object? _, CancellationToken ct) =>
                DelayedStream<string>(TimeSpan.FromMilliseconds(150), ct));

        var client = new ResilientHttpClient(mockInner.Object, Mock.Of<IResiliencePolicyProvider>(), options: options);
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/stream");

        var items = new List<string>();
        await foreach (var item in client.SendAsAsyncEnumerable<string>(request))
        {
            items.Add(item);
        }

        // 禁用连接期超时：首元素按自身延迟正常产出，无 OCE
        items.Should().ContainSingle();
    }

    private static async IAsyncEnumerable<T> DelayedStream<T>(
        TimeSpan delay,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 连接建立阶段延迟：监听传入 token（守卫生效时 OCE 从这里抛出）
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        yield return (T)default!;
    }

    private static async IAsyncEnumerable<string> FirstFastThenSlowStream(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return "first";                                                    // 首元素快速产出 → 解除连接期计时
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false); // 读取期 > 连接期限制
        yield return "second";
    }
}