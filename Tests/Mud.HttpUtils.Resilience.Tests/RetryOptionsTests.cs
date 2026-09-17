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

    // ---------------------------------------------------------------
    // T-17（零覆盖清零）：UseJitter / AllowNonIdempotentRetry 默认值
    // ---------------------------------------------------------------

    [Fact]
    public void T17_DefaultValues_UseJitterTrue_AllowNonIdempotentRetryFalse()
    {
        var options = new RetryOptions();

        options.UseJitter.Should().BeTrue("默认开启抖动以避免多实例重试风暴（README 默认值表契约）");
        options.AllowNonIdempotentRetry.Should().BeFalse("默认不重试非幂等方法（防重复提交）");
        options.RetryableHttpMethods.Should().BeEquivalentTo(
            new[] { "GET", "HEAD", "OPTIONS", "PUT", "DELETE", "TRACE" },
            because: "默认可重试集合为幂等方法（与 RetryGuard.DefaultRetryableMethods 一致）");
    }

    // ---------------------------------------------------------------
    // T-15（零覆盖清零）：UseJitter=false 时退避为确定性值
    // ---------------------------------------------------------------

    [Fact]
    public async Task T15_UseJitter_False_UsesDeterministicBackoff()
    {
        var delays = new List<TimeSpan>();
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 2,
                DelayMilliseconds = 1000,
                UseExponentialBackoff = false,
                UseJitter = false,
                OnRetry = (_, _, delay) => { delays.Add(delay); return Task.CompletedTask; }
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);
        var policy = provider.GetRetryPolicy<HttpResponseMessage>("global");

        var act = async () => await policy.ExecuteAsync(() =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException(
                "HTTP请求失败: 500 InternalServerError - error", null, HttpStatusCode.InternalServerError)));

        await act.Should().ThrowAsync<HttpRequestException>();

        delays.Should().HaveCount(2);
        delays.Should().OnlyContain(d => d == TimeSpan.FromMilliseconds(1000),
            "关闭抖动后固定退避必须精确等于 DelayMilliseconds（无随机成分）");
    }

    [Fact]
    public async Task T15_UseJitter_True_AddsJitterWithinQuarterBound()
    {
        var delays = new List<TimeSpan>();
        var options = new ResilienceOptions
        {
            Retry =
            {
                Enabled = true,
                MaxRetryAttempts = 2,
                DelayMilliseconds = 1000,
                UseExponentialBackoff = false,
                UseJitter = true,
                OnRetry = (_, _, delay) => { delays.Add(delay); return Task.CompletedTask; }
            }
        };
        var provider = new PollyResiliencePolicyProvider(options);
        var policy = provider.GetRetryPolicy<HttpResponseMessage>("global");

        var act = async () => await policy.ExecuteAsync(() =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException(
                "HTTP请求失败: 500 InternalServerError - error", null, HttpStatusCode.InternalServerError)));

        await act.Should().ThrowAsync<HttpRequestException>();

        delays.Should().HaveCount(2);
        delays.Should().OnlyContain(d => d >= TimeSpan.FromMilliseconds(1000) && d < TimeSpan.FromMilliseconds(1250),
            "抖动范围契约：[基础退避, 基础退避 + 基础退避/4)");
    }

    // ---------------------------------------------------------------
    // T-16（零覆盖清零）：RetryableHttpMethods 自定义集合约束重试
    // ---------------------------------------------------------------

    [Fact]
    public void T16_RetryableHttpMethods_CustomSet_ConstrainsRetry()
    {
        var options = new ResilienceOptions();
        options.Retry.RetryableHttpMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GET" };
        options.Retry.AllowNonIdempotentRetry = false;

        RetryGuard.IsRetryAllowedForMethod("GET", options).Should().BeTrue();
        RetryGuard.IsRetryAllowedForMethod("get", options).Should().BeTrue("集合不区分大小写");
        RetryGuard.IsRetryAllowedForMethod("POST", options).Should().BeFalse("不在集合中的方法不重试");
        RetryGuard.IsRetryAllowedForMethod("DELETE", options).Should().BeFalse();

        // CFG-09 既有语义：AllowNonIdempotentRetry=true 时方法集合被忽略（提前返回）
        options.Retry.AllowNonIdempotentRetry = true;
        RetryGuard.IsRetryAllowedForMethod("POST", options).Should().BeTrue();
    }

    [Fact]
    public void T16_RetryGuard_NullOptions_UsesDefaultIdempotentSet()
    {
        RetryGuard.IsRetryAllowedForMethod("GET", null).Should().BeTrue();
        RetryGuard.IsRetryAllowedForMethod("PUT", null).Should().BeTrue();
        RetryGuard.IsRetryAllowedForMethod("POST", null).Should().BeFalse();
        RetryGuard.IsRetryAllowedForMethod("PATCH", null).Should().BeFalse();
    }
}
