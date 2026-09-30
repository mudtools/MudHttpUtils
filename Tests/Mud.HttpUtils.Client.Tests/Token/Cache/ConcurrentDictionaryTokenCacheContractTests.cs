// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests.Token;

/// <summary>
/// S1-4（治理方案）：<see cref="ConcurrentDictionaryTokenCache{T}"/> 专用契约测试。
/// 此前该实现仅有分散引用、无专用契约文件 —— 本文件锁定其能力边界，
/// 特别是 C9 的"过期策略 / 驱逐回调静默 no-op"行为（S1-5 诊断化后语义不变）。
/// </summary>
public class ConcurrentDictionaryTokenCacheContractTests
{
    private sealed class Entry
    {
        public string Name { get; set; } = string.Empty;
    }

    private static ConcurrentDictionaryTokenCache<Entry> CreateCache() => new();

    // ---- 基础 CRUD ----

    [Fact]
    public void Set_TryGet_RoundTrips()
    {
        var cache = CreateCache();

        cache.Set("k1", new Entry { Name = "v1" });
        cache.TryGet("k1", out var value).Should().BeTrue();
        value!.Name.Should().Be("v1");
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var cache = CreateCache();

        cache.TryGet("missing", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryRemove_RemovesAndReturnsValue()
    {
        var cache = CreateCache();
        cache.Set("k1", new Entry { Name = "v1" });

        cache.TryRemove("k1", out var removed).Should().BeTrue();
        removed!.Name.Should().Be("v1");
        cache.Count.Should().Be(0);
        cache.TryRemove("k1", out _).Should().BeFalse();
    }

    [Fact]
    public void SetNull_TwoParam_StoresNullEntryEntry()
    {
        var cache = CreateCache();
        cache.Set("k1", new Entry { Name = "v1" });

        // 注意：与 MemoryCacheTokenCache / 桥接器不同，本实现的两参 Set(null) 是"存 null 条目"（既有语义，保持锁定）
        cache.Set("k1", null);
        cache.TryGet("k1", out var value).Should().BeTrue();
        value.Should().BeNull();
    }

    // ---- C9：过期策略与驱逐回调 no-op 行为锁定（S1-5 语义不变） ----

    [Fact]
    public void Set_WithExpirationPolicy_IsNoOpButValueIsStored()
    {
        var cache = CreateCache();
        var callbackInvoked = false;

        // 传入绝对过期 + 滑动过期 + 回调：不抛出、值照常存储（诊断一次性输出，语义为 no-op）
        cache.Set("k1", new Entry { Name = "v1" }, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), _ => callbackInvoked = true);

        cache.TryGet("k1", out var value).Should().BeTrue("no-op 指的是过期策略，不是写入本身");
        value!.Name.Should().Be("v1");
    }

    [Fact]
    public void Set_WithExpirationPolicy_EntryNeverExpires()
    {
        var cache = CreateCache();
        cache.Set("k1", new Entry { Name = "v1" }, TimeSpan.FromMilliseconds(1), null);

        // 绝对过期 1ms：等待远超过期时长后仍可读 —— 锁定"本实现不执行过期"的契约
        Thread.Sleep(50);
        cache.TryGet("k1", out _).Should().BeTrue("ConcurrentDictionaryTokenCache 的过期判定归管线（IsTokenValid），缓存自身不过期");
    }

    [Fact]
    public void Compact_DoesNotInvokeEvictionCallbacks()
    {
        var cache = CreateCache();
        var callbackInvoked = false;
        cache.Set("k1", new Entry { Name = "v1" }, null, null, _ => callbackInvoked = true);

        cache.Compact(1.0);

        cache.Count.Should().Be(0);
        callbackInvoked.Should().BeFalse("本实现不支持驱逐回调（C9 锁定）");
    }

    // ---- LRU Compact ----

    [Fact]
    public void Compact_Full_ClearsAll()
    {
        var cache = CreateCache();
        for (var i = 0; i < 5; i++)
            cache.Set("k" + i, new Entry { Name = "v" + i });

        cache.Compact(1.0);

        cache.Count.Should().Be(0);
    }

    [Fact]
    public void Compact_Partial_RemovesExpectedShare()
    {
        var cache = CreateCache();
        for (var i = 0; i < 10; i++)
            cache.Set("k" + i, new Entry { Name = "v" + i });

        cache.Compact(0.5);

        cache.Count.Should().Be(5);
    }

    [Fact]
    public void Compact_ZeroOrNegative_IsNoOp()
    {
        var cache = CreateCache();
        cache.Set("k1", new Entry { Name = "v1" });

        cache.Compact(0);
        cache.Compact(-0.5);

        cache.Count.Should().Be(1);
    }

    // ---- 生命周期 ----

    [Fact]
    public void Dispose_AfterDispose_ReadsAndWritesAreNoOps()
    {
        var cache = CreateCache();
        cache.Set("k1", new Entry { Name = "v1" });

        cache.Dispose();

        cache.TryGet("k1", out _).Should().BeFalse("HC-04：Dispose 后不再允许读取");
        cache.Set("k2", new Entry { Name = "v2" });
        cache.TryRemove("k1", out _).Should().BeFalse();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void Keys_Count_TrackOperations()
    {
        var cache = CreateCache();
        cache.Keys.Should().BeEmpty();
        cache.Count.Should().Be(0);

        cache.Set("k1", new Entry());
        cache.Set("k2", new Entry());
        cache.Keys.Should().BeEquivalentTo(new[] { "k1", "k2" });
        cache.Count.Should().Be(2);

        cache.TryRemove("k1", out _);
        cache.Keys.Should().BeEquivalentTo(new[] { "k2" });
    }
}
