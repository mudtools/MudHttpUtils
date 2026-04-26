using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Encryption;

namespace Mud.HttpUtils;

/// <summary>
/// 默认 AES 加密提供程序实现，使用 AES 对称加密算法进行数据加密和解密。
/// </summary>
/// <remarks>
/// 该类使用 CBC 模式和 PKCS7 填充方式，通过配置的密钥和初始化向量进行加密操作。
/// </remarks>
public sealed class DefaultAesEncryptionProvider : IEncryptionProvider, IDisposable
{
    private byte[] _key;
    private byte[] _iv;
    private bool _disposed;

    public DefaultAesEncryptionProvider(IOptions<AesEncryptionOptions> options)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        options.Value.Validate();
        _key = (byte[])options.Value.Key.Clone();
        _iv = (byte[])options.Value.IV.Clone();
        options.Value.ClearSensitiveData();
    }

    /// <summary>
    /// 使用 AES 算法加密明文数据。
    /// </summary>
    /// <param name="plainText">要加密的明文数据。</param>
    /// <returns>加密后的 Base64 编码密文字符串。</returns>
    /// <remarks>
    /// 如果输入为空或 null，则返回空字符串。
    /// </remarks>
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// 使用 AES 算法解密密文数据。
    /// </summary>
    /// <param name="cipherText">要解密的 Base64 编码密文字符串。</param>
    /// <returns>解密后的明文字符串。</returns>
    /// <remarks>
    /// 如果输入为空或 null，则返回空字符串。
    /// </remarks>
    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var cipherBytes = Convert.FromBase64String(cipherText);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// 使用 AES 算法加密二进制数据。
    /// </summary>
    /// <param name="data">要加密的二进制数据。</param>
    /// <returns>加密后的二进制数据。</returns>
    public byte[] EncryptBytes(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>
    /// 使用 AES 算法解密二进制数据。
    /// </summary>
    /// <param name="encryptedData">要解密的二进制数据。</param>
    /// <returns>解密后的二进制数据。</returns>
    public byte[] DecryptBytes(byte[] encryptedData)
    {
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SecurityHelper.ClearBytes(_key);
        SecurityHelper.ClearBytes(_iv);
        _key = Array.Empty<byte>();
        _iv = Array.Empty<byte>();
    }
}
