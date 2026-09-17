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

    #region 缓存失效（Invalidate / InvalidateAll）

    [Fact]
    public void Invalidate_RemovesCachedResolver_FactoryCalledAgainOnNextResolve()
    {
        // Arrange
        var callCount = 0;
        var resolver = new AppResiliencePolicyResolver(_ =>
        {
            callCount++;
            return new ResilienceOptions();
        });

        // Act
        resolver.ResolveResolver("app1");
        callCount.Should().Be(1);

        resolver.Invalidate("app1");

        // Act — 再次解析应重新调用工厂
        resolver.ResolveResolver("app1");
        callCount.Should().Be(2, "Invalidate 后缓存被清除，工厂应再次调用");
    }

    [Fact]
    public void Invalidate_ReturnsFalse_WhenKeyNotInCache()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions());

        // Act
        var result = resolver.Invalidate("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Invalidate_ReturnsTrue_WhenKeyWasCached()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions());
        resolver.ResolveResolver("app1");

        // Act
        var result = resolver.Invalidate("app1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void InvalidateAll_ClearsEntireCache()
    {
        // Arrange
        var callCount = 0;
        var resolver = new AppResiliencePolicyResolver(_ =>
        {
            callCount++;
            return new ResilienceOptions();
        });

        resolver.ResolveResolver("app1");
        resolver.ResolveResolver("app2");
        resolver.ResolveResolver("app3");
        callCount.Should().Be(3);

        // Act
        resolver.InvalidateAll();

        // Act — 全部重新解析
        resolver.ResolveResolver("app1");
        resolver.ResolveResolver("app2");
        resolver.ResolveResolver("app3");
        callCount.Should().Be(6, "InvalidateAll 后所有缓存条目被清除");
    }

    [Fact]
    public void Invalidate_DoesNotAffectOtherCachedApps()
    {
        // Arrange
        var callCount = 0;
        var resolver = new AppResiliencePolicyResolver(_ =>
        {
            callCount++;
            return new ResilienceOptions();
        });

        var first1 = resolver.ResolveResolver("app1");
        var first2 = resolver.ResolveResolver("app2");
        callCount.Should().Be(2);

        // Act — 仅失效 app1
        resolver.Invalidate("app1");

        // Assert — app2 的缓存不受影响
        var second2 = resolver.ResolveResolver("app2");
        second2.Should().BeSameAs(first2);
        callCount.Should().Be(2, "app2 缓存未失效，工厂不应被再次调用");

        // app1 被重新解析
        var second1 = resolver.ResolveResolver("app1");
        callCount.Should().Be(3);
        second1.Should().NotBeSameAs(first1);
    }

    #endregion

    #region 缓存基数上限（maxCachedApps）

    [Fact]
    public void ResolveResolver_ReturnsNull_WhenCacheFullAndNewKeyQueried()
    {
        // Arrange — maxCachedApps = 2，缓存满后第 3 个 key 返回 null
        var callCount = 0;
        var resolver = new AppResiliencePolicyResolver(appKey =>
        {
            callCount++;
            return new ResilienceOptions();
        }, maxCachedApps: 2);

        // 填满缓存
        resolver.ResolveResolver("app1").Should().NotBeNull();
        resolver.ResolveResolver("app2").Should().NotBeNull();

        // Act — 第 3 个 key：缓存已满且 key 不在缓存中
        var result = resolver.ResolveResolver("app3");

        // Assert — 超限时不缓存，但工厂仍被调用（fail-safe 路径）
        result.Should().NotBeNull("工厂返回非 null 选项时仍创建解析器（不缓存但不返回 null）");
        callCount.Should().Be(3);

        // 再次查询 app3 — 仍不在缓存中，工厂再次被调用
        var result2 = resolver.ResolveResolver("app3");
        callCount.Should().Be(4, "超限 key 不被缓存，工厂每次被调用");
    }

    [Fact]
    public void ResolveResolver_CachedKeyStillWorks_EvenWhenCacheFull()
    {
        // Arrange
        var callCount = 0;
        var resolver = new AppResiliencePolicyResolver(_ =>
        {
            callCount++;
            return new ResilienceOptions();
        }, maxCachedApps: 2);

        resolver.ResolveResolver("app1");
        resolver.ResolveResolver("app2");
        callCount.Should().Be(2);

        // 缓存已满，但已缓存的 key 仍走缓存
        resolver.ResolveResolver("app1");
        resolver.ResolveResolver("app2");
        callCount.Should().Be(2, "已缓存 key 不受基数上限影响");
    }

    [Fact]
    public void ResolveResolver_NullFactoryResult_WhenCacheFull_ReturnsNull()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(_ => null, maxCachedApps: 1);

        // 填满缓存（app1 返回 null，被哨兵缓存）
        resolver.ResolveResolver("app1").Should().BeNull();

        // Act — app2 也是 null，但缓存已满
        var result = resolver.ResolveResolver("app2");

        // Assert — 超限路径直接调用工厂，工厂返回 null
        result.Should().BeNull();
    }

    [Fact]
    public void Constructor_AcceptsMaxCachedAppsParameter()
    {
        // Arrange & Act
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions(), maxCachedApps: 10);

        // Assert
        resolver.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_NegativeMaxCachedApps_FallsBackToDefault()
    {
        // Arrange & Act — 负值应回退到默认 1024
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions(), maxCachedApps: -1);

        // Assert — 不抛异常
        resolver.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ZeroMaxCachedApps_FallsBackToDefault()
    {
        // Arrange & Act — 零值应回退到默认 1024
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions(), maxCachedApps: 0);

        // Assert
        resolver.Should().NotBeNull();
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions());

        // Act & Assert — 多次 Dispose 不抛异常
        resolver.Dispose();
        resolver.Dispose();
        resolver.Dispose();
    }

    [Fact]
    public void Dispose_ReleasesOptionsSubscription()
    {
        // Arrange — 不直接测试 IOptionsMonitor 订阅（需要 Mock），
        // 但确保 Dispose 后仍可正常使用 ResolveResolver（降级为无订阅模式）
        var resolver = new AppResiliencePolicyResolver(_ => new ResilienceOptions());

        resolver.ResolveResolver("app1").Should().NotBeNull();

        // Act
        resolver.Dispose();

        // Assert — Dispose 后仍可解析（订阅已释放但缓存仍可用）
        resolver.ResolveResolver("app1").Should().NotBeNull();
    }

    #endregion
}
