using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Tests;

public class AesEncryptionProviderTests
{
    private static IEncryptionProvider CreateProvider(byte[]? key = null, byte[]? iv = null)
    {
        var options = new AesEncryptionOptions
        {
            Key = key ?? Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng=="),
            IV = iv ?? Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==")
        };
        return new DefaultAesEncryptionProvider(Options.Create(options));
    }

    #region DefaultAesEncryptionProvider Tests

    [Fact]
    public void Encrypt_WithNullPlainText_ShouldReturnEmptyString()
    {
        var provider = CreateProvider();

        var result = provider.Encrypt(null!);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Encrypt_WithEmptyPlainText_ShouldReturnEmptyString()
    {
        var provider = CreateProvider();

        var result = provider.Encrypt(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Encrypt_WithValidPlainText_ShouldReturnNonEmptyString()
    {
        var provider = CreateProvider();

        var result = provider.Encrypt("Hello World");

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Decrypt_WithNullCipherText_ShouldReturnEmptyString()
    {
        var provider = CreateProvider();

        var result = provider.Decrypt(null!);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Decrypt_WithEmptyCipherText_ShouldReturnEmptyString()
    {
        var provider = CreateProvider();

        var result = provider.Decrypt(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Encrypt_AndDecrypt_ShouldReturnOriginalText()
    {
        var provider = CreateProvider();
        var originalText = "Hello, 世界! @#$%";

        var encrypted = provider.Encrypt(originalText);
        var decrypted = provider.Decrypt(encrypted);

        decrypted.Should().Be(originalText);
    }

    [Fact]
    public void Encrypt_WithDifferentKeys_ShouldReturnDifferentResults()
    {
        var provider1 = CreateProvider(key: Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng=="));
        var provider2 = CreateProvider(key: Convert.FromBase64String("QUJDREVGR0hJSktMTU5PUA=="));

        var result1 = provider1.Encrypt("test");
        var result2 = provider2.Encrypt("test");

        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Encrypt_SameTextTwice_ShouldReturnDifferentCiphertext()
    {
        var provider = CreateProvider();

        var result1 = provider.Encrypt("test");
        var result2 = provider.Encrypt("test");

        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        var act = () => new DefaultAesEncryptionProvider(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region AesEncryptionOptions Tests

    [Fact]
    public void Validate_WithValidKey16Bytes_ShouldNotThrow()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[16],
            IV = new byte[16]
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithValidKey24Bytes_ShouldNotThrow()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[24],
            IV = new byte[16]
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithValidKey32Bytes_ShouldNotThrow()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[32],
            IV = new byte[16]
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithInvalidKeyLength_ShouldThrowInvalidOperationException()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[10],
            IV = new byte[16]
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AES Key 长度*");
    }

    [Fact]
    public void Validate_WithInvalidIVLength_ShouldThrowInvalidOperationException()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[16],
            IV = new byte[10]
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AES IV 长度*");
    }

    [Fact]
    public void Validate_WithNullKey_ShouldThrowInvalidOperationException()
    {
        var options = new AesEncryptionOptions
        {
            Key = null!,
            IV = new byte[16]
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_WithNullIV_ShouldNotThrow()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[16],
#pragma warning disable CS0618
            IV = null!
#pragma warning restore CS0618
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void SectionName_ShouldBeMudHttpAesEncryption()
    {
        AesEncryptionOptions.SectionName.Should().Be("MudHttpAesEncryption");
    }

    #endregion

    #region DefaultAesEncryptionProvider.Dispose Tests

    private static DefaultAesEncryptionProvider CreateDisposableProvider(byte[]? key = null, byte[]? iv = null)
    {
        var options = new AesEncryptionOptions
        {
            Key = key ?? Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng=="),
            IV = iv ?? Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==")
        };
        return new DefaultAesEncryptionProvider(Options.Create(options));
    }

    [Fact]
    public void Dispose_CalledOnce_ShouldNotThrow()
    {
        var provider = CreateDisposableProvider();

        var act = () => provider.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        var provider = CreateDisposableProvider();

        provider.Dispose();
        var act = () => provider.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Encrypt_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var provider = CreateDisposableProvider();
        provider.Dispose();

        var act = () => provider.Encrypt("test");

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Decrypt_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var provider = CreateDisposableProvider();
        var encrypted = provider.Encrypt("test");
        provider.Dispose();

        var act = () => provider.Decrypt(encrypted);

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void EncryptBytes_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var provider = CreateDisposableProvider();
        provider.Dispose();

        var act = () => provider.EncryptBytes(new byte[16]);

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void DecryptBytes_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var provider = CreateDisposableProvider();
        var encrypted = provider.EncryptBytes(new byte[16]);
        provider.Dispose();

        var act = () => provider.DecryptBytes(encrypted);

        act.Should().Throw<ObjectDisposedException>();
    }

    #endregion

    #region AesEncryptionOptions.ClearSensitiveData Tests

    [Fact]
    public void ClearSensitiveData_ShouldZeroOutKeyAndIV()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            IV = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }
        };

        options.ClearSensitiveData();

        options.Key.Should().BeEmpty();
        options.IV.Should().BeEmpty();
    }

    [Fact]
    public void ClearSensitiveData_CalledMultipleTimes_ShouldNotThrow()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[16],
            IV = new byte[16]
        };

        options.ClearSensitiveData();
        var act = () => options.ClearSensitiveData();

        act.Should().NotThrow();
    }

    #endregion
}
