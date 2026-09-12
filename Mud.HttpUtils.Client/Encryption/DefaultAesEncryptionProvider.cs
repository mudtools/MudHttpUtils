// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Mud.HttpUtils;

/// <summary>
/// 默认 AES 加密提供程序实现，使用 AES 对称加密算法进行数据加密和解密。
/// </summary>
/// <remarks>
/// <para>M2-#7：默认启用<b>认证加密</b>，密文带 1 字节版本前缀：</para>
/// <list type="bullet">
/// <item><c>0x02</c>（net8.0/net10.0）：AesGcm —— <c>[版本][nonce(12)][tag(16)][密文]</c></item>
/// <item><c>0x03</c>（netstandard2.0/net6.0）：CBC + HMAC-SHA256 Encrypt-then-MAC —— <c>[版本][IV(16)][MAC(32)][密文]</c></item>
/// <item>无前缀：<see cref="AesEncryptionOptions.EnableAuthenticatedEncryption"/> 为 false 时的裸 CBC（仅调试用途）</item>
/// </list>
/// <para>解密按首字节版本分派；完整性校验失败抛 <see cref="CryptographicException"/>（而非返回乱文）。
/// CBC+HMAC 的 MAC 比较使用常量时间比较（复用 <c>DefaultHmacSignatureProvider</c> 的实现模式）。</para>
/// </remarks>
public sealed class DefaultAesEncryptionProvider : IEncryptionProvider, IDisposable
{
    private const int IvSizeBytes = 16;
    private const byte EnvelopeVersionGcm = 0x02;
    private const byte EnvelopeVersionCbcHmac = 0x03;
    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    private const int HmacTagSize = 32; // HMAC-SHA256

    private byte[] _key;
    private bool _disposed;
    private readonly bool _authenticated;

    /// <inheritdoc/>
    public DefaultAesEncryptionProvider(IOptions<AesEncryptionOptions> options)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        options.Value.Validate();
        _key = (byte[])options.Value.Key.Clone();
        _authenticated = options.Value.EnableAuthenticatedEncryption;
        options.Value.ClearSensitiveData();
    }

    /// <summary>
    /// 使用 AES 算法加密明文数据。密文格式见类备注。
    /// </summary>
    /// <param name="plainText">要加密的明文数据。</param>
    /// <returns>加密后的 Base64 编码密文字符串。</returns>
    /// <remarks>如果输入为空或 null，则返回空字符串。</remarks>
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        if (_disposed)
            throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = EncryptCore(plainBytes);
        return Convert.ToBase64String(encryptedBytes);
    }

    /// <summary>
    /// 使用 AES 算法解密密文数据。按版本前缀分派解密方式。
    /// </summary>
    /// <param name="cipherText">要解密的 Base64 编码密文字符串。</param>
    /// <returns>解密后的明文字符串。</returns>
    /// <exception cref="CryptographicException">完整性校验失败（密文被篡改）时抛出。</exception>
    /// <remarks>如果输入为空或 null，则返回空字符串。</remarks>
    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        var fullBytes = Convert.FromBase64String(cipherText);
        var plainBytes = DecryptCore(fullBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// 使用 AES 算法加密二进制数据。密文格式见类备注。
    /// </summary>
    /// <param name="data">要加密的二进制数据。</param>
    /// <returns>加密后的二进制数据。</returns>
    public byte[] EncryptBytes(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        return EncryptCore(data);
    }

    /// <summary>
    /// 使用 AES 算法解密二进制数据。按版本前缀分派解密方式。
    /// </summary>
    /// <param name="encryptedData">要解密的二进制数据。</param>
    /// <returns>解密后的二进制数据。</returns>
    /// <exception cref="CryptographicException">完整性校验失败（密文被篡改）时抛出。</exception>
    public byte[] DecryptBytes(byte[] encryptedData)
    {
        if (encryptedData == null)
            throw new ArgumentNullException(nameof(encryptedData));

        if (_disposed) throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));

        return DecryptCore(encryptedData);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SecurityHelper.ClearBytes(_key);
        _key = [];
    }

    private byte[] EncryptCore(byte[] plainBytes)
    {
        if (!_authenticated)
            return EncryptCbc(plainBytes);

#if NET8_0_OR_GREATER
        if (AesGcm.IsSupported)
            return EncryptGcm(plainBytes);
#endif
        return EncryptCbcThenHmac(plainBytes);
    }

    private byte[] DecryptCore(byte[] fullBytes)
    {
        if (fullBytes.Length == 0)
            throw new InvalidOperationException("密文数据格式无效：长度不足");

        // 无版本前缀 → 裸 CBC（EnableAuthenticatedEncryption=false 产出的密文）
        // 0x02/0x03 是合法版本字节；裸 CBC 首字节是 IV 的第 1 字节，无法与版本可靠区分，
        // 因此裸 CBC 依赖"解密失败再回退"不可行 —— 采用显式配置分派（见下）。
        if (fullBytes[0] == EnvelopeVersionGcm && _authenticated)
        {
#if NET8_0_OR_GREATER
            if (AesGcm.IsSupported)
                return DecryptGcm(fullBytes);
            throw new CryptographicException("密文为 GCM 格式，但当前运行时不支持 AesGcm");
#else
            throw new CryptographicException("密文为 GCM 格式，但当前目标框架不支持 AesGcm");
#endif
        }

        if (fullBytes[0] == EnvelopeVersionCbcHmac && _authenticated)
            return DecryptCbcThenHmac(fullBytes);

        // 未启用认证加密 → 裸 CBC
        if (!_authenticated)
            return DecryptCbc(fullBytes);

        // 启用认证加密但遇到无法识别的格式（含历史裸 CBC 密文）
        throw new CryptographicException(
            "密文格式无法识别：当前配置启用了认证加密，但密文缺少版本前缀。若确需解密旧格式密文，请设置 EnableAuthenticatedEncryption = false。");
    }

    // ---- v1：裸 CBC（仅调试用途，无完整性保护） ----

    private byte[] EncryptCbc(byte[] plainBytes)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        aes.GenerateIV();
        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);
        return result;
    }

    private byte[] DecryptCbc(byte[] fullBytes)
    {
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
        return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
    }

    // ---- v2：AesGcm（net8.0+） ----

#if NET8_0_OR_GREATER
    private byte[] EncryptGcm(byte[] plainBytes)
    {
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[GcmTagSize];

        using var gcm = new AesGcm(_key, GcmTagSize);
        gcm.Encrypt(nonce, plainBytes, cipher, tag);

        var result = new byte[1 + GcmNonceSize + GcmTagSize + cipher.Length];
        result[0] = EnvelopeVersionGcm;
        Buffer.BlockCopy(nonce, 0, result, 1, GcmNonceSize);
        Buffer.BlockCopy(tag, 0, result, 1 + GcmNonceSize, GcmTagSize);
        Buffer.BlockCopy(cipher, 0, result, 1 + GcmNonceSize + GcmTagSize, cipher.Length);
        return result;
    }

    private byte[] DecryptGcm(byte[] fullBytes)
    {
        if (fullBytes.Length < 1 + GcmNonceSize + GcmTagSize)
            throw new CryptographicException("密文数据格式无效：GCM 密文长度不足");

        var nonce = new byte[GcmNonceSize];
        var tag = new byte[GcmTagSize];
        var cipher = new byte[fullBytes.Length - 1 - GcmNonceSize - GcmTagSize];
        Buffer.BlockCopy(fullBytes, 1, nonce, 0, GcmNonceSize);
        Buffer.BlockCopy(fullBytes, 1 + GcmNonceSize, tag, 0, GcmTagSize);
        Buffer.BlockCopy(fullBytes, 1 + GcmNonceSize + GcmTagSize, cipher, 0, cipher.Length);

        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(_key, GcmTagSize);
        gcm.Decrypt(nonce, cipher, tag, plain);   // 校验失败抛 AuthenticationTagMismatchException（CryptographicException 子类）
        return plain;
    }
#endif

    // ---- v3：CBC + HMAC-SHA256 Encrypt-then-MAC（ns2.0/net6.0） ----

    private byte[] EncryptCbcThenHmac(byte[] plainBytes)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // MAC 覆盖 IV + 密文（Encrypt-then-MAC）
        var mac = ComputeHmac(aes.IV, cipherBytes);

        var result = new byte[1 + IvSizeBytes + HmacTagSize + cipherBytes.Length];
        result[0] = EnvelopeVersionCbcHmac;
        Buffer.BlockCopy(aes.IV, 0, result, 1, IvSizeBytes);
        Buffer.BlockCopy(mac, 0, result, 1 + IvSizeBytes, HmacTagSize);
        Buffer.BlockCopy(cipherBytes, 0, result, 1 + IvSizeBytes + HmacTagSize, cipherBytes.Length);
        return result;
    }

    private byte[] DecryptCbcThenHmac(byte[] fullBytes)
    {
        if (fullBytes.Length < 1 + IvSizeBytes + HmacTagSize)
            throw new CryptographicException("密文数据格式无效：CBC+HMAC 密文长度不足");

        var iv = new byte[IvSizeBytes];
        var mac = new byte[HmacTagSize];
        var cipherBytes = new byte[fullBytes.Length - 1 - IvSizeBytes - HmacTagSize];
        Buffer.BlockCopy(fullBytes, 1, iv, 0, IvSizeBytes);
        Buffer.BlockCopy(fullBytes, 1 + IvSizeBytes, mac, 0, HmacTagSize);
        Buffer.BlockCopy(fullBytes, 1 + IvSizeBytes + HmacTagSize, cipherBytes, 0, cipherBytes.Length);

        // 常量时间 MAC 校验（先验 MAC 再解密，防填充预言子）
        var computed = ComputeHmac(iv, cipherBytes);
        if (!FixedTimeEquals(computed, mac))
            throw new CryptographicException("密文完整性校验失败");

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = iv;

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
    }

    private byte[] ComputeHmac(byte[] iv, byte[] cipherBytes)
    {
        using var hmac = new HMACSHA256(_key);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);
        hmac.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return hmac.Hash!;
    }

    /// <summary>常量时间字节序列比较（与 DefaultHmacSignatureProvider 同一实现模式）。</summary>
    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;

        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
