// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// TimeoutOptions 范围校验测试。
/// </summary>
public class TimeoutOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new TimeoutOptions();

        options.Enabled.Should().BeTrue();
        options.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void TimeoutSeconds_SetToZero_Throws()
    {
        var act = () => new TimeoutOptions { TimeoutSeconds = 0 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TimeoutSeconds_SetToNegative_Throws()
    {
        var act = () => new TimeoutOptions { TimeoutSeconds = -1 };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TimeoutSeconds_SetToPositive_Succeeds()
    {
        var options = new TimeoutOptions { TimeoutSeconds = 60 };
        options.TimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void TimeoutSeconds_SetToOne_Succeeds()
    {
        var options = new TimeoutOptions { TimeoutSeconds = 1 };
        options.TimeoutSeconds.Should().Be(1);
    }

    [Fact]
    public void Enabled_CanBeDisabled()
    {
        var options = new TimeoutOptions { Enabled = false };
        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void FullConfiguration_SetsAllProperties()
    {
        var options = new TimeoutOptions
        {
            Enabled = true,
            TimeoutSeconds = 120
        };

        options.Enabled.Should().BeTrue();
        options.TimeoutSeconds.Should().Be(120);
    }
}
