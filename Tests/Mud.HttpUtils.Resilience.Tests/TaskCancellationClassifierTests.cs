// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Polly.CircuitBreaker;
using Polly.Timeout;
using Mud.HttpUtils.Resilience;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// M4-H-3：平台超时/用户取消 TCE 形态判定与异常归一化测试。
/// 验证 net5+ 下 <see cref="HttpClient.Timeout"/> 触发的 TCE（inner 为 <see cref="TimeoutException"/>）
/// 能被识别并归一为 isTimeout 异常，而用户取消不被误归一。T-3.x 验收。
/// </summary>
public class TaskCancellationClassifierTests
{
    #region IsPlatformTimeout（Polly 过滤谓词所用形态判定）

    [Fact]
    public void IsPlatformTimeout_WithInnerTimeoutException_ShouldReturnTrue()
    {
        // net5+ 平台超时特征：inner 为 TimeoutException
        var ex = new TaskCanceledException("timeout", new TimeoutException("The operation has timed out."));
        TaskCancellationClassifier.IsPlatformTimeout(ex).Should().BeTrue();
    }

    [Fact]
    public void IsPlatformTimeout_WithInnerTaskCanceledException_ShouldReturnFalse()
    {
        // 用户取消特征：inner 为 TaskCanceledException（非 TimeoutException）
        var inner = new TaskCanceledException("user cancel");
        var ex = new TaskCanceledException("cancelled", inner);
        TaskCancellationClassifier.IsPlatformTimeout(ex).Should().BeFalse();
    }

    [Fact]
    public void IsPlatformTimeout_WithNoInner_ShouldReturnFalse()
    {
        var ex = new TaskCanceledException("cancelled");
        TaskCancellationClassifier.IsPlatformTimeout(ex).Should().BeFalse();
    }

    [Fact]
    public void IsPlatformTimeout_WithNonTceType_ShouldReturnFalse()
    {
        TaskCancellationClassifier.IsPlatformTimeout(new InvalidOperationException()).Should().BeFalse();
    }

    #endregion

    #region Classify（调用方 token 可达处的三态归类）

    [Fact]
    public void Classify_UserTokenTriggered_ShouldReturnUserCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new TaskCanceledException("timeout", new TimeoutException());
        TaskCancellationClassifier.Classify(ex, cts.Token)
            .Should().Be(TaskCancellationClassifier.Category.UserCancelled);
    }

    [Fact]
    public void Classify_PlatformTimeoutShape_ShouldReturnPlatformTimeout()
    {
        var ex = new TaskCanceledException("timeout", new TimeoutException());
        TaskCancellationClassifier.Classify(ex, CancellationToken.None)
            .Should().Be(TaskCancellationClassifier.Category.PlatformTimeout);
    }

    [Fact]
    public void Classify_OtherShape_ShouldReturnOther()
    {
        var ex = new TaskCanceledException("cancelled");
        TaskCancellationClassifier.Classify(ex, CancellationToken.None)
            .Should().Be(TaskCancellationClassifier.Category.Other);
    }

    #endregion

    #region PollyExceptionNormalizer.TryNormalize（策略边界外归一化）

    [Fact]
    public void TryNormalize_PlatformTimeout_ShouldReturnTimeoutApiException()
    {
        var ex = new TaskCanceledException("timeout", new TimeoutException());
        var normalized = PollyExceptionNormalizer.TryNormalize(ex, "http://host/api", default);
        normalized.Should().NotBeNull();
        normalized!.IsTimeout.Should().BeTrue();
        normalized.Message.Should().Contain("平台 HttpClient.Timeout");
    }

    [Fact]
    public void TryNormalize_UserCancelled_ShouldReturnNull()
    {
        // 用户取消不应被归一（保持 OperationCanceled 语义）；此处 userToken 已触发。
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = new TaskCanceledException("cancelled", new OperationCanceledException(cts.Token));
        PollyExceptionNormalizer.TryNormalize(ex, "http://host/api", cts.Token).Should().BeNull();
    }

    [Fact]
    public void TryNormalize_OtherCancellation_ShouldReturnNull()
    {
        var ex = new TaskCanceledException("cancelled");
        PollyExceptionNormalizer.TryNormalize(ex, "http://host/api", default).Should().BeNull();
    }

    [Fact]
    public void TryNormalize_ResilienceOutcome_ShouldPreserveExistingSemantics()
    {
        // 既有语义：TimeoutRejectedException / BrokenCircuitException 归一逻辑不应被 H-3 破坏。
        var timeout = PollyExceptionNormalizer.TryNormalize(
            new TimeoutRejectedException(),
            "http://host/api", default);
        timeout.Should().NotBeNull();
        timeout!.IsTimeout.Should().BeTrue();

        var broken = PollyExceptionNormalizer.TryNormalize(
            new BrokenCircuitException("b"), "http://host/api", default);
        broken.Should().NotBeNull();
        broken!.IsCircuitOpen.Should().BeTrue();
    }

    #endregion
}