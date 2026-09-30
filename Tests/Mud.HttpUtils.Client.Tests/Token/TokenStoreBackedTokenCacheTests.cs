// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Mud.HttpUtils.Client.Tests.Token;

/// <summary>
/// S1-2（治理方案）：桥接器契约测试 —— 把 §5.3 的 6 条契约不变量逐条固化为可执行断言。
/// </summary>
public class TokenStoreBackedTokenCacheTests
{
    private const string TokenType = "TenantAccessToken";

    // ---- 测试替身 ----

    /// <summary>租户维度捕获型 store：基于 MemoryTokenStore（方法均为 virtual），记录写穿参数并支持故障注入。</summary>
    private sealed class CapturingTenantStore : MemoryTokenStore
    {
        public List<(string Key, string AccessToken, long ExpiresInSeconds)> AccessWrites { get; } = new();
        public List<(string Key, string RefreshToken)> RefreshWrites { get; } = new();
        public List<string> Removes { get; } = new();
        public List<string> Clears { get; } = new();
        public bool FailWrites;

        public override Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
        {
            AccessWrites.Add((tokenType, accessToken, expiresInSeconds));
            if (FailWrites)
                return Task.FromException(new IOException("store 不可用"));
            return base.SetAccessTokenAsync(tokenType, accessToken, expiresInSeconds, cancellationToken);
        }

        public override Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
        {
            RefreshWrites.Add((tokenType, refreshToken));
            if (FailWrites)
                return Task.FromException(new IOException("store 不可用"));
            return base.SetRefreshTokenAsync(tokenType, refreshToken, cancellationToken);
        }

        public override Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
        {
            Removes.Add(tokenType);
            return base.RemoveAsync(tokenType, cancellationToken);
        }

        public override Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Clears.Add(TokenType);
            return base.ClearAsync(cancellationToken);
        }
    }

    /// <summary>用户维度捕获型 store：完整实现 IUserTokenStore（仅带 userId 成员被调用方触及），记录调用并支持故障注入。</summary>
    private sealed class CapturingUserStore : IUserTokenStore
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (string? Access, string? Refresh, long Expires)>> _data = new(StringComparer.Ordinal);

        public List<(string UserId, string TokenType, long ExpiresInSeconds)> AccessWrites { get; } = new();
        public List<(string UserId, string TokenType)> Removes { get; } = new();
        public int ClearAllCalls;   // 桥接器绝不应触发（ClearAsync 会清空全部用户）
        public bool FailWrites;

        public Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员（C7）");

        public Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员（C7）");

        public Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员（C7）");

        public Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员（C7）");

        public Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员（C7）");

        public Task<IEnumerable<string>> GetTokenTypesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员（C7）");

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ClearAllCalls);
            return Task.CompletedTask;
        }

        public Task<string?> GetAccessTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
        {
            if (_data.TryGetValue(userId, out var bucket) && bucket.TryGetValue(tokenType, out var entry) && entry.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return Task.FromResult<string?>(entry.Access);
            return Task.FromResult<string?>(null);
        }

        public Task SetAccessTokenAsync(string userId, string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
        {
            AccessWrites.Add((userId, tokenType, expiresInSeconds));
            if (FailWrites)
                return Task.FromException(new IOException("store 不可用"));
            var bucket = _data.GetOrAdd(userId, _ => new ConcurrentDictionary<string, (string?, string?, long)>(StringComparer.Ordinal));
            bucket[tokenType] = (accessToken, bucket.TryGetValue(tokenType, out var old) ? old.Refresh : null, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds);
            return Task.CompletedTask;
        }

        public Task<string?> GetRefreshTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
        {
            if (_data.TryGetValue(userId, out var bucket) && bucket.TryGetValue(tokenType, out var entry))
                return Task.FromResult(entry.Refresh);
            return Task.FromResult<string?>(null);
        }

        public Task SetRefreshTokenAsync(string userId, string tokenType, string refreshToken, CancellationToken cancellationToken = default)
        {
            var bucket = _data.GetOrAdd(userId, _ => new ConcurrentDictionary<string, (string?, string?, long)>(StringComparer.Ordinal));
            var existing = bucket.TryGetValue(tokenType, out var old) ? old : (null, null, 0L);
            bucket[tokenType] = (existing.Item1, refreshToken, existing.Item3);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
        {
            Removes.Add((userId, tokenType));
            if (_data.TryGetValue(userId, out var bucket))
                bucket.TryRemove(tokenType, out _);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> GetTokenTypesAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<string>>(_data.TryGetValue(userId, out var bucket) ? bucket.Keys.ToList() : new List<string>());

        public Task ClearUserAsync(string userId, CancellationToken cancellationToken = default)
        {
            _data.TryRemove(userId, out _);
            return Task.CompletedTask;
        }
    }

    /// <summary>可释放包装：验证 ownsInnerStore 语义（不变量 #2）。</summary>
    private sealed class DisposableStore : ITokenStore, IDisposable
    {
        public bool Disposed { get; private set; }
        public Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetTokenTypesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<string>>(new List<string>());
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() => Disposed = true;
    }

    /// <summary>加密标记 store（不变量 #3 检测对象）。</summary>
    private sealed class EncryptedFlagStore : MemoryTokenStore, IEncryptedTokenStore
    {
        public bool IsEncryptionEnabled => true;
    }

    /// <summary>故障注入计时器：返回真异步的失败任务，覆盖后台续体路径。</summary>
    private sealed class SlowFaultingStore : MemoryTokenStore
    {
        public override Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
            => Task.Delay(30, cancellationToken).ContinueWith(t => throw new IOException("异步 store 故障"), TaskScheduler.Default);
    }

    // ---- 值适配 ----

    private static CredentialToken MakeToken(string accessToken, long expiresInSeconds)
        => new()
        {
            AccessToken = accessToken,
            Expire = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds).ToUnixTimeMilliseconds(),
            IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

    private static TokenStoreValue? TenantAdapter(CredentialToken? token)
    {
        if (token == null)
            return null;
        var remaining = (token.Expire - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000;
        return new TokenStoreValue(token.AccessToken, token.RefreshToken, remaining);
    }

    private static CredentialToken? TenantFactory(TokenStoreValue value)
        => new()
        {
            AccessToken = value.AccessToken,
            RefreshToken = value.RefreshToken,
            Expire = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
            IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

    private static TokenStoreBackedTokenCache<CredentialToken> CreateTenantBridge(ITokenStore store, bool ownsInnerStore = false)
        => new(store, TenantAdapter, TenantFactory, ownsInnerStore: ownsInnerStore);

    // ---- 不变量 #1：Keys/Count 与镜像同源 + 失效写穿（C6 登出漏删防线） ----

    [Fact]
    public void KeysAndCount_ReflectMirrorOnly()
    {
        var store = new CapturingTenantStore();
        var bridge = CreateTenantBridge(store);

        bridge.Set("default", MakeToken("t1", 3600));
        bridge.Set("scope-a", MakeToken("t2", 3600));

        bridge.Count.Should().Be(2);
        bridge.Keys.Should().BeEquivalentTo(new[] { "default", "scope-a" });
    }

    [Fact]
    public async Task TryRemove_WritesStoreRemoval_LogoutCompleteness()
    {
        var store = new CapturingTenantStore();
        var bridge = CreateTenantBridge(store);

        bridge.Set("default", MakeToken("t1", 3600));
        bridge.TryRemove("default", out var removed);

        removed.Should().NotBeNull();
        removed!.AccessToken.Should().Be("t1");
        bridge.Keys.Should().BeEmpty();
        store.Removes.Should().ContainSingle().Which.Should().Be("default");
        (await store.GetAccessTokenAsync("default")).Should().BeNull();
    }

    [Fact]
    public async Task SetNull_TreatedAsInvalidation_RemovesFromStoreAndFiresCallback()
    {
        var store = new CapturingTenantStore();
        var bridge = CreateTenantBridge(store);
        var evicted = new List<string>();

        bridge.Set("default", MakeToken("t1", 3600));
        // 失效调用方自带回调（与 MemoryCacheTokenCache 一致：登记回调仅在桥接器内部驱逐/替换时触发，
        // 显式失效按"失效调用方传入的回调"触发）
        bridge.Set("default", null, null, null, k => evicted.Add(k));

        evicted.Should().Contain("default");
        bridge.Keys.Should().BeEmpty();
        store.Removes.Should().Contain("default");
        (await store.GetAccessTokenAsync("default")).Should().BeNull();
    }

    // ---- 不变量 #2：Dispose 默认不释放内层 store（C4） ----

    [Fact]
    public void Dispose_DefaultDoesNotReleaseInnerStore()
    {
        var store = new DisposableStore();
        var bridge = CreateTenantBridge(store, ownsInnerStore: false);

        bridge.Dispose();

        store.Disposed.Should().BeFalse("store 通常由 DI 容器管理（如 Redis 连接），桥接器不得提前释放");
    }

    [Fact]
    public void Dispose_WithOwnsInnerStoreTrue_ReleasesInnerStore()
    {
        var store = new DisposableStore();
        var bridge = CreateTenantBridge(store, ownsInnerStore: true);

        bridge.Dispose();

        store.Disposed.Should().BeTrue();
    }

    // ---- 不变量 #3：加密叠加构造期检测（C5） ----

    [Fact]
    public void Constructor_DetectsEncryptedInnerStore()
    {
        var bridge = CreateTenantBridge(new EncryptedFlagStore());

        bridge.IsInnerStoreEncrypted.Should().BeTrue();
    }

    [Fact]
    public void Constructor_PlainStore_NotMarkedEncrypted()
    {
        var bridge = CreateTenantBridge(new CapturingTenantStore());

        bridge.IsInnerStoreEncrypted.Should().BeFalse();
    }

    // ---- 不变量 #4：TTL 与管线判定同源（C1） ----

    [Fact]
    public async Task WriteThrough_PrefersAdapterDerivedTtl()
    {
        var store = new CapturingTenantStore();
        var bridge = CreateTenantBridge(store);

        bridge.Set("default", MakeToken("access-1", 3600));

        var write = store.AccessWrites.Should().ContainSingle().Subject;
        write.Key.Should().Be("default");
        write.AccessToken.Should().Be("access-1");
        write.ExpiresInSeconds.Should().BeInRange(3590, 3600);
    }

    [Fact]
    public async Task WriteThrough_FallsBackToSetAbsoluteExpiration()
    {
        var store = new CapturingTenantStore();
        // 适配器产不出 TTL 的令牌（Expire = 0）→ 回退 Set 的绝对过期参数
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(
            store,
            token => token == null ? null : new TokenStoreValue(token.AccessToken, token.RefreshToken, 0),
            TenantFactory);

        bridge.Set("default", new CredentialToken { AccessToken = "no-expire", Expire = 0 }, TimeSpan.FromSeconds(120), null);

        store.AccessWrites.Should().ContainSingle().Which.ExpiresInSeconds.Should().Be(120);
    }

    [Fact]
    public async Task WriteThrough_SkipsAccessToken_WhenNoTtlDerivable()
    {
        var store = new CapturingTenantStore();
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(
            store,
            token => token == null ? null : new TokenStoreValue(token.AccessToken, token.RefreshToken, 0),
            TenantFactory);

        // 无 TTL 可推导 → 跳过访问令牌写穿（防 store 无界滞留），refresh 字段照常
        bridge.Set("default", new CredentialToken { AccessToken = "no-expire", RefreshToken = "rt-1", Expire = 0 });

        store.AccessWrites.Should().BeEmpty();
        store.RefreshWrites.Should().ContainSingle().Which.RefreshToken.Should().Be("rt-1");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task WriteThrough_RespectsStoreKeyMapper()
    {
        var store = new CapturingTenantStore();
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(
            store, TenantAdapter, TenantFactory,
            storeKeyMapper: key => "prefix:" + key);

        bridge.Set("default", MakeToken("t1", 3600));

        store.AccessWrites.Should().ContainSingle().Which.Key.Should().Be("prefix:default");
    }

    // ---- 不变量 #5：写穿失败可观测 + 补偿重放 ----

    [Fact]
    public async Task WriteThroughFailure_IsCountedAndCompensated_SetDoesNotThrow()
    {
        var store = new CapturingTenantStore { FailWrites = true };
        var bridge = CreateTenantBridge(store);

        var act = () => bridge.Set("default", MakeToken("t1", 3600));

        act.Should().NotThrow("写穿失败不得打断管线");
        bridge.WriteThroughFailures.Should().Be(1);
        bridge.PendingCompensationCount.Should().Be(1);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RetryFailedWritesAsync_ReplaysCompensationQueue()
    {
        var store = new CapturingTenantStore { FailWrites = true };
        var bridge = CreateTenantBridge(store);
        bridge.Set("default", MakeToken("t1", 3600));
        bridge.WriteThroughFailures.Should().Be(1);

        store.FailWrites = false;
        var replayed = await bridge.RetryFailedWritesAsync();

        replayed.Should().Be(1);
        bridge.PendingCompensationCount.Should().Be(0);
        (await store.GetAccessTokenAsync("default")).Should().Be("t1");
    }

    [Fact]
    public async Task UserBridge_WriteThroughFailure_IsCountedAndCompensated()
    {
        var userStore = new CapturingUserStore { FailWrites = true };
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(
            userStore,
            TokenStoreBackedTokenCache<CredentialToken>.DefaultUserKeyMapper,
            TenantAdapter, TenantFactory);

        var act = () => bridge.Set("u1", MakeToken("u1-access", 3600));

        act.Should().NotThrow("用户维度写穿失败同样不得打断管线（不变量 #5）");
        bridge.WriteThroughFailures.Should().Be(1);
        bridge.PendingCompensationCount.Should().Be(1);

        userStore.FailWrites = false;
        var replayed = await bridge.RetryFailedWritesAsync();

        replayed.Should().Be(1);
        bridge.PendingCompensationCount.Should().Be(0);
        (await userStore.GetAccessTokenAsync("u1", "u1")).Should().Be("u1-access", "重放成功后持久层补齐");
    }

    [Fact]
    public async Task BackgroundWriteFault_LandsInCompensationQueue()
    {
        var store = new SlowFaultingStore();
        var bridge = CreateTenantBridge(store);

        bridge.Set("default", MakeToken("t1", 3600));

        // 真异步故障走后台续体：等待在途计数归零（含 5s 兜底，防 CI 抖动）
        var sw = Stopwatch.StartNew();
        while (bridge.PendingWriteThroughCount > 0 && sw.ElapsedMilliseconds < 5000)
            await Task.Delay(10);
        bridge.PendingWriteThroughCount.Should().Be(0);
        bridge.WriteThroughFailures.Should().Be(1);
        bridge.PendingCompensationCount.Should().Be(1);
    }

    // ---- 水合 ----

    [Fact]
    public async Task HydrateAsync_LoadsStoreEntriesIntoMirror()
    {
        var store = new CapturingTenantStore();
        await store.SetAccessTokenAsync("default", "hydrated-token", 3600);
        await store.SetRefreshTokenAsync("default", "hydrated-rt");
        var bridge = CreateTenantBridge(store);

        var hydrated = await bridge.HydrateAsync();

        hydrated.Should().Be(1);
        bridge.TryGet("default", out var value).Should().BeTrue();
        value!.AccessToken.Should().Be("hydrated-token");
        value.RefreshToken.Should().Be("hydrated-rt");
    }

    [Fact]
    public async Task HydrateAsync_DoesNotOverwriteExistingMirrorEntry()
    {
        var store = new CapturingTenantStore();
        await store.SetAccessTokenAsync("default", "stale-store-token", 3600);
        var bridge = CreateTenantBridge(store);
        bridge.Set("default", MakeToken("fresh-local-token", 3600));

        await bridge.HydrateAsync();

        bridge.TryGet("default", out var value).Should().BeTrue();
        value!.AccessToken.Should().Be("fresh-local-token", "本地在途新值优先于 store 旧值");
    }

    [Fact]
    public async Task HydrateUserAsync_LoadsUserEntriesIntoMirror()
    {
        var userStore = new CapturingUserStore();
        await userStore.SetAccessTokenAsync("u1", "UserAccessToken", "u1-token", 3600);
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(
            userStore,
            TokenStoreBackedTokenCache<CredentialToken>.DefaultUserKeyMapper,
            TenantAdapter, TenantFactory);

        var hydrated = await bridge.HydrateUserAsync("u1");

        hydrated.Should().Be(1);
        bridge.TryGet("UserAccessToken", out var value).Should().BeTrue();
        value!.AccessToken.Should().Be("u1-token");
    }

    // ---- 用户维度（C7：绝不调用无 userId 成员） ----

    [Fact]
    public async Task UserBridge_WritesThroughWithUserIdMapping()
    {
        var userStore = new CapturingUserStore();
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(
            userStore,
            TokenStoreBackedTokenCache<CredentialToken>.DefaultUserKeyMapper,
            TenantAdapter, TenantFactory);

        bridge.Set("u1", MakeToken("u1-access", 3600));
        bridge.Set("u2\u001Fscope-a", MakeToken("u2-access", 7200));

        userStore.AccessWrites.Should().Contain(w => w.UserId == "u1" && w.TokenType == "u1" && Math.Abs(w.ExpiresInSeconds - 3600) < 15);
        userStore.AccessWrites.Should().Contain(w => w.UserId == "u2" && w.TokenType == "scope-a" && Math.Abs(w.ExpiresInSeconds - 7200) < 15);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task UserBridge_Clear_RemovesPerUser_NeverCallsClearAll()
    {
        var userStore = new CapturingUserStore();
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(
            userStore,
            TokenStoreBackedTokenCache<CredentialToken>.DefaultUserKeyMapper,
            TenantAdapter, TenantFactory);
        bridge.Set("u1", MakeToken("a1", 3600));
        bridge.Set("u2", MakeToken("a2", 3600));

        bridge.Clear();

        bridge.Keys.Should().BeEmpty();
        userStore.Removes.Should().HaveCount(2);
        userStore.ClearAllCalls.Should().Be(0, "IUserTokenStore.ClearAsync 会清空全部用户，桥接器必须逐键删除（§5.3.1 ④）");
        await Task.CompletedTask;
    }

    // ---- Compact：镜像 LRU + 回调，不回删 store ----

    [Fact]
    public void Compact_Full_EvictsAllAndFiresCallbacks_DoesNotTouchStore()
    {
        var store = new CapturingTenantStore();
        var evicted = new List<string>();
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(store, TenantAdapter, TenantFactory);
        bridge.Set("k1", MakeToken("t1", 3600), null, null, k => evicted.Add(k));
        bridge.Set("k2", MakeToken("t2", 3600));

        bridge.Compact(1.0);

        bridge.Keys.Should().BeEmpty();
        evicted.Should().BeEquivalentTo(new[] { "k1" });
        store.Removes.Should().BeEmpty("镜像驱逐是内存压力行为，不是失效语义，不回删 store");
    }

    [Fact]
    public void Compact_Partial_RemovesExpectedShare()
    {
        var store = new CapturingTenantStore();
        var bridge = CreateTenantBridge(store);
        for (var i = 0; i < 10; i++)
            bridge.Set("k" + i, MakeToken("t" + i, 3600));

        bridge.Compact(0.5);

        bridge.Count.Should().Be(5);
    }

    // ---- 管线端到端（C6 的最终防线：管理器失效必落到 store） ----

    [Fact]
    public async Task ManagerPipeline_Invalidation_PropagatesToStore()
    {
        var store = new CapturingTenantStore();
        var bridge = CreateTenantBridge(store);
        var manager = new BridgeTestManager(bridge);

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().NotBeNullOrEmpty();
        bridge.Keys.Should().ContainSingle();
        var storeKey = bridge.Keys.Single();
        (await store.GetAccessTokenAsync(storeKey)).Should().NotBeNull("刷新结果必须写穿到持久层");

        await manager.InvalidateTokenAsync();

        bridge.Keys.Should().BeEmpty();
        store.Removes.Should().Contain(storeKey);
        (await store.GetAccessTokenAsync(storeKey)).Should().BeNull("管理器失效必须同步删除持久层条目（防登出漏删 / C6）");
    }

    [Fact]
    public async Task ManagerPipeline_ColdStartWithHydrate_AvoidsRefresh()
    {
        var store = new CapturingTenantStore();
        await store.SetAccessTokenAsync("default", "pre-seeded-token", 3600);
        var bridge = CreateTenantBridge(store);
        await bridge.HydrateAsync();
        var manager = new BridgeTestManager(bridge);

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("pre-seeded-token", "水合后的令牌应经管线有效性判定直接命中，不再触发刷新");
    }

    // ---- S2：IAsyncTokenCache<T> 真穿透（消除"必须先水合"限制） ----

    [Fact]
    public async Task GetAsync_ReadsThroughToStore_WhenMirrorMisses()
    {
        var store = new CapturingTenantStore();
        await store.SetAccessTokenAsync("default", "store-token", 3600);
        await store.SetRefreshTokenAsync("default", "store-rt");
        var bridge = CreateTenantBridge(store);

        var value = await bridge.GetAsync("default");

        value!.AccessToken.Should().Be("store-token", "镜像未命中时读穿透直达 store");
        bridge.Keys.Should().Contain("default", "穿透结果回填镜像（后续同步 TryGet 可命中）");
        bridge.TryGet("default", out var mirrored).Should().BeTrue();
        mirrored!.AccessToken.Should().Be("store-token");
    }

    [Fact]
    public async Task GetAsync_MirrorHit_DoesNotTouchStore()
    {
        var store = new CapturingTenantStore();
        var bridge = CreateTenantBridge(store);
        bridge.Set("default", MakeToken("mirror-token", 3600));

        var value = await bridge.GetAsync("default");

        value!.AccessToken.Should().Be("mirror-token", "镜像优先，保住热路径零 I/O（方案 B 限制）");
    }

    [Fact]
    public async Task SetAsync_WaitsForStoreWrite()
    {
        var store = new CapturingTenantStore();
        var bridge = CreateTenantBridge(store);

        await bridge.SetAsync("default", MakeToken("async-written", 3600));

        store.AccessWrites.Should().ContainSingle().Which.AccessToken.Should().Be("async-written");
        (await store.GetAccessTokenAsync("default")).Should().Be("async-written");
    }

    [Fact]
    public async Task SetAsync_AdapterReturnsNull_DeletesStoreKey_LikeSyncSet()
    {
        var store = new CapturingTenantStore();
        // 值适配器对"空访问令牌"返回 null（= 删除该键，§5.3.1 ②）
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(
            store,
            token => token == null || string.IsNullOrEmpty(token.AccessToken) ? null : TenantAdapter(token),
            TenantFactory);

        // 同步路径基准：适配器 null → 写穿删除持久层
        await store.SetAccessTokenAsync("default", "stale-1", 3600);
        bridge.Set("default", MakeToken(string.Empty, 0));
        store.Removes.Should().Contain("default", "同步 Set 的适配器 null = 删除该键");

        // 回归（评审修正）：异步路径此前对适配器 null 静默 no-op，与同步语义分歧
        await store.SetAccessTokenAsync("default", "stale-2", 3600);
        await bridge.SetAsync("default", MakeToken(string.Empty, 0));

        store.Removes.Should().Contain("default");
        (await store.GetAccessTokenAsync("default")).Should().BeNull("异步路径必须同样落到持久层删除，防失效条目残留 store");
    }

    [Fact]
    public async Task RemoveAsync_PropagatesToStore_AndReturnsRemovedValue()
    {
        var store = new CapturingTenantStore();
        var bridge = CreateTenantBridge(store);
        bridge.Set("default", MakeToken("to-remove", 3600));

        var removed = await bridge.RemoveAsync("default");

        removed!.AccessToken.Should().Be("to-remove");
        bridge.Keys.Should().BeEmpty();
        (await store.GetAccessTokenAsync("default")).Should().BeNull("失效必须落到持久层（防 C6）");
    }

    [Fact]
    public async Task ManagerPipeline_ColdStartWithoutHydrate_ReadsThroughStore()
    {
        var store = new CapturingTenantStore();
        await store.SetAccessTokenAsync("default", "pre-seeded-token", 3600);
        var bridge = CreateTenantBridge(store);
        var manager = new BridgeTestManager(bridge);
        // 关键：不调用 HydrateAsync —— S2 读穿透应直接命中 store

        var token = await manager.GetOrRefreshTokenAsync();

        token.Should().Be("pre-seeded-token", "S2 能力探测使管理器在异步路径上经 GetAsync 穿透 store，冷启动无需显式水合");
    }

    [Fact]
    public async Task UserBridge_GetAsync_ReadsThroughWithUserIdMapping()
    {
        var userStore = new CapturingUserStore();
        // 按 DefaultUserKeyMapper 的键约定播种：裸键 "u1" ↔ (userId="u1", tokenType="u1")
        await userStore.SetAccessTokenAsync("u1", "u1", "u1-rt-token", 3600);
        var bridge = new TokenStoreBackedTokenCache<CredentialToken>(
            userStore,
            TokenStoreBackedTokenCache<CredentialToken>.DefaultUserKeyMapper,
            TenantAdapter, TenantFactory);

        var value = await bridge.GetAsync("u1");

        value!.AccessToken.Should().Be("u1-rt-token", "用户维度穿透按 userKeyMapper 把 cacheKey 映射为 (userId, tokenType)");
    }

    /// <summary>最小用户管线子类：刷新产出固定令牌（1 小时有效），用于验证用户级读穿透。</summary>
    private sealed class BridgeUserTestManager : UserTokenManagerBase
    {
        public BridgeUserTestManager(ITokenCache<UserTokenInfo> userTokenCache) : base(userTokenCache)
        {
        }

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("用户令牌管理器必须提供 userId");

        public override Task<TokenResult> InvalidateUserTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(TokenResult.Empty);

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<UserTokenInfo?>(new UserTokenInfo
            {
                AccessToken = "minted-user-token",
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(MakeToken("minted-tenant-token", 3600));

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(string userId, string tokenType, CancellationToken cancellationToken)
            => Task.FromResult<UserTokenInfo?>(null);
    }

    [Fact]
    public async Task UserManagerPipeline_ColdStartWithoutHydrate_ReadsThroughStore()
    {
        var userStore = new CapturingUserStore();
        await userStore.SetAccessTokenAsync("u1", "u1", "seeded-user-token", 3600);
        var bridge = new TokenStoreBackedTokenCache<UserTokenInfo>(
            userStore,
            TokenStoreBackedTokenCache<UserTokenInfo>.DefaultUserKeyMapper,
            token => token == null
                ? null
                : new TokenStoreValue(token.AccessToken, token.RefreshToken,
                    (token.AccessTokenExpireTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000),
            value => new UserTokenInfo
            {
                AccessToken = value.AccessToken,
                RefreshToken = value.RefreshToken,
                AccessTokenExpireTime = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
                IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        var manager = new BridgeUserTestManager(bridge);

        var token = await manager.GetOrRefreshTokenAsync("u1");

        token.Should().Be("seeded-user-token", "用户级读穿透：镜像未命中直达 store，不触发刷新");
    }

    /// <summary>最小管线子类：注入桥接器，刷新产出固定令牌（1 小时有效）。</summary>
    private sealed class BridgeTestManager : TokenManagerBase
    {
        public BridgeTestManager(ITokenCache<CredentialToken> tokenCache) : base(tokenCache)
        {
        }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(MakeToken("minted-token", 3600));
    }
}
