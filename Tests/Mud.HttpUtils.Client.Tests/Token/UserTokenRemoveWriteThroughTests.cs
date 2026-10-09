// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// B3 回归护栏：用户维度移除路径（<c>RemoveTokenAsync</c> / <c>InvalidateUserTokenAsync</c>）
/// 必须在同步镜像清理之外补齐<b>异步写穿</b>，与租户维度（<c>TokenManagerBase</c> 的
/// <c>IAsyncTokenCache&lt;CredentialToken&gt;.RemoveAsync</c>）对齐。
/// </summary>
/// <remarks>
/// <para>
/// 缺陷场景（C6 类）：桥接式缓存（<c>TokenStoreBackedTokenCache&lt;T&gt;</c>）的同步 <c>TryRemove</c>
/// 在<b>镜像未命中</b>（冷启动 / 重启 / 多实例）时<b>不写穿持久层</b>，于是登出后持久层仍留有旧令牌，
/// 下次读穿透（<c>GetAsync</c> 直达 store）会把已失效令牌"复活"。
/// </para>
/// <para>
/// 本用例族以冷缓存（镜像为空）为起点 —— 这正是修复前唯一会漏写的路径。
/// </para>
/// </remarks>
public class UserTokenRemoveWriteThroughTests
{
    // ── 桥接器装配：用户维度 ─────────────────────────────────────────────

    private static readonly Func<UserTokenInfo?, TokenStoreValue?> ValueAdapter = tokenInfo =>
        tokenInfo is null
            ? null
            : new TokenStoreValue(
                tokenInfo.AccessToken,
                tokenInfo.RefreshToken,
                tokenInfo.AccessTokenExpireTime > 0
                    ? Math.Max(0, (tokenInfo.AccessTokenExpireTime - NowMs()) / 1000)
                    : 0);

    private static readonly Func<TokenStoreValue, UserTokenInfo?> ValueFactory = storeValue => new UserTokenInfo
    {
        UserId = string.Empty,
        AccessToken = storeValue.AccessToken,
        RefreshToken = storeValue.RefreshToken,
        AccessTokenExpireTime = storeValue.ExpiresInSeconds > 0
            ? NowMs() + (storeValue.ExpiresInSeconds * 1000)
            : 0,
    };

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static TokenStoreBackedTokenCache<UserTokenInfo> CreateUserBridge(RecordingUserStore store)
        => new(
            store,
            TokenStoreBackedTokenCache<UserTokenInfo>.DefaultUserKeyMapper,
            ValueAdapter,
            ValueFactory);

    // ── ① 冷启动写穿 ───────────────────────────────────────────────────

    /// <summary>
    /// 镜像未命中（冷启动）时 <c>RemoveTokenAsync</c> 仍须把删除落到持久层。
    /// 修复前：同步 <c>TryRemove</c> 未命中直接 return false ⇒ 持久层无任何调用。
    /// </summary>
    [Fact]
    public async Task RemoveTokenAsync_ColdCache_ShouldWriteThroughToStore()
    {
        var store = new RecordingUserStore();
        using var manager = new TestUserTokenManager(CreateUserBridge(store));

        var removed = await manager.RemoveTokenAsync("u");

        removed.Should().BeTrue();
        store.Removes.Should().Contain(
            r => r.UserId == "u" && r.TokenType == "u",
            "冷启动登出必须无条件写穿持久层（对齐租户路径 IAsyncTokenCache.RemoveAsync）");
    }

    /// <summary>
    /// 同一约束适用于 <c>InvalidateUserTokenAsync</c>（登出语义的另一公共入口）。
    /// </summary>
    [Fact]
    public async Task InvalidateUserTokenAsync_ColdCache_ShouldWriteThroughToStore()
    {
        var store = new RecordingUserStore();
        using var manager = new TestUserTokenManager(CreateUserBridge(store));

        await manager.InvalidateUserTokenAsync("u");

        store.Removes.Should().Contain(r => r.UserId == "u" && r.TokenType == "u");
    }

    // ── ② scope 化复合键 ───────────────────────────────────────────────

    /// <summary>
    /// scope 化复合键（<c>userId + U+001F + scopeKey</c>）同样必须写穿，
    /// 且键必须在同步清理<b>之前</b>取快照（清理会清空镜像，事后前缀扫描已无键可扫）。
    /// </summary>
    [Fact]
    public async Task RemoveTokenAsync_WithScopedEntries_ShouldWriteThroughAllCompositeKeys()
    {
        var store = new RecordingUserStore();
        using var manager = new TestUserTokenManager(CreateUserBridge(store));

        (await manager.GetOrRefreshTokenAsync("u", new[] { "read:admin" })).Should().Be("token-for-u");
        (await manager.GetOrRefreshTokenAsync("u", new[] { "read:basic" })).Should().Be("token-for-u");
        store.Removes.Clear();

        await manager.RemoveTokenAsync("u");

        store.Removes.Should().Contain(r => r.UserId == "u" && r.TokenType == "u",
            "裸键（默认作用域）必须写穿");
        store.Removes.Should().Contain(r => r.UserId == "u" && r.TokenType == "read:admin",
            "scope 化复合键必须写穿（否则持久层残留 ⇒ 读穿透复活已登出令牌）");
        store.Removes.Should().Contain(r => r.UserId == "u" && r.TokenType == "read:basic");
    }

    // ── ③ 非异步缓存回归 ───────────────────────────────────────────────

    /// <summary>
    /// 非异步缓存实现（默认 <c>MemoryCacheTokenCache&lt;T&gt;</c>）下行为与旧版逐字节等价：
    /// 不产生额外调用、返回值与异常语义不变。
    /// </summary>
    [Fact]
    public async Task RemoveTokenAsync_NonAsyncCache_ShouldBehaveAsBefore()
    {
        using var manager = new TestUserTokenManager();

        (await manager.RemoveTokenAsync("u")).Should().BeTrue();
        (await manager.HasValidTokenAsync("u")).Should().BeFalse();
    }

    /// <summary>
    /// 校验失败仍<b>同步抛出</b>（改为异步实现不得把既有同步异常搬进返回的 Task）。
    /// </summary>
    [Fact]
    public void RemoveTokenAsync_InvalidUserId_ShouldThrowSynchronously()
    {
        using var manager = new TestUserTokenManager();

        // 直接以 Action 调用：若异常被搬进返回的 Task（改为 async 实现的常见副作用），
        // 此处不会捕获到异常，断言即失败。
        var exception = Record.Exception(() =>
        {
            _ = manager.RemoveTokenAsync("bad\u001Fuser");
        });

        exception.Should().BeOfType<ArgumentException>("userId 含复合键分隔符必须在调用点同步拒绝");
    }

    // ── 测试替身 ───────────────────────────────────────────────────────

    /// <summary>仅记录调用的用户维度 store（继承自 ITokenStore 的 7 个无 userId 成员按契约抛 NotSupportedException）。</summary>
    private sealed class RecordingUserStore : IUserTokenStore
    {
        public List<(string UserId, string TokenType)> Removes { get; } = new();
        public List<(string UserId, string TokenType, string? Access, string? Refresh)> Writes { get; } = new();

        // ── IUserTokenStore（带 userId，桥接器唯一允许使用的成员） ──
        public Task<string?> GetAccessTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task SetAccessTokenAsync(string userId, string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
        {
            Writes.Add((userId, tokenType, accessToken, null));
            return Task.CompletedTask;
        }

        public Task<string?> GetRefreshTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task SetRefreshTokenAsync(string userId, string tokenType, string refreshToken, CancellationToken cancellationToken = default)
        {
            Writes.Add((userId, tokenType, null, refreshToken));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
        {
            Removes.Add((userId, tokenType));
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> GetTokenTypesAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public Task ClearUserAsync(string userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        // ── ITokenStore（无 userId，语义未定义 ⇒ 契约允许抛） ──
        public Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员");

        public Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员");

        public Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员");

        public Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员");

        public Task RemoveAsync(string tokenType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员");

        public Task<IEnumerable<string>> GetTokenTypesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员");

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("桥接器不得调用无 userId 成员");
    }

    /// <summary>最小可用的用户令牌管理器（刷新返回可缓存的未来过期令牌）。</summary>
    private sealed class TestUserTokenManager : UserTokenManagerBase
    {
        public TestUserTokenManager(ITokenCache<UserTokenInfo>? userTokenCache = null)
            : base(userTokenCache)
        {
        }

        public override Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(cancellationToken);

        public override Task<string?> GetTokenAsync(string? userId, CancellationToken cancellationToken = default)
            => GetOrRefreshTokenAsync(userId, cancellationToken);

        protected override Task<CredentialToken> RefreshTokenCoreAsync(CancellationToken cancellationToken)
            => Task.FromResult(new CredentialToken
            {
                AccessToken = "tenant-token",
                Expire = NowMs() + 3_600_000,
            });

        public override Task<UserTokenInfo?> GetTokenInfoAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> GetUserTokenWithCodeAsync(
            string code, string redirectUri, CancellationToken cancellationToken = default)
            => Task.FromResult<UserTokenInfo?>(null);

        public override Task<UserTokenInfo?> RefreshUserTokenAsync(
            string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new UserTokenInfo
            {
                UserId = userId,
                AccessToken = $"token-for-{userId}",
                AccessTokenExpireTime = NowMs() + 3_600_000,
                RefreshToken = "refresh-token",
            });

        public override Task<bool> CanRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
