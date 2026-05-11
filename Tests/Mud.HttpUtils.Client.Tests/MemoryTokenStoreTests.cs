namespace Mud.HttpUtils.Client.Tests;

public class MemoryTokenStoreTests
{
    #region GetAccessTokenAsync

    [Fact]
    public async Task GetAccessTokenAsync_WhenNotSet_ReturnsNull()
    {
        var store = new MemoryTokenStore();

        var result = await store.GetAccessTokenAsync("TestToken");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenSet_ReturnsToken()
    {
        var store = new MemoryTokenStore();
        await store.SetAccessTokenAsync("TestToken", "access_123", 3600);

        var result = await store.GetAccessTokenAsync("TestToken");

        result.Should().Be("access_123");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenExpired_ReturnsNull()
    {
        var store = new MemoryTokenStore();
        await store.SetAccessTokenAsync("TestToken", "access_123", -1);

        var result = await store.GetAccessTokenAsync("TestToken");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccessTokenAsync_CaseInsensitive_ReturnsToken()
    {
        var store = new MemoryTokenStore();
        await store.SetAccessTokenAsync("TenantAccessToken", "access_123", 3600);

        var result = await store.GetAccessTokenAsync("tenantaccesstoken");

        result.Should().Be("access_123");
    }

    #endregion

    #region SetAccessTokenAsync

    [Fact]
    public async Task SetAccessTokenAsync_OverwritesExistingToken()
    {
        var store = new MemoryTokenStore();
        await store.SetAccessTokenAsync("TestToken", "old_token", 3600);
        await store.SetAccessTokenAsync("TestToken", "new_token", 3600);

        var result = await store.GetAccessTokenAsync("TestToken");

        result.Should().Be("new_token");
    }

    [Fact]
    public async Task SetAccessTokenAsync_PreservesExistingRefreshToken()
    {
        var store = new MemoryTokenStore();
        await store.SetAccessTokenAsync("TestToken", "access_123", 3600);
        await store.SetRefreshTokenAsync("TestToken", "refresh_456");
        await store.SetAccessTokenAsync("TestToken", "new_access", 3600);

        var refreshToken = await store.GetRefreshTokenAsync("TestToken");

        refreshToken.Should().Be("refresh_456");
    }

    #endregion

    #region GetRefreshTokenAsync

    [Fact]
    public async Task GetRefreshTokenAsync_WhenNotSet_ReturnsNull()
    {
        var store = new MemoryTokenStore();

        var result = await store.GetRefreshTokenAsync("TestToken");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRefreshTokenAsync_WhenSet_ReturnsToken()
    {
        var store = new MemoryTokenStore();
        await store.SetRefreshTokenAsync("TestToken", "refresh_456");

        var result = await store.GetRefreshTokenAsync("TestToken");

        result.Should().Be("refresh_456");
    }

    [Fact]
    public async Task GetRefreshTokenAsync_CaseInsensitive_ReturnsToken()
    {
        var store = new MemoryTokenStore();
        await store.SetRefreshTokenAsync("TestToken", "refresh_456");

        var result = await store.GetRefreshTokenAsync("testtoken");

        result.Should().Be("refresh_456");
    }

    #endregion

    #region SetRefreshTokenAsync

    [Fact]
    public async Task SetRefreshTokenAsync_WhenNoExistingEntry_CreatesNewEntry()
    {
        var store = new MemoryTokenStore();
        await store.SetRefreshTokenAsync("TestToken", "refresh_456");

        var result = await store.GetRefreshTokenAsync("TestToken");

        result.Should().Be("refresh_456");
    }

    [Fact]
    public async Task SetRefreshTokenAsync_WhenExistingEntry_UpdatesRefreshToken()
    {
        var store = new MemoryTokenStore();
        await store.SetAccessTokenAsync("TestToken", "access_123", 3600);
        await store.SetRefreshTokenAsync("TestToken", "old_refresh");
        await store.SetRefreshTokenAsync("TestToken", "new_refresh");

        var result = await store.GetRefreshTokenAsync("TestToken");

        result.Should().Be("new_refresh");
    }

    [Fact]
    public async Task SetRefreshTokenAsync_PreservesExistingAccessToken()
    {
        var store = new MemoryTokenStore();
        await store.SetAccessTokenAsync("TestToken", "access_123", 3600);
        await store.SetRefreshTokenAsync("TestToken", "refresh_456");

        var accessToken = await store.GetAccessTokenAsync("TestToken");

        accessToken.Should().Be("access_123");
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task RemoveAsync_RemovesAllTokenData()
    {
        var store = new MemoryTokenStore();
        await store.SetAccessTokenAsync("TestToken", "access_123", 3600);
        await store.SetRefreshTokenAsync("TestToken", "refresh_456");

        await store.RemoveAsync("TestToken");

        var accessToken = await store.GetAccessTokenAsync("TestToken");
        var refreshToken = await store.GetRefreshTokenAsync("TestToken");
        accessToken.Should().BeNull();
        refreshToken.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_NonExistentToken_DoesNotThrow()
    {
        var store = new MemoryTokenStore();

        var act = async () => await store.RemoveAsync("NonExistent");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_OneToken_DoesNotAffectOtherTokens()
    {
        var store = new MemoryTokenStore();
        await store.SetAccessTokenAsync("Token1", "access_1", 3600);
        await store.SetAccessTokenAsync("Token2", "access_2", 3600);

        await store.RemoveAsync("Token1");

        var token2 = await store.GetAccessTokenAsync("Token2");
        token2.Should().Be("access_2");
    }

    #endregion

    #region Concurrent Access

    [Fact]
    public async Task ConcurrentAccess_IsThreadSafe()
    {
        var store = new MemoryTokenStore();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await store.SetAccessTokenAsync($"Token_{index}", $"access_{index}", 3600);
                var result = await store.GetAccessTokenAsync($"Token_{index}");
                result.Should().Be($"access_{index}");
            }));
        }

        await Task.WhenAll(tasks);
    }

    #endregion
}
