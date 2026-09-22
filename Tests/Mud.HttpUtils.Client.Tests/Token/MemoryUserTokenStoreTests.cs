namespace Mud.HttpUtils.Client.Tests;

public class MemoryUserTokenStoreTests
{
    private readonly MemoryUserTokenStore _store;
    private readonly ITokenStore _tokenStore;

    public MemoryUserTokenStoreTests()
    {
        _store = new MemoryUserTokenStore();
        _tokenStore = (ITokenStore)_store;
    }

    #region Explicit ITokenStore Methods Throw NotSupportedException

    [Fact]
    public void GetAccessTokenAsync_WithoutUserId_ThrowsNotSupportedException()
    {
        var act = async () => await _tokenStore.GetAccessTokenAsync("TestToken");

        act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*userId*");
    }

    [Fact]
    public void SetAccessTokenAsync_WithoutUserId_ThrowsNotSupportedException()
    {
        var act = async () => await _tokenStore.SetAccessTokenAsync("TestToken", "access_123", 3600);

        act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*userId*");
    }

    [Fact]
    public void GetRefreshTokenAsync_WithoutUserId_ThrowsNotSupportedException()
    {
        var act = async () => await _tokenStore.GetRefreshTokenAsync("TestToken");

        act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*userId*");
    }

    [Fact]
    public void SetRefreshTokenAsync_WithoutUserId_ThrowsNotSupportedException()
    {
        var act = async () => await _tokenStore.SetRefreshTokenAsync("TestToken", "refresh_456");

        act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*userId*");
    }

    [Fact]
    public void RemoveAsync_WithoutUserId_ThrowsNotSupportedException()
    {
        var act = async () => await _tokenStore.RemoveAsync("TestToken");

        act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*userId*");
    }

    #endregion

    #region GetAccessTokenAsync (with userId)

    [Fact]
    public async Task GetAccessTokenAsync_WhenNotSet_ReturnsNull()
    {
        var result = await _store.GetAccessTokenAsync("user1", "TestToken");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenSet_ReturnsToken()
    {
        await _store.SetAccessTokenAsync("user1", "TestToken", "access_123", 3600);

        var result = await _store.GetAccessTokenAsync("user1", "TestToken");

        result.Should().Be("access_123");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenExpired_ReturnsNull()
    {
        await _store.SetAccessTokenAsync("user1", "TestToken", "access_123", -1);

        var result = await _store.GetAccessTokenAsync("user1", "TestToken");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccessTokenAsync_DifferentUsers_SameTokenType_Isolated()
    {
        await _store.SetAccessTokenAsync("user1", "TestToken", "access_user1", 3600);
        await _store.SetAccessTokenAsync("user2", "TestToken", "access_user2", 3600);

        var user1Token = await _store.GetAccessTokenAsync("user1", "TestToken");
        var user2Token = await _store.GetAccessTokenAsync("user2", "TestToken");

        user1Token.Should().Be("access_user1");
        user2Token.Should().Be("access_user2");
    }

    [Fact]
    public async Task GetAccessTokenAsync_SameUser_DifferentTokenTypes_Isolated()
    {
        await _store.SetAccessTokenAsync("user1", "TokenA", "access_a", 3600);
        await _store.SetAccessTokenAsync("user1", "TokenB", "access_b", 3600);

        var tokenA = await _store.GetAccessTokenAsync("user1", "TokenA");
        var tokenB = await _store.GetAccessTokenAsync("user1", "TokenB");

        tokenA.Should().Be("access_a");
        tokenB.Should().Be("access_b");
    }

    [Fact]
    public async Task GetAccessTokenAsync_CaseSensitive_UserId_ShouldIsolate()
    {
        // SR-H4（P1.5）：userId 比较器改为 Ordinal，"User1"/"user1" 读写互不可见。
        // 大小写归一化责任在调用方入口，存储层一律 Ordinal（跨用户读取令牌 = 越权面）。
        await _store.SetAccessTokenAsync("User1", "TestToken", "access_123", 3600);

        var result = await _store.GetAccessTokenAsync("user1", "TestToken");

        result.Should().BeNull("大小写不同的 userId 是不同的用户（Ordinal 隔离）");
    }

    [Fact]
    public async Task GetAccessTokenAsync_CaseInsensitive_TokenType()
    {
        await _store.SetAccessTokenAsync("user1", "TenantAccessToken", "access_123", 3600);

        var result = await _store.GetAccessTokenAsync("user1", "tenantaccesstoken");

        result.Should().Be("access_123", "tokenType 语义不区分大小写（与 MemoryTokenStore 一致）");
    }

    #endregion

    #region SetAccessTokenAsync (with userId)

    [Fact]
    public async Task SetAccessTokenAsync_OverwritesExistingToken()
    {
        await _store.SetAccessTokenAsync("user1", "TestToken", "old_token", 3600);
        await _store.SetAccessTokenAsync("user1", "TestToken", "new_token", 3600);

        var result = await _store.GetAccessTokenAsync("user1", "TestToken");

        result.Should().Be("new_token");
    }

    [Fact]
    public async Task SetAccessTokenAsync_PreservesExistingRefreshToken()
    {
        await _store.SetAccessTokenAsync("user1", "TestToken", "access_123", 3600);
        await _store.SetRefreshTokenAsync("user1", "TestToken", "refresh_456");
        await _store.SetAccessTokenAsync("user1", "TestToken", "new_access", 3600);

        var refreshToken = await _store.GetRefreshTokenAsync("user1", "TestToken");

        refreshToken.Should().Be("refresh_456");
    }

    #endregion

    #region GetRefreshTokenAsync / SetRefreshTokenAsync (with userId)

    [Fact]
    public async Task GetRefreshTokenAsync_WhenNotSet_ReturnsNull()
    {
        var result = await _store.GetRefreshTokenAsync("user1", "TestToken");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetRefreshTokenAsync_WhenSet_ReturnsToken()
    {
        await _store.SetRefreshTokenAsync("user1", "TestToken", "refresh_456");

        var result = await _store.GetRefreshTokenAsync("user1", "TestToken");

        result.Should().Be("refresh_456");
    }

    [Fact]
    public async Task SetRefreshTokenAsync_UpdatesExistingRefreshToken()
    {
        await _store.SetRefreshTokenAsync("user1", "TestToken", "old_refresh");
        await _store.SetRefreshTokenAsync("user1", "TestToken", "new_refresh");

        var result = await _store.GetRefreshTokenAsync("user1", "TestToken");

        result.Should().Be("new_refresh");
    }

    [Fact]
    public async Task SetRefreshTokenAsync_PreservesExistingAccessToken()
    {
        await _store.SetAccessTokenAsync("user1", "TestToken", "access_123", 3600);
        await _store.SetRefreshTokenAsync("user1", "TestToken", "refresh_456");

        var accessToken = await _store.GetAccessTokenAsync("user1", "TestToken");

        accessToken.Should().Be("access_123");
    }

    [Fact]
    public async Task RefreshToken_DifferentUsers_Isolated()
    {
        await _store.SetRefreshTokenAsync("user1", "TestToken", "refresh_user1");
        await _store.SetRefreshTokenAsync("user2", "TestToken", "refresh_user2");

        var user1Refresh = await _store.GetRefreshTokenAsync("user1", "TestToken");
        var user2Refresh = await _store.GetRefreshTokenAsync("user2", "TestToken");

        user1Refresh.Should().Be("refresh_user1");
        user2Refresh.Should().Be("refresh_user2");
    }

    #endregion

    #region RemoveAsync (with userId)

    [Fact]
    public async Task RemoveAsync_RemovesAllTokenDataForUser()
    {
        await _store.SetAccessTokenAsync("user1", "TestToken", "access_123", 3600);
        await _store.SetRefreshTokenAsync("user1", "TestToken", "refresh_456");

        await _store.RemoveAsync("user1", "TestToken");

        var accessToken = await _store.GetAccessTokenAsync("user1", "TestToken");
        var refreshToken = await _store.GetRefreshTokenAsync("user1", "TestToken");
        accessToken.Should().BeNull();
        refreshToken.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_OneUser_DoesNotAffectOtherUsers()
    {
        await _store.SetAccessTokenAsync("user1", "TestToken", "access_user1", 3600);
        await _store.SetAccessTokenAsync("user2", "TestToken", "access_user2", 3600);

        await _store.RemoveAsync("user1", "TestToken");

        var user2Token = await _store.GetAccessTokenAsync("user2", "TestToken");
        user2Token.Should().Be("access_user2");
    }

    [Fact]
    public async Task RemoveAsync_OneTokenType_DoesNotAffectOtherTokenTypes()
    {
        await _store.SetAccessTokenAsync("user1", "TokenA", "access_a", 3600);
        await _store.SetAccessTokenAsync("user1", "TokenB", "access_b", 3600);

        await _store.RemoveAsync("user1", "TokenA");

        var tokenB = await _store.GetAccessTokenAsync("user1", "TokenB");
        tokenB.Should().Be("access_b");
    }

    [Fact]
    public async Task RemoveAsync_NonExistentUser_DoesNotThrow()
    {
        var act = async () => await _store.RemoveAsync("nonexistent", "TestToken");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_LastTokenOfUser_ShouldCollectEmptyUserDict()
    {
        // SR-L5（P3.8）：逐条 Remove 后空内层字典被清扫，海量短命 userId 不滞留空字典壳。
        await _store.SetAccessTokenAsync("short-lived-user", "TokenA", "access_a", 3600);

        await _store.RemoveAsync("short-lived-user", "TokenA");

        // 清扫后：该用户的令牌类型列表应为空（外层条目已移除）
        var types = await _store.GetTokenTypesAsync("short-lived-user");
        types.Should().BeEmpty("空内层字典已被清扫（SR-L5）");

        // 再 Set 应正常恢复（清扫误删由下次 Set 恢复——弱一致可接受）
        await _store.SetAccessTokenAsync("short-lived-user", "TokenA", "access_a2", 3600);
        var token = await _store.GetAccessTokenAsync("short-lived-user", "TokenA");
        token.Should().Be("access_a2");
    }

    #endregion

    #region M6-HC-28 空壳桶清扫（桶数有界）

    /// <summary>
    /// 海量短命 userId 的过期读路径会留下"空内层字典壳"（<c>GetAccessTokenAsync</c> 只移除条目、不移除外层桶）。
    /// HC-28 的惰性清扫须在桶数越过阈值后的下一次写入把这些空壳回收，且不得误删仍有令牌的桶。
    /// </summary>
    [Fact]
    public async Task SetAccessToken_WhenBucketCountExceedsThreshold_SweepsEmptyBuckets()
    {
        var threshold = MemoryUserTokenStore.UserBucketSweepThreshold;
        var userCount = threshold + 50;

        // 步骤 1：造出 userCount 个桶（每个桶持有一个已过期条目，故清扫期间桶非空、不会被回收）。
        for (var i = 0; i < userCount; i++)
            await _store.SetAccessTokenAsync($"user_{i}", "TestToken", $"access_{i}", -1);

        _store.UserBucketCount.Should().BeGreaterThan(threshold, "构造阶段须先越过清扫阈值");

        // 步骤 2：逐个读取（过期 → 条目被移除），桶随之变为空壳。Get 路径不触发清扫，空壳由此滞留。
        for (var i = 0; i < userCount; i++)
            (await _store.GetAccessTokenAsync($"user_{i}", "TestToken")).Should().BeNull();

        // 步骤 3：一次写入越过阈值 → 触发惰性全量清扫。
        await _store.SetAccessTokenAsync("live-user", "TestToken", "live_token", 3600);

        _store.UserBucketCount.Should().BeLessThanOrEqualTo(1,
            "清扫后只应残留仍有令牌的 live-user 桶，空壳桶必须被回收（桶数有界）");
        (await _store.GetAccessTokenAsync("live-user", "TestToken")).Should().Be("live_token",
            "清扫不得误删仍有令牌的桶");
    }

    [Fact]
    public async Task SetAccessToken_BelowThreshold_DoesNotSweepNonEmptyBuckets()
    {
        // 未越阈值时不扫描：正常令牌读写一律不受影响（防「清扫误删」回归）。
        for (var i = 0; i < 20; i++)
            await _store.SetAccessTokenAsync($"user_{i}", "TestToken", $"access_{i}", 3600);

        await _store.SetAccessTokenAsync("user_late", "TestToken", "access_late", 3600);

        for (var i = 0; i < 20; i++)
            (await _store.GetAccessTokenAsync($"user_{i}", "TestToken")).Should().Be($"access_{i}");
        (await _store.GetAccessTokenAsync("user_late", "TestToken")).Should().Be("access_late");
    }

    #endregion

    #region Concurrent Access

    [Fact]
    public async Task ConcurrentAccess_IsThreadSafe()
    {
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var userId = $"user_{index}";
                await _store.SetAccessTokenAsync(userId, "TestToken", $"access_{index}", 3600);
                var result = await _store.GetAccessTokenAsync(userId, "TestToken");
                result.Should().Be($"access_{index}");
            }));
        }

        await Task.WhenAll(tasks);
    }

    #endregion
}
