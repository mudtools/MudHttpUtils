namespace Mud.HttpUtils.Client.Tests.Token.Cache;

/// <summary>
/// TR-03（P0.1）：MemoryCacheTokenCache 两参 Set 与五参 Set 的契约等价性护栏。
/// <para>
/// 先红记录（2026-09-21，修复前）：
/// <list type="bullet">
/// <item><description><see cref="TwoArgSet_WithSizeLimit_ShouldBehaveLikeFiveArgSet"/>：当前两参 Set 直接
/// <c>_cache.Set(key, value)</c>，SizeLimit 非空时 MemoryCache 抛 InvalidOperationException（未设置 Size）。</description></item>
/// <item><description><see cref="ShadowIndex_ShouldNotRetainEvictedKeys"/>：当前两参 Set 未注册驱逐回调，
/// Compact(1.0) 驱逐全部条目后影子索引残留 100 个键，Count 失真。</description></item>
/// </list>
/// </para>
/// </remarks>
public class MemoryCacheTokenCacheContractTests
{
    [Fact]
    public void TwoArgSet_WithSizeLimit_ShouldBehaveLikeFiveArgSet()
    {
        using var cache = new MemoryCacheTokenCache<string>(sizeLimit: 16);

        var act = () => cache.Set("k", "v");

        act.Should().NotThrow("两参 Set 必须按 SizeLimit 配置补 Size（与五参重载契约一致）");
        cache.TryGet("k", out var v).Should().BeTrue();
        v.Should().Be("v");
    }

    [Fact]
    public void ShadowIndex_ShouldNotRetainEvictedKeys()
    {
        using var cache = new MemoryCacheTokenCache<string>();
        for (var i = 0; i < 100; i++)
            cache.Set($"k{i}", new string('x', 64));

        cache.Compact(1.0);

        cache.Count.Should().Be(0,
            "Compact(1.0) 驱逐全部条目后影子索引必须同步（驱逐回调登记）");
        cache.Keys.Should().BeEmpty();
        cache.TryGet("k0", out _).Should().BeFalse();
    }

    [Fact]
    public void TwoArgSet_ShouldNotThrow_WhenSizeLimitNotConfigured()
    {
        using var cache = new MemoryCacheTokenCache<string>();

        var act = () => cache.Set("k", "v");

        act.Should().NotThrow();
        cache.TryGet("k", out var v).Should().BeTrue();
        v.Should().Be("v");
    }
}
