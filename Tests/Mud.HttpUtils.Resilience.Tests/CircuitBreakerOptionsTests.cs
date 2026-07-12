// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// CircuitBreakerOptions 范围校验测试。
/// </summary>
public class CircuitBreakerOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new CircuitBreakerOptions();

        options.Enabled.Should().BeFalse();
        options.FailureThreshold.Should().Be(5);
        options.BreakDurationSeconds.Should().Be(30);
        options.SamplingDurationSeconds.Should().Be(0);
        options.MinimumThroughput.Should().Be(10);
    }

    [Fact]
    public void FailureThreshold_SetToZero_Throws()
    {
        var act = () => new CircuitBreakerOptions { FailureThreshold = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FailureThreshold_SetToNegative_Throws()
    {
        var act = () => new CircuitBreakerOptions { FailureThreshold = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FailureThreshold_SetToPositive_Succeeds()
    {
        var options = new CircuitBreakerOptions { FailureThreshold = 10 };
        options.FailureThreshold.Should().Be(10);
    }

    [Fact]
    public void FailureThreshold_SetToOver100_WhenSamplingDurationIsPositive_Throws()
    {
        // 先设置 SamplingDurationSeconds > 0，然后设置 FailureThreshold > 100
        var options = new CircuitBreakerOptions
        {
            SamplingDurationSeconds = 10
        };

        var act = () => options.FailureThreshold = 101;
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*1-100*");
    }

    [Fact]
    public void FailureThreshold_SetTo100_WhenSamplingDurationIsPositive_Succeeds()
    {
        var options = new CircuitBreakerOptions
        {
            SamplingDurationSeconds = 10,
            FailureThreshold = 100
        };

        options.FailureThreshold.Should().Be(100);
    }

    [Fact]
    public void SamplingDurationSeconds_SetToOverZero_WhenFailureThresholdOver100_Throws()
    {
        // 先设置 FailureThreshold > 100（在简单模式下允许），然后切换到高级模式
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 150 // 简单模式下允许 > 100（连续失败次数）
        };

        var act = () => options.SamplingDurationSeconds = 10;
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*1-100*");
    }

    [Fact]
    public void SamplingDurationSeconds_SetToNegative_Throws()
    {
        var act = () => new CircuitBreakerOptions { SamplingDurationSeconds = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SamplingDurationSeconds_SetToZero_Succeeds()
    {
        var options = new CircuitBreakerOptions { SamplingDurationSeconds = 0 };
        options.SamplingDurationSeconds.Should().Be(0);
    }

    [Fact]
    public void SamplingDurationSeconds_SetToPositive_Succeeds()
    {
        var options = new CircuitBreakerOptions
        {
            FailureThreshold = 50, // 先设为 <= 100
            SamplingDurationSeconds = 30
        };
        options.SamplingDurationSeconds.Should().Be(30);
    }

    [Fact]
    public void BreakDurationSeconds_SetToZero_Throws()
    {
        var act = () => new CircuitBreakerOptions { BreakDurationSeconds = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BreakDurationSeconds_SetToNegative_Throws()
    {
        var act = () => new CircuitBreakerOptions { BreakDurationSeconds = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BreakDurationSeconds_SetToPositive_Succeeds()
    {
        var options = new CircuitBreakerOptions { BreakDurationSeconds = 60 };
        options.BreakDurationSeconds.Should().Be(60);
    }

    [Fact]
    public void FailureThreshold_CanExceed100_WhenSamplingDurationIsZero()
    {
        // 在简单熔断模式下，FailureThreshold 表示连续失败次数，可以 > 100
        var options = new CircuitBreakerOptions
        {
            SamplingDurationSeconds = 0,
            FailureThreshold = 200
        };
        options.FailureThreshold.Should().Be(200);
    }

    [Fact]
    public void FullConfiguration_AdvancedMode_ValidatesCorrectly()
    {
        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 50, // 百分比 50%
            BreakDurationSeconds = 60,
            SamplingDurationSeconds = 30,
            MinimumThroughput = 20
        };

        options.Enabled.Should().BeTrue();
        options.FailureThreshold.Should().Be(50);
        options.BreakDurationSeconds.Should().Be(60);
        options.SamplingDurationSeconds.Should().Be(30);
        options.MinimumThroughput.Should().Be(20);
    }

    [Fact]
    public void FullConfiguration_SimpleMode_ValidatesCorrectly()
    {
        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 10, // 连续失败 10 次
            BreakDurationSeconds = 30,
            SamplingDurationSeconds = 0
        };

        options.Enabled.Should().BeTrue();
        options.FailureThreshold.Should().Be(10);
        options.BreakDurationSeconds.Should().Be(30);
        options.SamplingDurationSeconds.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void MinimumThroughput_SetToLessThanTwo_ThrowsArgumentOutOfRangeException(int invalidValue)
    {
        var options = new CircuitBreakerOptions();
        var act = () => options.MinimumThroughput = invalidValue;
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*必须 >= 2*");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(int.MaxValue)]
    public void MinimumThroughput_SetToValidValue_DoesNotThrow(int validValue)
    {
        var options = new CircuitBreakerOptions();
        options.MinimumThroughput = validValue;
        options.MinimumThroughput.Should().Be(validValue);
    }

    [Fact]
    public void MinimumThroughput_DefaultValue_IsTen()
    {
        var options = new CircuitBreakerOptions();
        options.MinimumThroughput.Should().Be(10);
    }

    [Fact]
    public void MinimumThroughput_SetAfterSamplingDuration_StillValidates()
    {
        // 先设置 SamplingDurationSeconds > 0，再设置 MinimumThroughput < 2，应抛出异常
        var options = new CircuitBreakerOptions
        {
            SamplingDurationSeconds = 30
        };

        var act = () => options.MinimumThroughput = 1;
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*必须 >= 2*");
    }
}
