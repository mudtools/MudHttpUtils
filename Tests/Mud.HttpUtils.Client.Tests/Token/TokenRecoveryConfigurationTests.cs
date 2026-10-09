// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// B6：<see cref="TokenRecoveryConfiguration"/>（纯 DTO，可被绑定源生成器完整支持）
/// 与 <see cref="TokenRecoveryOptionsExtensions.Apply"/> 的映射与校验契约。
/// </summary>
public class TokenRecoveryConfigurationTests
{
    [Fact]
    public void Apply_ShouldMapAllPrimitiveMembers()
    {
        var options = new TokenRecoveryOptions();
        var config = new TokenRecoveryConfiguration
        {
            Enabled = false,
            RecoveryMaxRetries = 3,
            TokenScheme = "Basic",
            RefreshTimeoutSeconds = 12.5,
            RefreshWaitHardTimeoutSeconds = 20,
            MaxCachedRequestBodyBytes = 4096,
            BufferingMode = RequestBodyBufferingMode.MemoryOnly,
            AllowNonIdempotentRecovery = true,
            RefreshDedupWindowSeconds = 7,
            MaxDedupEntries = 64,
            MaxCapturedResponseBodyBytes = 8192
        };

        var returned = options.Apply(config);

        returned.Should().BeSameAs(options, "便于链式调用");
        options.Enabled.Should().BeFalse();
        options.RecoveryMaxRetries.Should().Be(3);
        options.TokenScheme.Should().Be("Basic");
        options.RefreshTimeoutSeconds.Should().Be(12.5);
        options.RefreshWaitHardTimeoutSeconds.Should().Be(20);
        options.MaxCachedRequestBodyBytes.Should().Be(4096);
        options.BufferingMode.Should().Be(RequestBodyBufferingMode.MemoryOnly);
        options.AllowNonIdempotentRecovery.Should().BeTrue();
        options.RefreshDedupWindowSeconds.Should().Be(7);
        options.MaxDedupEntries.Should().Be(64);
        options.MaxCapturedResponseBodyBytes.Should().Be(8192);
    }

    [Fact]
    public void Apply_ShouldPreserveProgrammingOnlyMembers()
    {
        var detector = new ProbeDetector();
        var options = new TokenRecoveryOptions { TokenInvalidationDetector = detector };
        options.AdditionalTokenInvalidationDetectors.Add(detector);

        options.Apply(new TokenRecoveryConfiguration());

        options.TokenInvalidationDetector.Should().BeSameAs(detector, "判定器属编程式注入面，不参与配置绑定");
        options.AdditionalTokenInvalidationDetectors.Should().ContainSingle().Which.Should().BeSameAs(detector);
    }

    [Theory]
    [InlineData(-1, "RecoveryMaxRetries")]
    [InlineData(0, "RefreshTimeoutSeconds")]
    public void Apply_InvalidValues_ShouldThrow(double value, string _)
    {
        var options = new TokenRecoveryOptions();
        var config = new TokenRecoveryConfiguration { RecoveryMaxRetries = (int)value };
        if (value == 0)
            config = new TokenRecoveryConfiguration { RefreshTimeoutSeconds = 0 };

        var act = () => options.Apply(config);

        act.Should().Throw<ArgumentOutOfRangeException>("赋值经目标 setter 校验，语义与直接赋值一致");
    }

    [Fact]
    public void Apply_NullArguments_Throw()
    {
        var act1 = () => ((TokenRecoveryOptions)null!).Apply(new TokenRecoveryConfiguration());
        var act2 = () => new TokenRecoveryOptions().Apply(null!);

        act1.Should().Throw<ArgumentNullException>().WithParameterName("options");
        act2.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    /// <summary>默认投影必须与 <see cref="TokenRecoveryOptions"/> 默认值逐字段一致（防漂移）。</summary>
    [Fact]
    public void Defaults_ShouldMatchTokenRecoveryOptions()
    {
        var defaults = new TokenRecoveryOptions();
        var applied = new TokenRecoveryOptions().Apply(new TokenRecoveryConfiguration());

        applied.Enabled.Should().Be(defaults.Enabled);
        applied.RecoveryMaxRetries.Should().Be(defaults.RecoveryMaxRetries);
        applied.TokenScheme.Should().Be(defaults.TokenScheme);
        applied.RefreshTimeoutSeconds.Should().Be(defaults.RefreshTimeoutSeconds);
        applied.RefreshWaitHardTimeoutSeconds.Should().Be(defaults.RefreshWaitHardTimeoutSeconds);
        applied.MaxCachedRequestBodyBytes.Should().Be(defaults.MaxCachedRequestBodyBytes);
        applied.BufferingMode.Should().Be(defaults.BufferingMode);
        applied.AllowNonIdempotentRecovery.Should().Be(defaults.AllowNonIdempotentRecovery);
        applied.RefreshDedupWindowSeconds.Should().Be(defaults.RefreshDedupWindowSeconds);
        applied.MaxDedupEntries.Should().Be(defaults.MaxDedupEntries);
        applied.MaxCapturedResponseBodyBytes.Should().Be(defaults.MaxCapturedResponseBodyBytes);
    }

    private sealed class ProbeDetector : ITokenInvalidationDetector
    {
        public bool ShouldInspect(HttpRequestMessage request) => false;

        public ValueTask<bool> IsTokenInvalidAsync(
            HttpResponseMessage response, ReadOnlyMemory<byte>? body, CancellationToken cancellationToken)
            => new ValueTask<bool>(false);
    }
}
