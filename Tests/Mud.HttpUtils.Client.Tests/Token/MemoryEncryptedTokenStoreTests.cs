using System.Security.Cryptography;
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

    #region NEW-TM-14：Decrypt 异常包装为 InvalidOperationException

    [Fact]
    public async Task GetAccessTokenAsync_WhenDecryptThrows_ShouldWrapInInvalidOperationException()
    {
        // Arrange：让 Decrypt 抛出异常（模拟密钥轮换、密文损坏）
        // 构造函数已配置 Encrypt 正常，先 SetAccessToken 触发 Encrypt
        await _store.SetAccessTokenAsync("TestToken", "my_secret", 3600);

        // 重新配置 Decrypt 抛异常
        _encryptionProviderMock.Setup(p => p.Decrypt(It.IsAny<string>()))
            .Throws(new CryptographicException("密钥不匹配"));

        // Act
        var act = async () => await _store.GetAccessTokenAsync("TestToken");

        // Assert：应抛 InvalidOperationException，包含可能原因说明
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("解密失败");
        ex.Which.Message.Should().Contain("密钥轮换");
        ex.Which.InnerException.Should().BeOfType<CryptographicException>();
    }

    [Fact]
    public async Task GetRefreshTokenAsync_WhenDecryptThrows_ShouldWrapInInvalidOperationException()
    {
        // Arrange
        await _store.SetRefreshTokenAsync("TestToken", "my_refresh");

        _encryptionProviderMock.Setup(p => p.Decrypt(It.IsAny<string>()))
            .Throws(new InvalidOperationException("IV 不匹配"));

        // Act
        var act = async () => await _store.GetRefreshTokenAsync("TestToken");

        // Assert
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("解密失败");
        ex.Which.Message.Should().Contain("IV 不匹配");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenDecryptThrowsOperationCanceledException_ShouldPropagateDirectly()
    {
        // Arrange：OperationCanceledException 不应被包装，应直接传播
        await _store.SetAccessTokenAsync("TestToken", "my_secret", 3600);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _encryptionProviderMock.Setup(p => p.Decrypt(It.IsAny<string>()))
            .Throws(new OperationCanceledException(cts.Token));

        // Act
        var act = async () => await _store.GetAccessTokenAsync("TestToken", cts.Token);

        // Assert：应直接抛 OperationCanceledException，不被包装为 InvalidOperationException
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
