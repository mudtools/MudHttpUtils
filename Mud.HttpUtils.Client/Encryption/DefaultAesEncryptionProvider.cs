// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Threading;

namespace Mud.HttpUtils;

/// <summary>
/// 默认 AES 加密提供程序实现，使用 AES 对称加密算法进行数据加密和解密。
/// </summary>
/// <remarks>
/// <para>
/// 本提供程序<b>始终使用认证加密</b>（AEAD / Encrypt-then-MAC）。产出的密文首字节为<b>信封版本前缀</b>，
/// 格式为二者之一（见 <see cref="AesEncryptionOptions.RequireCrossRuntimePortable"/>）：
/// </para>
/// <list type="bullet">
/// <item><c>0x02</c>（net8.0/net10.0 且 <c>AesGcm.IsSupported</c>）：AesGcm —— <c>[0x02][nonce(12)][tag(16)][密文]</c></item>
/// <item><c>0x03</c>：CBC + HMAC-SHA256 Encrypt-then-MAC —— <c>[0x03][IV(16)][MAC(32)][密文]</c></item>
/// </list>
/// <para>
/// <b>解密仅按首字节版本前缀分派，不依赖任何运行时配置</b>：格式由密文自身唯一确定，
/// 从而杜绝"配置变更导致同一段密文被按另一种格式解析"的静默数据损坏风险。
/// 完整性校验失败抛 <see cref="CryptographicException"/>（而非返回乱码）。
/// </para>
/// <para>
/// 版本字节空间：<c>0x00</c> 保留为非法哨兵（永不用作版本，便于识别全零/未初始化数据）；
/// <c>0x01</c> 曾用于 v1 裸 CBC，该路径已移除且编号<b>永久冻结不再复用</b>；
/// <c>0x02</c>/<c>0x03</c> 在用；<c>0x04</c>~<c>0x0F</c> 保留给未来对称算法；<c>0x10</c>~<c>0xFF</c> 保留给未来扩展。
/// </para>
/// </remarks>
public sealed class DefaultAesEncryptionProvider : IEncryptionProvider, IDisposable
{
    // ---- 信封版本字节空间（详见类备注；0x00/0x01 仅文档声明，不定义未使用的常量）----

    /// <summary>v2：AesGcm。布局 <c>[0x02][nonce(12)][tag(16)][密文]</c>。</summary>
    private const byte EnvelopeVersionGcm = 0x02;

    /// <summary>v3：CBC + HMAC-SHA256（Encrypt-then-MAC）。布局 <c>[0x03][IV(16)][MAC(32)][密文]</c>。</summary>
    private const byte EnvelopeVersionCbcHmac = 0x03;

    /// <summary>
    /// v4：CBC + HMAC-SHA256 + HKDF 密钥分离。布局 <c>[0x04][IV(16)][MAC(32)][密文]</c>（M5-HC-13）。
    /// </summary>
    private const byte EnvelopeVersionCbcHmacKeySep = 0x04;

    // ---- 尺寸常量 ----

    private const int IvSizeBytes = 16;
    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;
    private const int HmacTagSize = 32; // HMAC-SHA256
    private const int CipherBlockSizeBytes = 16; // AES 分组长度；PKCS7 下密文段恒为 16 的整数倍

    // ---- 各格式最小长度（前缀 + 元数据 + 至少 1 个密文分组）----

    private const int MinGcmLength = 1 + GcmNonceSize + GcmTagSize;               // 29（GCM 为流模式，密文段可为 0）
    private const int MinCbcHmacLength = 1 + IvSizeBytes + HmacTagSize + CipherBlockSizeBytes; // 65

    private byte[] _key;
    /// <summary>M5-HC-13：HKDF 派生的 AES 加密子密钥（EnableKeySeparation 时使用）。</summary>
    private byte[]? _encKey;
    /// <summary>M5-HC-13：HKDF 派生的 HMAC 子密钥（EnableKeySeparation 时使用）。</summary>
    private byte[]? _macKey;
    private readonly bool _enableKeySeparation;
    private bool _disposed;

    /// <summary>
    /// TMX-15-6 (C7)：以 Volatile.Read 读取密钥，消除与 Dispose 清零的竞态。
    /// Dispose 可能在另一线程执行 SecurityHelper.ClearBytes(_key)，
    /// 不加 volatile 读则加密/解密线程可能读到已清零的密钥。
    /// </summary>
    private byte[] KeyForCrypto
    {
        get
        {
            var key = Volatile.Read(ref _key);
            if (key == null || key.Length == 0)
                throw new ObjectDisposedException(nameof(DefaultAesEncryptionProvider));
            return key;
        }
    }

    /// <summary>net8+ 是否可用 AesGcm 且未被 <see cref="AesEncryptionOptions.RequireCrossRuntimePortable"/> 关闭。</summary>
    private readonly bool _useGcm;

    /// <summary>
    /// 初始化 <see cref="DefaultAesEncryptionProvider"/> 实例。
    /// </summary>
    /// <param name="options">AES 加密选项。</param>
    /// <param name="logger">可选日志记录器；当运行时不支持 AesGcm 时记录降级提示（EventId 119）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 或其 <c>Value</c> 为 null。</exception>
    public DefaultAesEncryptionProvider(
        IOptions<AesEncryptionOptions> options,
        ILogger<DefaultAesEncryptionProvider>? logger = null)
    {
        if (options?.Value == null)
            throw new ArgumentNullException(nameof(options));

        options.Value.Validate();
        _key = options.Value.Key;      // getter 已返回克隆（TMR-06：不再修改 options.Value）
        var requireCrossRuntimePortable = options.Value.RequireCrossRuntimePortable;
        _enableKeySeparation = options.Value.EnableKeySeparation;

        // M5-HC-13：HKDF 派生 enc/mac 子密钥（纯托管，全 TFM 可用）
        if (_enableKeySeparation)
        {
            _encKey = HkdfExpand(_key, "Mud.HttpUtils:AES:enc", _key.Length);
            _macKey = HkdfExpand(_key, "Mud.HttpUtils:AES:mac", 32);
        }

        _useGcm = ResolveUseGcm(requireCrossRuntimePortable);

        // AE-7：不支持 GCM 时静默降级会导致"为什么这份密文是 0x03"不可诊断，此处显式记录。
        if (!_useGcm)
        {
            MudHttpClientLog.AesGcmUnavailableFallbackToCbcHmac(
                logger ?? NullLogger<DefaultAesEncryptionProvider>.Instance);
        }
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
    /// <exception cref="CryptographicException">
    /// 密文格式无法识别（缺少版本前缀或长度不足），或完整性校验失败（密文被篡改）时抛出。
    /// </exception>
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
    /// <exception cref="CryptographicException">
    /// 密文格式无法识别（缺少版本前缀或长度不足），或完整性校验失败（密文被篡改）时抛出。
    /// </exception>
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
        if (_encKey != null) { SecurityHelper.ClearBytes(_encKey); _encKey = []; }
        if (_macKey != null) { SecurityHelper.ClearBytes(_macKey); _macKey = []; }
    }

    private static bool ResolveUseGcm(bool requireCrossRuntimePortable)
    {
        if (requireCrossRuntimePortable)
            return false;

#if NET8_0_OR_GREATER
        return AesGcm.IsSupported;
#else
        return false;
#endif
    }

    private byte[] EncryptCore(byte[] plainBytes)
    {
#if NET8_0_OR_GREATER
        if (_useGcm)
            return EncryptGcm(plainBytes);
#endif
        return EncryptCbcThenHmac(plainBytes);
    }

    /// <summary>
    /// 按密文首字节（信封版本前缀）分派解密。<b>不读取任何配置字段</b>。
    /// </summary>
    private byte[] DecryptCore(byte[] fullBytes)
    {
        if (fullBytes.Length == 0)
            throw new CryptographicException("密文数据格式无效：长度为 0，缺少信封版本前缀。");

        switch (fullBytes[0])
        {
            case EnvelopeVersionGcm:
#if NET8_0_OR_GREATER
                if (AesGcm.IsSupported)
                {
                    EnsureMinLength(fullBytes.Length, MinGcmLength, "v2 AesGcm");
                    return DecryptGcm(fullBytes);
                }
                throw new CryptographicException(
                    "密文为 v2(AesGcm) 格式，但当前运行时不支持 AesGcm。请在 net8.0+ 运行时解密，"
                    + "或在加密侧设置 AesEncryptionOptions.RequireCrossRuntimePortable = true 产出可跨运行时解密的 v3 格式。");
#else
                throw new CryptographicException(
                    "密文为 v2(AesGcm) 格式，但当前目标框架不支持 AesGcm。请在 net8.0+ 目标框架下解密，"
                    + "或在加密侧设置 AesEncryptionOptions.RequireCrossRuntimePortable = true 产出可跨运行时解密的 v3 格式。");
#endif

            case EnvelopeVersionCbcHmac:
                EnsureMinLength(fullBytes.Length, MinCbcHmacLength, "v3 CBC+HMAC");
                return DecryptCbcThenHmac(fullBytes, keySeparated: false);

            case EnvelopeVersionCbcHmacKeySep:
                // M5-HC-13：HKDF 密钥分离格式
                EnsureMinLength(fullBytes.Length, MinCbcHmacLength, "v4 CBC+HMAC (key-separated)");
                return DecryptCbcThenHmac(fullBytes, keySeparated: true);

            default:
                // 0x00（保留哨兵）、0x01（v1 裸 CBC，已废弃且编号冻结）以及所有未分配值均在此拒绝。
                throw new CryptographicException(
                    $"密文格式无法识别：缺少 Mud.HttpUtils 信封版本前缀（期望 0x02、0x03 或 0x04），"
                    + $"实际首字节为 0x{fullBytes[0]:X2}。");
        }
    }

    /// <summary>长度校验前置：在任何解密操作之前拒绝结构不完整的密文（AE-3）。</summary>
    private static void EnsureMinLength(int actual, int min, string formatName)
    {
        if (actual < min)
        {
            throw new CryptographicException(
                $"密文数据格式无效：{formatName} 密文长度不足（实际 {actual} 字节，最少 {min} 字节）。");
        }
    }

    // ---- v2：AesGcm（net8.0+） ----

#if NET8_0_OR_GREATER
    private byte[] EncryptGcm(byte[] plainBytes)
    {
        var nonce = RandomNumberGenerator.GetBytes(GcmNonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[GcmTagSize];

        var key = KeyForCrypto;  // TMX-15-6
        using var gcm = new AesGcm(key, GcmTagSize);
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
        var nonce = new byte[GcmNonceSize];
        var tag = new byte[GcmTagSize];
        var cipher = new byte[fullBytes.Length - 1 - GcmNonceSize - GcmTagSize];
        Buffer.BlockCopy(fullBytes, 1, nonce, 0, GcmNonceSize);
        Buffer.BlockCopy(fullBytes, 1 + GcmNonceSize, tag, 0, GcmTagSize);
        Buffer.BlockCopy(fullBytes, 1 + GcmNonceSize + GcmTagSize, cipher, 0, cipher.Length);

        var key = KeyForCrypto;  // TMX-15-6
        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, GcmTagSize);
        gcm.Decrypt(nonce, cipher, tag, plain);   // 校验失败抛 AuthenticationTagMismatchException（CryptographicException 子类）
        return plain;
    }
#endif

    // ---- v3/v4：CBC + HMAC-SHA256 Encrypt-then-MAC（全目标框架） ----

    private byte[] EncryptCbcThenHmac(byte[] plainBytes)
    {
        var key = KeyForCrypto;  // TMX-15-6
        // M5-HC-13：启用密钥分离时产出 0x04，否则回退 0x03
        var version = _enableKeySeparation ? EnvelopeVersionCbcHmacKeySep : EnvelopeVersionCbcHmac;
        var aesKey = _enableKeySeparation ? Volatile.Read(ref _encKey)! : key;

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // MAC 覆盖 IV + 密文（Encrypt-then-MAC）
        var mac = ComputeHmac(aes.IV, cipherBytes, useSeparatedMacKey: _enableKeySeparation);

        var result = new byte[1 + IvSizeBytes + HmacTagSize + cipherBytes.Length];
        result[0] = version;
        Buffer.BlockCopy(aes.IV, 0, result, 1, IvSizeBytes);
        Buffer.BlockCopy(mac, 0, result, 1 + IvSizeBytes, HmacTagSize);
        Buffer.BlockCopy(cipherBytes, 0, result, 1 + IvSizeBytes + HmacTagSize, cipherBytes.Length);
        return result;
    }

    private byte[] DecryptCbcThenHmac(byte[] fullBytes, bool keySeparated)
    {
        var iv = new byte[IvSizeBytes];
        var mac = new byte[HmacTagSize];
        var cipherBytes = new byte[fullBytes.Length - 1 - IvSizeBytes - HmacTagSize];
        Buffer.BlockCopy(fullBytes, 1, iv, 0, IvSizeBytes);
        Buffer.BlockCopy(fullBytes, 1 + IvSizeBytes, mac, 0, HmacTagSize);
        Buffer.BlockCopy(fullBytes, 1 + IvSizeBytes + HmacTagSize, cipherBytes, 0, cipherBytes.Length);

        // 常量时间 MAC 校验（先验 MAC 再解密，防填充预言子）
        var computed = ComputeHmac(iv, cipherBytes, useSeparatedMacKey: keySeparated);
        if (!SecurityHelper.FixedTimeEquals(computed, mac))
            throw new CryptographicException("密文完整性校验失败");

        var key = keySeparated ? Volatile.Read(ref _encKey) ?? KeyForCrypto : KeyForCrypto;  // TMX-15-6
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = iv;

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
    }

    /// <summary>
    /// 计算 <c>HMAC-SHA256(IV || 密文)</c>。
    /// </summary>
    private byte[] ComputeHmac(byte[] iv, byte[] cipherBytes, bool useSeparatedMacKey = false)
    {
        var key = useSeparatedMacKey && _macKey != null ? Volatile.Read(ref _macKey)! : KeyForCrypto;  // TMX-15-6
        using var hmac = new HMACSHA256(key);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);
        hmac.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return hmac.Hash!;
    }

    /// <summary>M5-HC-13：HKDF-Expand（RFC 5869）—— 以主密钥为 PRK，info 区分用途，产出指定长度子密钥。</summary>
    private static byte[] HkdfExpand(byte[] prk, string info, int length)
    {
        var infoBytes = System.Text.Encoding.UTF8.GetBytes(info);
        var result = new byte[length];
        var t = Array.Empty<byte>();
        var offset = 0;
        byte counter = 1;

        using var hmac = new HMACSHA256(prk);
        while (offset < length)
        {
            var input = new byte[t.Length + infoBytes.Length + 1];
            Buffer.BlockCopy(t, 0, input, 0, t.Length);
            Buffer.BlockCopy(infoBytes, 0, input, t.Length, infoBytes.Length);
            input[input.Length - 1] = counter++;

            t = hmac.ComputeHash(input);
            var toCopy = Math.Min(t.Length, length - offset);
            Buffer.BlockCopy(t, 0, result, offset, toCopy);
            offset += toCopy;
        }
        return result;
    }
}
