// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils.Resilience.Tests;

/// <summary>
/// AppResiliencePolicyResolver 的单元测试。
/// 覆盖 per-app 弹性策略解析、null 缓存哨兵、空 appKey 回退等场景。
/// </summary>
public class AppResiliencePolicyResolverTests
{
    #region ResolveResolver

    [Fact]
    public void ResolveResolver_ShouldReturnNull_WhenAppKeyIsEmpty()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions());

        // Act
        var result = resolver.ResolveResolver("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveResolver_ShouldReturnNull_WhenAppKeyIsNull()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions());

        // Act
        var result = resolver.ResolveResolver(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveResolver_ShouldReturnNull_WhenOptionsFactoryReturnsNull()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(_ => null);

        // Act
        var result = resolver.ResolveResolver("app1");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveResolver_ShouldReturnResolver_WhenOptionsFactoryReturnsOptions()
    {
        // Arrange
        var options = new ResilienceOptions
        {
            Retry = { Enabled = true, MaxRetryAttempts = 5 },
            Timeout = { Enabled = true, TimeoutSeconds = 60 }
        };
        var resolver = new AppResiliencePolicyResolver(_ => options);

        // Act
        var result = resolver.ResolveResolver("app1");

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ResolveResolver_ShouldReturnSameInstance_WhenCalledMultipleTimesWithSameAppKey()
    {
        // Arrange
        var callCount = 0;
        var resolver = new AppResiliencePolicyResolver(_ =>
        {
            callCount++;
            return new ResilienceOptions();
        });

        // Act
        var first = resolver.ResolveResolver("app1");
        var second = resolver.ResolveResolver("app1");

        // Assert
        first.Should().BeSameAs(second);
        callCount.Should().Be(1, "工厂函数应只被调用一次（缓存）");
    }

    [Fact]
    public void ResolveResolver_ShouldReturnDifferentInstances_WhenCalledWithDifferentAppKeys()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions());

        // Act
        var first = resolver.ResolveResolver("app1");
        var second = resolver.ResolveResolver("app2");

        // Assert
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void ResolveResolver_ShouldNotCallFactoryAgain_WhenFirstCallReturnedNull()
    {
        // Arrange
        var callCount = 0;
        var resolver = new AppResiliencePolicyResolver(_ =>
        {
            callCount++;
            return null;
        });

        // Act
        resolver.ResolveResolver("app1");
        resolver.ResolveResolver("app1");

        // Assert
        callCount.Should().Be(1, "工厂函数应只被调用一次，后续使用哨兵缓存");
    }

    [Fact]
    public void ResolveResolver_ShouldReturnNull_WhenFactoryReturnsNullAndThenCalledAgain()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(_ => null);

        // Act
        var first = resolver.ResolveResolver("app1");
        var second = resolver.ResolveResolver("app1");

        // Assert
        first.Should().BeNull();
        second.Should().BeNull();
    }

    #endregion

    #region 构造函数

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsFactoryIsNull()
    {
        // Act
        var act = () => new AppResiliencePolicyResolver(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("optionsFactory");
    }

    [Fact]
    public void Constructor_ShouldAcceptNullLogger()
    {
        // Arrange & Act
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions(), null);

        // Assert
        resolver.Should().NotBeNull();
    }

    #endregion

    #region Per-App 隔离验证

    [Fact]
    public void ResolveResolver_ShouldCreateDifferentPolicies_WhenDifferentAppsHaveDifferentOptions()
    {
        // Arrange
        var optionsMap = new Dictionary<string, ResilienceOptions>
        {
            ["app1"] = new() { Retry = { Enabled = true, MaxRetryAttempts = 3 } },
            ["app2"] = new() { Retry = { Enabled = true, MaxRetryAttempts = 5 } }
        };
        var resolver = new AppResiliencePolicyResolver(
            appKey => optionsMap.TryGetValue(appKey, out var opt) ? opt : null);

        // Act
        var resolver1 = resolver.ResolveResolver("app1");
        var resolver2 = resolver.ResolveResolver("app2");

        // Assert
        resolver1.Should().NotBeNull();
        resolver2.Should().NotBeNull();
        resolver1.Should().NotBeSameAs(resolver2);
    }

    [Fact]
    public void ResolveResolver_ShouldReturnNullForUnknownApp_WhileKnownAppStillWorks()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(appKey =>
            appKey == "known" ? new ResilienceOptions() : null);

        // Act & Assert
        resolver.ResolveResolver("known").Should().NotBeNull();
        resolver.ResolveResolver("unknown").Should().BeNull();
        resolver.ResolveResolver("known").Should().NotBeNull("known app should still work after unknown app query");
    }

    #endregion
}
