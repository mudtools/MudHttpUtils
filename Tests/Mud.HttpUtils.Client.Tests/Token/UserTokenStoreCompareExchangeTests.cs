// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// U-7（用户维度扩展）：<see cref="IUserTokenStoreCompareExchange"/> 契约与
/// <see cref="UserTokenStoreCompareExchangeExtensions"/> 统一入口 ——
/// 内存实现原子 CAS（命中/未命中/删除不误伤另一维度/构造守卫/并发不丢更新）、
/// 回退路径（非能力实现走读取-比较-写入）、能力探测优先。
/// </summary>
public class UserTokenStoreCompareExchangeTests
{
    private const string User = "user-1";
    private const string Type = "UserAccessToken";

    // ── 内存实现（原子能力）────────────────────────────────────────────

    [Fact]
    public async Task Memory_AccessMatch_Should_Swap_And_PreserveRefreshToken()
    {
        var store = new MemoryUserTokenStore();
        await store.SetAccessTokenAsync(User, Type, "access-old", 3600);
        await store.SetRefreshTokenAsync(User, Type, "refresh-1");

        var swapped = await store.CompareExchangeAccessTokenAsync(User, Type, "access-old", "access-new", 7200);

        swapped.Should().BeTrue();
        (await store.GetAccessTokenAsync(User, Type)).Should().Be("access-new");
        (await store.GetRefreshTokenAsync(User, Type)).Should().Be("refresh-1", "仅替换访问维度，刷新令牌不受影响");
        (await store.GetTokenTypesAsync(User)).Should().ContainSingle();
    }

    [Fact]
    public async Task Memory_AccessMismatch_Should_ReturnFalse_WithoutWriting()
    {
        var store = new MemoryUserTokenStore();
        await store.SetAccessTokenAsync(User, Type, "access-old", 3600);

        var swapped = await store.CompareExchangeAccessTokenAsync(User, Type, "not-the-current", "access-new", 3600);

        swapped.Should().BeFalse("不匹配即不做任何写入");
        (await store.GetAccessTokenAsync(User, Type)).Should().Be("access-old");
    }

    [Fact]
    public async Task Memory_MissingKey_Should_ReturnFalse()
    {
        var store = new MemoryUserTokenStore();

        (await store.CompareExchangeAccessTokenAsync(User, Type, "any", "new", 60)).Should().BeFalse();
        (await store.CompareExchangeRefreshTokenAsync(User, Type, "any", "new")).Should().BeFalse();
    }

    [Fact]
    public async Task Memory_AccessDelete_Should_ClearOnlyAccessDimension()
    {
        var store = new MemoryUserTokenStore();
        await store.SetAccessTokenAsync(User, Type, "access-old", 3600);
        await store.SetRefreshTokenAsync(User, Type, "refresh-1");

        var removed = await store.CompareExchangeAccessTokenAsync(User, Type, "access-old", null);

        removed.Should().BeTrue();
        (await store.GetAccessTokenAsync(User, Type)).Should().BeNull("访问维度已清空");
        (await store.GetRefreshTokenAsync(User, Type)).Should().Be("refresh-1", "不得误删刷新维度");
    }

    [Fact]
    public async Task Memory_Delete_WhenOtherDimensionEmpty_Should_RemoveWholeEntry()
    {
        var store = new MemoryUserTokenStore();
        await store.SetAccessTokenAsync(User, Type, "access-only", 3600);

        (await store.CompareExchangeAccessTokenAsync(User, Type, "access-only", null)).Should().BeTrue();

        (await store.GetTokenTypesAsync(User)).Should().BeEmpty("双维度皆空 → 整条移除（等价两键皆删）");
    }

    [Fact]
    public async Task Memory_RefreshMatch_Should_Swap_AndPreserveAccessToken()
    {
        var store = new MemoryUserTokenStore();
        await store.SetAccessTokenAsync(User, Type, "access-1", 3600);
        await store.SetRefreshTokenAsync(User, Type, "refresh-old");

        var swapped = await store.CompareExchangeRefreshTokenAsync(User, Type, "refresh-old", "refresh-new");

        swapped.Should().BeTrue();
        (await store.GetRefreshTokenAsync(User, Type)).Should().Be("refresh-new");
        (await store.GetAccessTokenAsync(User, Type)).Should().Be("access-1");
    }

    [Fact]
    public async Task Memory_EmptyExpectedToken_Should_Throw()
    {
        var store = new MemoryUserTokenStore();

        await FluentActions.Awaiting(() => store.CompareExchangeAccessTokenAsync(User, Type, "", "new"))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => store.CompareExchangeRefreshTokenAsync(User, Type, "", "new"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Memory_ConcurrentExchanges_SameExpected_Should_ExactlyOneWin()
    {
        var store = new MemoryUserTokenStore();
        await store.SetAccessTokenAsync(User, Type, "old", 3600);

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(i => store.CompareExchangeAccessTokenAsync(User, Type, "old", $"new-{i}", 3600)));

        results.Count(r => r).Should().Be(1, "CAS 语义：同一期望值的并发交换恰有一个成功（原子性）");
        (await store.GetAccessTokenAsync(User, Type)).Should().StartWith("new-", "最终值必为某个胜出者的新值");
    }

    [Fact]
    public async Task Memory_ClearUser_Should_StillWork_AfterCas()
    {
        var store = new MemoryUserTokenStore();
        await store.SetAccessTokenAsync(User, Type, "a", 3600);
        await store.CompareExchangeAccessTokenAsync(User, Type, "a", "b", 3600);

        await store.ClearUserAsync(User);

        (await store.GetTokenTypesAsync(User)).Should().BeEmpty();
    }

    // ── 回退路径与能力探测 ─────────────────────────────────────────────

    /// <summary>仅实现 IUserTokenStore（无 CAS 能力）→ 走非原子回退。</summary>
    private class PlainUserStore : IUserTokenStore
    {
        private readonly Dictionary<(string User, string Type), (string? Access, string? Refresh)> _tokens = new();

        public List<string> Calls { get; } = new();

        public Task<string?> GetAccessTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
        {
            Calls.Add("get-access");
            return Task.FromResult(_tokens.TryGetValue((userId, tokenType), out var v) ? v.Access : null);
        }

        public Task SetAccessTokenAsync(string userId, string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
        {
            Calls.Add("set-access");
            var existing = _tokens.TryGetValue((userId, tokenType), out var v) ? v.Refresh : null;
            _tokens[(userId, tokenType)] = (accessToken, existing);
            return Task.CompletedTask;
        }

        public Task<string?> GetRefreshTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
        {
            Calls.Add("get-refresh");
            return Task.FromResult(_tokens.TryGetValue((userId, tokenType), out var v) ? v.Refresh : null);
        }

        public Task SetRefreshTokenAsync(string userId, string tokenType, string refreshToken, CancellationToken cancellationToken = default)
        {
            Calls.Add("set-refresh");
            var existing = _tokens.TryGetValue((userId, tokenType), out var v) ? v.Access : null;
            _tokens[(userId, tokenType)] = (existing, refreshToken);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
        {
            Calls.Add("remove");
            _tokens.Remove((userId, tokenType));
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> GetTokenTypesAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());

        public Task ClearUserAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        Task<string?> ITokenStore.GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task ITokenStore.SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<string?> ITokenStore.GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task ITokenStore.SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task ITokenStore.RemoveAsync(string tokenType, CancellationToken cancellationToken) => throw new NotSupportedException();
        Task<IEnumerable<string>> ITokenStore.GetTokenTypesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        Task ITokenStore.ClearAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>实现 CAS 能力的替身（验证探测优先，不得走回退）。</summary>
    private sealed class CapableUserStore : PlainUserStore, IUserTokenStoreCompareExchange
    {
        public int AccessCasCalls { get; private set; }

        public Task<bool> CompareExchangeAccessTokenAsync(string userId, string tokenType, string expectedToken, string? newToken, long expiresInSeconds = 0, CancellationToken cancellationToken = default)
        {
            AccessCasCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> CompareExchangeRefreshTokenAsync(string userId, string tokenType, string expectedToken, string? newToken, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    [Fact]
    public async Task Fallback_Match_Should_ReadCompareWrite_ViaPublicContract()
    {
        var store = new PlainUserStore();
        await store.SetAccessTokenAsync(User, Type, "old", 3600);
        store.Calls.Clear();

        var swapped = await store.CompareExchangeAccessTokenAsync(User, Type, "old", "new", 60);

        swapped.Should().BeTrue("非原子回退：读取 → 明文比较 → 写入");
        store.Calls.Should().Equal(new[] { "get-access", "set-access" });
        (await store.GetAccessTokenAsync(User, Type)).Should().Be("new");
    }

    [Fact]
    public async Task Fallback_Mismatch_Should_NotWrite()
    {
        var store = new PlainUserStore();
        await store.SetAccessTokenAsync(User, Type, "old", 3600);
        store.Calls.Clear();

        (await store.CompareExchangeAccessTokenAsync(User, Type, "wrong", "new", 60)).Should().BeFalse();

        store.Calls.Should().Equal(new[] { "get-access" }, "读取后不匹配即返回，不得触达写入");
    }

    [Fact]
    public async Task Fallback_Delete_Should_RemoveKey()
    {
        var store = new PlainUserStore();
        await store.SetRefreshTokenAsync(User, Type, "refresh-old");
        store.Calls.Clear();

        (await store.CompareExchangeRefreshTokenAsync(User, Type, "refresh-old", null)).Should().BeTrue();

        store.Calls.Should().Equal(new[] { "get-refresh", "remove" });
    }

    [Fact]
    public async Task CapabilityProbe_Should_Prefer_AtomicImplementation()
    {
        var store = new CapableUserStore();

        var swapped = await store.CompareExchangeAccessTokenAsync(User, Type, "any", "new", 60);

        swapped.Should().BeTrue();
        store.AccessCasCalls.Should().Be(1, "能力探测命中即委托原子实现");
        store.Calls.Should().BeEmpty("探测命中时不得走读取-比较-写入回退");
    }

    [Fact]
    public async Task MemoryStore_Should_Expose_AtomicCapability_ForProbePath()
    {
        var store = new MemoryUserTokenStore();

        store.Should().BeAssignableTo<IUserTokenStoreCompareExchange>("内存实现提供原子原语，探测路径直接命中");
        await store.SetRefreshTokenAsync(User, Type, "r-old");
        (await ((IUserTokenStore)store).CompareExchangeRefreshTokenAsync(User, Type, "r-old", "r-new")).Should().BeTrue();
    }
}
