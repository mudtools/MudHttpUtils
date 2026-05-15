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
    public async Task GetAccessTokenAsync_CaseInsensitive_UserId()
    {
        await _store.SetAccessTokenAsync("User1", "TestToken", "access_123", 3600);

        var result = await _store.GetAccessTokenAsync("user1", "TestToken");

        result.Should().Be("access_123");
    }

    [Fact]
    public async Task GetAccessTokenAsync_CaseInsensitive_TokenType()
    {
        await _store.SetAccessTokenAsync("user1", "TenantAccessToken", "access_123", 3600);

        var result = await _store.GetAccessTokenAsync("user1", "tenantaccesstoken");

        result.Should().Be("access_123");
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
