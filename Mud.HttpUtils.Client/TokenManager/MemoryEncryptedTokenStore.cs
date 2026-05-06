namespace Mud.HttpUtils;

public class MemoryEncryptedTokenStore : MemoryTokenStore, IEncryptedTokenStore
{
    private readonly IEncryptionProvider _encryptionProvider;

    public MemoryEncryptedTokenStore(IEncryptionProvider encryptionProvider)
    {
        _encryptionProvider = encryptionProvider ?? throw new ArgumentNullException(nameof(encryptionProvider));
    }

    public bool IsEncryptionEnabled => true;

    public new async Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        var encrypted = await base.GetAccessTokenAsync(tokenType, cancellationToken).ConfigureAwait(false);
        if (encrypted == null)
            return null;

        return _encryptionProvider.Decrypt(encrypted);
    }

    public new async Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
    {
        var encrypted = _encryptionProvider.Encrypt(accessToken);
        await base.SetAccessTokenAsync(tokenType, encrypted, expiresInSeconds, cancellationToken).ConfigureAwait(false);
    }

    public new async Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        var encrypted = await base.GetRefreshTokenAsync(tokenType, cancellationToken).ConfigureAwait(false);
        if (encrypted == null)
            return null;

        return _encryptionProvider.Decrypt(encrypted);
    }

    public new async Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
    {
        var encrypted = _encryptionProvider.Encrypt(refreshToken);
        await base.SetRefreshTokenAsync(tokenType, encrypted, cancellationToken).ConfigureAwait(false);
    }
}
