// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// RetryOptions 范围校验测试。
/// </summary>
public class RetryOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new RetryOptions();

        options.Enabled.Should().BeTrue();
        options.MaxRetryAttempts.Should().Be(3);
        options.DelayMilliseconds.Should().Be(1000);
        options.UseExponentialBackoff.Should().BeTrue();
        options.RetryStatusCodes.Should().BeNull();
        options.OnRetry.Should().BeNull();
    }

    [Fact]
    public void MaxRetryAttempts_SetToZero_Succeeds()
    {
        var options = new RetryOptions { MaxRetryAttempts = 0 };
        options.MaxRetryAttempts.Should().Be(0);
    }

    [Fact]
    public void MaxRetryAttempts_SetToNegative_Throws()
    {
        var act = () => new RetryOptions { MaxRetryAttempts = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MaxRetryAttempts_SetToPositive_Succeeds()
    {
        var options = new RetryOptions { MaxRetryAttempts = 10 };
        options.MaxRetryAttempts.Should().Be(10);
    }

    [Fact]
    public void DelayMilliseconds_SetToZero_Succeeds()
    {
        var options = new RetryOptions { DelayMilliseconds = 0 };
        options.DelayMilliseconds.Should().Be(0);
    }

    [Fact]
    public void DelayMilliseconds_SetToNegative_Throws()
    {
        var act = () => new RetryOptions { DelayMilliseconds = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DelayMilliseconds_SetToPositive_Succeeds()
    {
        var options = new RetryOptions { DelayMilliseconds = 500 };
        options.DelayMilliseconds.Should().Be(500);
    }

    [Fact]
    public void UseExponentialBackoff_CanBeDisabled()
    {
        var options = new RetryOptions { UseExponentialBackoff = false };
        options.UseExponentialBackoff.Should().BeFalse();
    }

    [Fact]
    public void Enabled_CanBeDisabled()
    {
        var options = new RetryOptions { Enabled = false };
        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void RetryStatusCodes_CanBeSet()
    {
        var codes = new[] { 408, 429, 503 };
        var options = new RetryOptions { RetryStatusCodes = codes };
        options.RetryStatusCodes.Should().Equal(codes);
    }

    [Fact]
    public void FullConfiguration_SetsAllProperties()
    {
        var options = new RetryOptions
        {
            Enabled = true,
            MaxRetryAttempts = 5,
            DelayMilliseconds = 2000,
            UseExponentialBackoff = false,
            RetryStatusCodes = [408, 429, 500, 502, 503, 504]
        };

        options.Enabled.Should().BeTrue();
        options.MaxRetryAttempts.Should().Be(5);
        options.DelayMilliseconds.Should().Be(2000);
        options.UseExponentialBackoff.Should().BeFalse();
        options.RetryStatusCodes.Should().Equal(408, 429, 500, 502, 503, 504);
    }
}
