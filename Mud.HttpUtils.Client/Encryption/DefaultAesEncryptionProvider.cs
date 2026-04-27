using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Mud.HttpUtils.Encryption;

namespace Mud.HttpUtils;

/// <summary>
/// 默认 AES 加密提供程序实现，使用 AES 对称加密算法进行数据加密和解密。
/// </summary>
/// <remarks>
/// 该类使用 CBC 模式和 PKCS7 填充方式。每次加密时随机生成 IV，并将 IV 附加到密文前（前 16 字节），
/// 以确保相同明文不会产生相同密文，满足语义安全性要求。
/// </remarks>
public sealed class DefaultAesEncryptionProvider : IEncryptionProvider, IDisposable
{
    private const int IvSizeBytes = 16;

    private byte[] _key;
    private bool _disposed;

    public DefaultAesEncryptionProvider(IOptions<AesEncryptionOptions> options)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        options.Value.Validate();
        _key = (byte[])options.Value.Key.Clone();
        options.Value.ClearSensitiveData();
    }

    /// <summary>
    /// 使用 AES 算法加密明文数据。每次加密随机生成 IV，IV 附加在密文前。
    /// </summary>
    /// <param name="plainText">要加密的明文数据。</param>
    /// <returns>加密后的 Base64 编码密文字符串（含前缀 IV）。</returns>
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
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        aes.GenerateIV();
        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// 使用 AES 算法解密密文数据。从密文前 16 字节提取 IV。
    /// </summary>
    /// <param name="cipherText">要解密的 Base64 编码密文字符串（含前缀 IV）。</param>
    /// <returns>解密后的明文字符串。</returns>
    /// <remarks>
    /// 如果输入为空或 null，则返回空字符串。
    /// </remarks>
    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        var fullBytes = Convert.FromBase64String(cipherText);
        if (fullBytes.Length < IvSizeBytes + 1)
            throw new InvalidOperationException("密文数据格式无效：长度不足");

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var iv = new byte[IvSizeBytes];
        var cipherBytes = new byte[fullBytes.Length - IvSizeBytes];
        Buffer.BlockCopy(fullBytes, 0, iv, 0, IvSizeBytes);
        Buffer.BlockCopy(fullBytes, IvSizeBytes, cipherBytes, 0, cipherBytes.Length);
        aes.IV = iv;

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// 使用 AES 算法加密二进制数据。每次加密随机生成 IV，IV 附加在密文前。
    /// </summary>
    /// <param name="data">要加密的二进制数据。</param>
    /// <returns>加密后的二进制数据（含前缀 IV）。</returns>
    public byte[] EncryptBytes(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        aes.GenerateIV();
        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var encryptedBytes = encryptor.TransformFinalBlock(data, 0, data.Length);

        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return result;
    }

    /// <summary>
    /// 使用 AES 算法解密二进制数据。从密文前 16 字节提取 IV。
    /// </summary>
    /// <param name="encryptedData">要解密的二进制数据（含前缀 IV）。</param>
    /// <returns>解密后的二进制数据。</returns>
    public byte[] DecryptBytes(byte[] encryptedData)
    {
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        if (encryptedData.Length < IvSizeBytes + 1)
            throw new InvalidOperationException("密文数据格式无效：长度不足");

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        var iv = new byte[IvSizeBytes];
        var cipherBytes = new byte[encryptedData.Length - IvSizeBytes];
        Buffer.BlockCopy(encryptedData, 0, iv, 0, IvSizeBytes);
        Buffer.BlockCopy(encryptedData, IvSizeBytes, cipherBytes, 0, cipherBytes.Length);
        aes.IV = iv;

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SecurityHelper.ClearBytes(_key);
        _key = Array.Empty<byte>();
    }
}
