// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// T19 ~ T21：审查报告 §4.1 指出的<b>零覆盖</b>配置项补齐（测试缺失类，不改变产品行为）。
/// </summary>
/// <remarks>
/// 补齐前全仓 <c>Tests</c> 目录对下列三个属性的引用数均为 <c>0</c>：
/// <c>OAuth2Options.ClientSecretCacheTtlSeconds</c>、
/// <c>TokenRecoveryOptions.RefreshTimeoutSeconds</c>、
/// <c>TokenRefreshBackgroundOptions.MaxConsecutiveFailures</c>。
/// 三者均带 setter 值域校验（非法值抛异常），因此「默认值 + 边界 + 绑定往返」三类断言可同时守住
/// 「默认值漂移」与「校验被误删」两种回归。
/// </remarks>
public class OptionsCoverageGapTests
{
    private static T BindOptions<T>(string sectionPath, Dictionary<string, string?> values) where T : class, new()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.Configure<T>(config.GetSection(sectionPath));
        return services.BuildServiceProvider().GetRequiredService<IOptions<T>>().Value;
    }

    #region T19：OAuth2Options.ClientSecretCacheTtlSeconds

    [Fact]
    public void T19_OAuth2Options_ClientSecretCacheTtlSeconds_DefaultAndBoundary()
    {
        new OAuth2Options().ClientSecretCacheTtlSeconds.Should().Be(300, "默认 TTL 为 300 秒");

        var negative = () => _ = new OAuth2Options { ClientSecretCacheTtlSeconds = -1 };
        negative.Should().Throw<ArgumentOutOfRangeException>();

        new OAuth2Options { ClientSecretCacheTtlSeconds = 0 }.ClientSecretCacheTtlSeconds.Should().Be(0,
            "0 合法：表示禁用缓存（区别于负数的非法值）");
    }

    [Fact]
    public void T19_OAuth2Options_ClientSecretCacheTtlSeconds_BindsFromConfiguration()
    {
        var options = BindOptions<OAuth2Options>(OAuth2Options.SectionName, new Dictionary<string, string?>
        {
            ["MudHttpOAuth2:ClientSecretCacheTtlSeconds"] = "120",
        });

        options.ClientSecretCacheTtlSeconds.Should().Be(120);
    }

    #endregion

    #region T20：TokenRecoveryOptions.RefreshTimeoutSeconds

    [Fact]
    public void T20_TokenRecoveryOptions_RefreshTimeoutSeconds_BindsFractionalDouble()
    {
        // double 类型：绑定必须保留小数（防止被误改成 int 或截断）
        var options = BindOptions<TokenRecoveryOptions>(TokenRecoveryOptions.SectionName, new Dictionary<string, string?>
        {
            ["MudHttpTokenRecovery:RefreshTimeoutSeconds"] = "5.5",
        });

        options.RefreshTimeoutSeconds.Should().Be(5.5);
    }

    [Fact]
    public void T20_TokenRecoveryOptions_RefreshTimeoutSeconds_DefaultAndBoundary()
    {
        new TokenRecoveryOptions().RefreshTimeoutSeconds.Should().Be(30);

        var zero = () => _ = new TokenRecoveryOptions { RefreshTimeoutSeconds = 0 };
        zero.Should().Throw<ArgumentOutOfRangeException>();

        var negative = () => _ = new TokenRecoveryOptions { RefreshTimeoutSeconds = -1.5 };
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region T21：TokenRefreshBackgroundOptions.MaxConsecutiveFailures

    [Fact]
    public void T21_TokenRefreshBackgroundOptions_MaxConsecutiveFailures_DefaultAndBoundary()
    {
        new TokenRefreshBackgroundOptions().MaxConsecutiveFailures.Should().Be(0,
            "默认 0 = 不因连续失败次数停止后台刷新服务");

        var negative = () => _ = new TokenRefreshBackgroundOptions { MaxConsecutiveFailures = -1 };
        negative.Should().Throw<ArgumentOutOfRangeException>();

        new TokenRefreshBackgroundOptions { MaxConsecutiveFailures = 0 }.MaxConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void T21_TokenRefreshBackgroundOptions_MaxConsecutiveFailures_BindsFromConfiguration()
    {
        var options = BindOptions<TokenRefreshBackgroundOptions>(
            TokenRefreshBackgroundOptions.SectionName,
            new Dictionary<string, string?>
            {
                ["TokenRefreshBackground:MaxConsecutiveFailures"] = "5",
            });

        options.MaxConsecutiveFailures.Should().Be(5);
    }

    #endregion
}
