// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// F-2（TK-1）：<see cref="TokenScopeKey"/> 定位符形态与既有 <c>string[] scopes</c> 路径的
/// <b>单缓存互通</b>（同键、同条目、同一次刷新互不重复），以及 tokenType / appKey 段的
/// 一致性校验（只校验、不进缓存键）。
/// </summary>
public class TokenScopeKeyLocatorTests
{
    /// <summary>记录所有触达缓存键的测试缓存（供断言"tokenType/appKey 不进缓存键"）。</summary>
    private sealed class RecordingTokenCache : ITokenCache<CredentialToken>
    {
        private readonly Dictionary<string, CredentialToken> _store = new(StringComparer.Ordinal);

        public List<string> TouchedKeys { get; } = new();

        public bool TryGet(string key, out CredentialToken? value)
        {
            TouchedKeys.Add(key);
            return _store.TryGetValue(key, out value);
        }

        public void Set(string key, CredentialToken? value)
        {
            TouchedKeys.Add(key);
            if (value == null)
                _store.Remove(key);
            else
                _store[key] = value;
        }

        public void Set(string key, CredentialToken? value, TimeSpan? absoluteExpirationRelativeToNow, TimeSpan? slidingExpiration, Action<string>? postEvictionCallback = null)
            => Set(key, value);

        public bool TryRemove(string key, out CredentialToken? removed)
        {
            TouchedKeys.Add(key);
            if (_store.TryGetValue(key, out removed))
            {
                _store.Remove(key);
                return true;
            }

            removed = null;
            return false;
        }

        public int Count => _store.Count;
        public IEnumerable<string> Keys => _store.Keys.ToArray();
        public void Clear() => _store.Clear();
        public void Compact(double percentage) { }
        public void Dispose() { }
    }

    /// <summary>计数刷新的探针管理器：可声明 <c>TokenType</c>（供定位符校验基准）。</summary>
    private sealed class LocatorProbeManager : TokenManagerBase
    {
        private readonly string? _declaredTokenType;
        private int _refreshCount;

        public LocatorProbeManager(ITokenCache<CredentialToken> tokenCache, string? declaredTokenType = null)
            : base(tokenCache)
        {
            _declaredTokenType = declaredTokenType;
        }

        public int RefreshCount => Volatile.Read(ref _refreshCount);

        protected override string? TokenType => _declaredTokenType;

        public void Bind(string tenantKey, string? scopeKey = null) => BindTenantGuard(tenantKey, scopeKey);

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _refreshCount);
            return Task.FromResult(new CredentialToken
            {
                AccessToken = $"token-{Guid.NewGuid():N}",
                Expire = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()
            });
        }
    }

    // ── 单缓存互通 ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrRefresh_ForScope_ShouldShareCacheEntryWithLegacyScopesPath()
    {
        var cache = new RecordingTokenCache();
        using var manager = new LocatorProbeManager(cache);

        var viaLocator = await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("corp.token", "app-a", "s1"));
        var viaLegacy = await manager.GetOrRefreshTokenAsync(new[] { "s1" });

        viaLegacy.Should().Be(viaLocator, "ForScope(t,a,s) 与 scopes=[s] 必须命中同一缓存条目");
        manager.RefreshCount.Should().Be(1, "同一次刷新互不重复（单缓存互通）");
        cache.TouchedKeys.Distinct(StringComparer.Ordinal)
            .Should().Equal(new[] { ScopeKeyBuilder.Build(new[] { "s1" }) }, "唯一缓存键由 scopeKey 段经旧路径生成");
    }

    [Fact]
    public async Task GetOrRefresh_ForAppAndForShared_ShouldShareDefaultCacheEntryWithNullScopesPath()
    {
        var cache = new RecordingTokenCache();
        using var manager = new LocatorProbeManager(cache);

        var viaForApp = await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForApp("corp.token", "app-a"));
        var viaLegacyNull = await manager.GetOrRefreshTokenAsync((string[]?)null);
        var viaForShared = await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForShared("corp.token"));

        viaLegacyNull.Should().Be(viaForApp, "ForApp(t,a) 与 scopes=null 命中同一 default 条目");
        viaForShared.Should().Be(viaForApp, "ForShared(t) 与 scopes=null 命中同一 default 条目");
        manager.RefreshCount.Should().Be(1);
        cache.TouchedKeys.Distinct(StringComparer.Ordinal)
            .Should().Equal(new[] { ScopeKeyBuilder.DefaultKey }, "ForApp/ForShared 均映射 default 键");
    }

    [Fact]
    public async Task GetOrRefresh_LocatorSegments_ShouldNotParticipateInCacheKey()
    {
        // tokenType / appKey 段只做一致性校验、绝不参与缓存键：不同段组合、相同 scopeKey ⇒ 同一条目。
        var cache = new RecordingTokenCache();
        using var manager = new LocatorProbeManager(cache);

        var first = await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("type-1", "app-a", "s1"));
        var second = await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("type-2", "app-b", "s1"));

        second.Should().Be(first, "tokenType/appKey 段不同不得分裂缓存条目");
        manager.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrRefresh_ConcurrentLocatorAndLegacyPaths_ShouldRefreshOnce()
    {
        var cache = new RecordingTokenCache();
        using var manager = new LocatorProbeManager(cache);

        var locatorTask = manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("corp.token", "app-a", "s1"));
        var legacyTask = manager.GetOrRefreshTokenAsync(new[] { "s1" });
        var results = await Task.WhenAll(locatorTask, legacyTask);

        results[0].Should().Be(results[1], "并发路径殊途同归于同一条目");
        manager.RefreshCount.Should().Be(1, "键控锁去重：同一次刷新互不重复");
    }

    [Fact]
    public async Task Invalidate_ForScope_ShouldInteropWithLegacyScopesPath()
    {
        // 旧路径写入 → 定位符失效：必须命中同一条目。
        var cache = new RecordingTokenCache();
        using var manager = new LocatorProbeManager(cache);
        await manager.GetOrRefreshTokenAsync(new[] { "s1" });

        var removed = await manager.InvalidateTokenAsync(TokenScopeKey.ForScope("corp.token", "app-a", "s1"));

        removed.AccessToken.Should().NotBeNull("定位符失效必须命中旧路径写入的条目");
        manager.RefreshCount.Should().Be(1);

        await manager.GetOrRefreshTokenAsync(new[] { "s1" });
        manager.RefreshCount.Should().Be(2, "失效后旧路径重刷（互通双向）");
    }

    [Fact]
    public async Task Invalidate_LegacyPath_ShouldInteropWithLocator()
    {
        // 定位符写入 → 旧路径失效 → 定位符重刷。
        var cache = new RecordingTokenCache();
        using var manager = new LocatorProbeManager(cache);
        await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("corp.token", "app-a", "s1"));

        var removed = await manager.InvalidateTokenAsync(new[] { "s1" });

        removed.AccessToken.Should().NotBeNull();
        manager.RefreshCount.Should().Be(1);

        await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("corp.token", "app-a", "s1"));
        manager.RefreshCount.Should().Be(2);
    }

    // ── tokenType / appKey 一致性校验 ──────────────────────────────────

    [Fact]
    public async Task GetOrRefresh_TokenTypeMismatch_ShouldThrowArgumentException()
    {
        using var manager = new LocatorProbeManager(new RecordingTokenCache(), declaredTokenType: "corp.token");

        var act = async () => await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("other.type", "app-a", "s1"));

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithParameterName("scopeKey")
            .WithMessage("*令牌类型不一致*", "校验失败须给出可定位的诊断消息");
    }

    [Fact]
    public async Task GetOrRefresh_AppKeyMismatchAfterBinding_ShouldThrowArgumentException()
    {
        using var manager = new LocatorProbeManager(new RecordingTokenCache(), declaredTokenType: "corp.token");
        manager.Bind("app-a");

        var act = async () => await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("corp.token", "app-b", "s1"));

        (await act.Should().ThrowAsync<ArgumentException>())
            .WithParameterName("scopeKey")
            .WithMessage("*应用归属不一致*");
    }

    [Fact]
    public async Task Invalidate_AnySegmentMismatch_ShouldThrowArgumentException()
    {
        using var manager = new LocatorProbeManager(new RecordingTokenCache(), declaredTokenType: "corp.token");
        manager.Bind("app-a");

        var tokenTypeAct = async () => await manager.InvalidateTokenAsync(TokenScopeKey.ForScope("other.type", "app-a", "s1"));
        (await tokenTypeAct.Should().ThrowAsync<ArgumentException>()).WithParameterName("scopeKey");

        var appKeyAct = async () => await manager.InvalidateTokenAsync(TokenScopeKey.ForApp("corp.token", "app-b"));
        (await appKeyAct.Should().ThrowAsync<ArgumentException>()).WithParameterName("scopeKey");
    }

    [Fact]
    public async Task GetOrRefresh_MatchingSegments_ShouldPassAndHitLegacyEntry()
    {
        var cache = new RecordingTokenCache();
        using var manager = new LocatorProbeManager(cache, declaredTokenType: "corp.token");
        manager.Bind("app-a");

        var token = await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("corp.token", "app-a", "s1"));
        var viaLegacy = await manager.GetOrRefreshTokenAsync(new[] { "s1" });

        viaLegacy.Should().Be(token, "校验通过后仍与旧路径单缓存互通");
        manager.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrRefresh_SharedLocatorUnderBoundTenant_ShouldSkipAppKeyCheck()
    {
        // ForShared 无 appKey 段：即使上下文已绑定租户也跳过 appKey 校验。
        using var manager = new LocatorProbeManager(new RecordingTokenCache(), declaredTokenType: "corp.token");
        manager.Bind("app-a");

        var token = await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForShared("corp.token"));

        token.Should().NotBeNull("ForShared 形态无 appKey 段，跳过 appKey 一致性校验");
    }

    [Fact]
    public async Task GetOrRefresh_UnboundManager_ShouldSkipAppKeyCheck()
    {
        // 尚未绑定租户：无上下文 AppKey 可对照，appKey 段跳过校验。
        using var manager = new LocatorProbeManager(new RecordingTokenCache(), declaredTokenType: "corp.token");

        var token = await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("corp.token", "any-app", "s1"));

        token.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrRefresh_UndeclaredTokenType_ShouldSkipTokenTypeCheck()
    {
        // TokenType 未声明（默认 null）：tokenType 段跳过校验。
        using var manager = new LocatorProbeManager(new RecordingTokenCache());

        var token = await manager.GetOrRefreshTokenAsync(TokenScopeKey.ForScope("any.type", "app-a", "s1"));

        token.Should().NotBeNull();
    }
}
