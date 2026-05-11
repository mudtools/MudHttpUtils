using Moq;

namespace Mud.HttpUtils.Client.Tests;

public class MemoryEncryptedTokenStoreTests
{
    private readonly Mock<IEncryptionProvider> _encryptionProviderMock;
    private readonly MemoryEncryptedTokenStore _store;

    public MemoryEncryptedTokenStoreTests()
    {
        _encryptionProviderMock = new Mock<IEncryptionProvider>();
        _encryptionProviderMock.Setup(p => p.Encrypt(It.IsAny<string>()))
            .Returns((string plain) => $"enc_{plain}");
        _encryptionProviderMock.Setup(p => p.Decrypt(It.IsAny<string>()))
            .Returns((string cipher) => cipher.StartsWith("enc_") ? cipher[4..] : cipher);

        _store = new MemoryEncryptedTokenStore(_encryptionProviderMock.Object);
    }

    #region Constructor

    [Fact]
    public void Constructor_NullEncryptionProvider_ThrowsArgumentNullException()
    {
        var act = () => new MemoryEncryptedTokenStore(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("encryptionProvider");
    }

    #endregion

    #region IsEncryptionEnabled

    [Fact]
    public void IsEncryptionEnabled_ReturnsTrue()
    {
        _store.IsEncryptionEnabled.Should().BeTrue();
    }

    #endregion

    #region SetAccessTokenAsync / GetAccessTokenAsync

    [Fact]
    public async Task SetAccessTokenAsync_EncryptsBeforeStoring()
    {
        await _store.SetAccessTokenAsync("TestToken", "my_secret_token", 3600);

        _encryptionProviderMock.Verify(p => p.Encrypt("my_secret_token"), Times.Once);
    }

    [Fact]
    public async Task GetAccessTokenAsync_DecryptsAfterReading()
    {
        await _store.SetAccessTokenAsync("TestToken", "my_secret_token", 3600);
        _encryptionProviderMock.Invocations.Clear();

        var result = await _store.GetAccessTokenAsync("TestToken");

        _encryptionProviderMock.Verify(p => p.Decrypt(It.IsAny<string>()), Times.Once);
        result.Should().Be("my_secret_token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenNotSet_ReturnsNullWithoutDecrypting()
    {
        var result = await _store.GetAccessTokenAsync("NonExistent");

        result.Should().BeNull();
        _encryptionProviderMock.Verify(p => p.Decrypt(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenExpired_ReturnsNull()
    {
        await _store.SetAccessTokenAsync("TestToken", "my_secret_token", -1);

        var result = await _store.GetAccessTokenAsync("TestToken");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAccessTokenAsync_OverwritesExistingToken()
    {
        await _store.SetAccessTokenAsync("TestToken", "old_token", 3600);
        await _store.SetAccessTokenAsync("TestToken", "new_token", 3600);

        var result = await _store.GetAccessTokenAsync("TestToken");

        result.Should().Be("new_token");
    }

    [Fact]
    public async Task SetAccessTokenAsync_PreservesExistingRefreshToken()
    {
        await _store.SetAccessTokenAsync("TestToken", "access_123", 3600);
        await _store.SetRefreshTokenAsync("TestToken", "refresh_456");
        await _store.SetAccessTokenAsync("TestToken", "new_access", 3600);

        var refreshToken = await _store.GetRefreshTokenAsync("TestToken");

        refreshToken.Should().Be("refresh_456");
    }

    #endregion

    #region SetRefreshTokenAsync / GetRefreshTokenAsync

    [Fact]
    public async Task SetRefreshTokenAsync_EncryptsBeforeStoring()
    {
        await _store.SetRefreshTokenAsync("TestToken", "my_refresh_token");

        _encryptionProviderMock.Verify(p => p.Encrypt("my_refresh_token"), Times.Once);
    }

    [Fact]
    public async Task GetRefreshTokenAsync_DecryptsAfterReading()
    {
        await _store.SetRefreshTokenAsync("TestToken", "my_refresh_token");
        _encryptionProviderMock.Invocations.Clear();

        var result = await _store.GetRefreshTokenAsync("TestToken");

        _encryptionProviderMock.Verify(p => p.Decrypt(It.IsAny<string>()), Times.Once);
        result.Should().Be("my_refresh_token");
    }

    [Fact]
    public async Task GetRefreshTokenAsync_WhenNotSet_ReturnsNullWithoutDecrypting()
    {
        var result = await _store.GetRefreshTokenAsync("NonExistent");

        result.Should().BeNull();
        _encryptionProviderMock.Verify(p => p.Decrypt(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetRefreshTokenAsync_WhenNoExistingEntry_CreatesNewEntry()
    {
        await _store.SetRefreshTokenAsync("TestToken", "refresh_456");

        var result = await _store.GetRefreshTokenAsync("TestToken");

        result.Should().Be("refresh_456");
    }

    [Fact]
    public async Task SetRefreshTokenAsync_UpdatesExistingRefreshToken()
    {
        await _store.SetRefreshTokenAsync("TestToken", "old_refresh");
        await _store.SetRefreshTokenAsync("TestToken", "new_refresh");

        var result = await _store.GetRefreshTokenAsync("TestToken");

        result.Should().Be("new_refresh");
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task RemoveAsync_RemovesAllTokenData()
    {
        await _store.SetAccessTokenAsync("TestToken", "access_123", 3600);
        await _store.SetRefreshTokenAsync("TestToken", "refresh_456");

        await _store.RemoveAsync("TestToken");

        var accessToken = await _store.GetAccessTokenAsync("TestToken");
        var refreshToken = await _store.GetRefreshTokenAsync("TestToken");
        accessToken.Should().BeNull();
        refreshToken.Should().BeNull();
    }

    #endregion

    #region Round-trip Encryption

    [Fact]
    public async Task AccessToken_RoundTrip_EncryptDecryptTransparent()
    {
        var plainToken = "sensitive_access_token_12345";
        await _store.SetAccessTokenAsync("TestToken", plainToken, 3600);

        var result = await _store.GetAccessTokenAsync("TestToken");

        result.Should().Be(plainToken);
    }

    [Fact]
    public async Task RefreshToken_RoundTrip_EncryptDecryptTransparent()
    {
        var plainToken = "sensitive_refresh_token_67890";
        await _store.SetRefreshTokenAsync("TestToken", plainToken);

        var result = await _store.GetRefreshTokenAsync("TestToken");

        result.Should().Be(plainToken);
    }

    #endregion

    #region Case Insensitive

    [Fact]
    public async Task GetAccessTokenAsync_CaseInsensitive_ReturnsToken()
    {
        await _store.SetAccessTokenAsync("TenantAccessToken", "access_123", 3600);

        var result = await _store.GetAccessTokenAsync("tenantaccesstoken");

        result.Should().Be("access_123");
    }

    #endregion
}
