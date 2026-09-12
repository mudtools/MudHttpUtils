using Microsoft.Extensions.Options;
using System.Linq;
using System.Security.Cryptography;

namespace Mud.HttpUtils.Tests;

public class AesEncryptionProviderTests
{
    private static readonly byte[] TestKey = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==");

    private static IEncryptionProvider CreateProvider(byte[]? key = null, bool requireCrossRuntimePortable = false)
    {
        var options = new AesEncryptionOptions
        {
            Key = key ?? (byte[])TestKey.Clone(),
            RequireCrossRuntimePortable = requireCrossRuntimePortable,
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
            Key = new byte[16]
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithValidKey24Bytes_ShouldNotThrow()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[24]
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithValidKey32Bytes_ShouldNotThrow()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[32]
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithInvalidKeyLength_ShouldThrowInvalidOperationException()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[10]
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AES Key 长度*");
    }

    [Fact]
    public void CFG27_AesEncryptionOptions_Validate_DoesNotRequireIV()
    {
        // CFG-27：IV 属性已移除，Validate 仅校验 Key（IV 自 v1.8.0 起自动随机生成）。
        var options = new AesEncryptionOptions
        {
            Key = new byte[16]
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithNullKey_ShouldThrowInvalidOperationException()
    {
        var options = new AesEncryptionOptions
        {
            Key = null!
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CFG27_AesEncryptionOptions_IVProperty_NoLongerExists()
    {
        // CFG-27：IV 属性已从公开面移除（运行时无消费点）。
        typeof(AesEncryptionOptions).GetProperty("IV").Should().BeNull();
    }

    [Fact]
    public void SectionName_ShouldBeMudHttpAesEncryption()
    {
        AesEncryptionOptions.SectionName.Should().Be("MudHttpAesEncryption");
    }

    #endregion

    #region DefaultAesEncryptionProvider.Dispose Tests

    private static DefaultAesEncryptionProvider CreateDisposableProvider(byte[]? key = null)
    {
        var options = new AesEncryptionOptions
        {
            Key = key ?? Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==")
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
    public void ClearSensitiveData_ShouldZeroOutKey()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }
        };

        options.ClearSensitiveData();

        options.Key.Should().BeEmpty();
    }

    [Fact]
    public void ClearSensitiveData_CalledMultipleTimes_ShouldNotThrow()
    {
        var options = new AesEncryptionOptions
        {
            Key = new byte[16]
        };

        options.ClearSensitiveData();
        var act = () => options.ClearSensitiveData();

        act.Should().NotThrow();
    }

    #endregion

    #region M2-#7 认证加密回归 (T-7.x)

    /// <summary>
    /// T-7.1 / T-7.4 验收：认证加密密文必须带版本前缀。
    /// net8+（AesGcm 可用）→ 0x02；否则 → 0x03（CBC+HMAC）。
    /// </summary>
    [Fact]
    public void AuthenticatedEncryption_Encrypt_ProducesVersionPrefix()
    {
        var provider = CreateProvider();
        var expected = AesGcm.IsSupported ? (byte)0x02 : (byte)0x03;

        var bytes = Convert.FromBase64String(provider.Encrypt("test"));

        bytes[0].Should().Be(expected);
    }

    /// <summary>
    /// T-7 验收：翻转密文任意 1 bit → 解密抛 <see cref="CryptographicException"/>（而非返回乱文）。
    /// 末字节属于密文体：GCM tag 校验失败 / CBC+HMAC MAC 校验失败。
    /// </summary>
    [Fact]
    public void AuthenticatedEncryption_FlipCiphertextBit_ThrowsCryptographicException()
    {
        var provider = CreateProvider();
        var bytes = Convert.FromBase64String(provider.Encrypt("secret payload"));
        bytes[^1] ^= 0x01;

        var act = () => provider.DecryptBytes(bytes);

        act.Should().Throw<CryptographicException>();
    }

    /// <summary>
    /// T-7 验收（续）：翻转 nonce/IV 字节（下标 1）→ GCM tag / HMAC MAC 均覆盖该字节 → 解密抛异常。
    /// </summary>
    [Fact]
    public void AuthenticatedEncryption_FlipNonceBit_ThrowsCryptographicException()
    {
        var provider = CreateProvider();
        var bytes = Convert.FromBase64String(provider.Encrypt("secret payload"));
        bytes[1] ^= 0x01;

        var act = () => provider.DecryptBytes(bytes);

        act.Should().Throw<CryptographicException>();
    }

    /// <summary>
    /// T-7.3 验收：v3（CBC + HMAC-SHA256 Encrypt-then-MAC）信封可被解密 ——
    /// 手工构造合法 v3 信封（测试进程为 net8，经此用例确定性覆盖 DecryptCbcThenHmac 路径）。
    /// </summary>
    [Fact]
    public void AuthenticatedEncryption_CbcHmacEnvelope_RoundTrips()
    {
        var provider = CreateProvider();
        var key = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==");
        var plain = Encoding.UTF8.GetBytes("manual v3 envelope");

        var envelope = BuildCbcHmacEnvelope(key, plain, tamper: false);

        provider.DecryptBytes(envelope).Should().Equal(plain);
    }

    /// <summary>
    /// T-7.3 验收（续）：篡改 v3 信封密文体 → 常量时间 MAC 校验失败 → 抛 <see cref="CryptographicException"/>。
    /// </summary>
    [Fact]
    public void AuthenticatedEncryption_CbcHmacEnvelope_TamperedMac_ThrowsCryptographicException()
    {
        var provider = CreateProvider();
        var key = Convert.FromBase64String("MTIzNDU2Nzg5MDEyMzQ1Ng==");
        var plain = Encoding.UTF8.GetBytes("manual v3 envelope");

        var envelope = BuildCbcHmacEnvelope(key, plain, tamper: true);

        var act = () => provider.DecryptBytes(envelope);

        act.Should().Throw<CryptographicException>();
    }

    /// <summary>构造 v3（CBC+HMAC）信封：<c>[0x03][IV(16)][MAC(32)][密文]</c>。</summary>
    private static byte[] BuildCbcHmacEnvelope(byte[] key, byte[] plain, bool tamper)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        byte[] cipher;
        using (var encryptor = aes.CreateEncryptor())
            cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

        // MAC 覆盖 IV + 密文（Encrypt-then-MAC，与 DecryptCbcThenHmac 的 ComputeHmac 一致）
        byte[] mac;
        using (var hmac = new HMACSHA256(key))
            mac = hmac.ComputeHash(aes.IV.Concat(cipher).ToArray());

        var envelope = new byte[1 + aes.IV.Length + mac.Length + cipher.Length];
        envelope[0] = 0x03;
        Buffer.BlockCopy(aes.IV, 0, envelope, 1, aes.IV.Length);
        Buffer.BlockCopy(mac, 0, envelope, 1 + aes.IV.Length, mac.Length);
        Buffer.BlockCopy(cipher, 0, envelope, 1 + aes.IV.Length + mac.Length, cipher.Length);

        if (tamper)
            envelope[^1] ^= 0x01;
        return envelope;
    }

    #endregion

    #region AES 信封版本前缀歧义消除 (AE-T11)

    /// <summary>
    /// AE-1/AE-4：net8+ 且未要求跨运行时可移植 → 密文首字节恒为 0x02。
    /// 循环 200 次消除随机性（nonce 随机，但版本字节必须恒定）。
    /// </summary>
    [Fact]
    public void Envelope_VersionSpace_Gcm_HasPrefix02()
    {
        if (!AesGcm.IsSupported) return;   // 环境不支持 GCM 时由下一条用例覆盖 0x03

        var provider = CreateProvider();

        for (var i = 0; i < 200; i++)
        {
            var bytes = Convert.FromBase64String(provider.Encrypt("payload " + i));
            bytes[0].Should().Be((byte)0x02);
        }
    }

    /// <summary>
    /// AE-4：非 GCM 运行时（或 <c>RequireCrossRuntimePortable=true</c>）→ 密文首字节恒为 0x03。
    /// </summary>
    [Fact]
    public void Envelope_VersionSpace_CbcHmac_HasPrefix03()
    {
        var provider = CreateProvider(requireCrossRuntimePortable: AesGcm.IsSupported);

        for (var i = 0; i < 200; i++)
        {
            var bytes = Convert.FromBase64String(provider.Encrypt("payload " + i));
            bytes[0].Should().Be((byte)0x03);
        }
    }

    /// <summary>
    /// AE-5（B-3）：net8+ 上强制 <c>RequireCrossRuntimePortable=true</c> → 产出 0x03，
    /// 且该密文仍可被默认（GCM）provider 解密（解密侧按前缀分派，与配置无关）。
    /// </summary>
    [Fact]
    public void Envelope_CrossRuntimePortable_ProducesCbcHmacOnNet8()
    {
        var portable = CreateProvider(requireCrossRuntimePortable: true);
        var plain = "cross-runtime payload";

        var cipher = Convert.FromBase64String(portable.Encrypt(plain));

        cipher[0].Should().Be((byte)0x03);
        CreateProvider().Decrypt(Convert.ToBase64String(cipher)).Should().Be(plain);
    }

    /// <summary>
    /// AE-2 根治验证：分派只依赖密文首字节，不依赖加密侧配置。
    /// 0x02 与 0x03 两种密文在任意配置的 provider 上解密结果一致。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decrypt_DispatchIgnoresRuntimeConfiguration(bool requireCrossRuntimePortable)
    {
        var plain = "configuration independent";
        var gcmCipher = Convert.FromBase64String(CreateProvider().Encrypt(plain));
        var cbcHmacCipher = Convert.FromBase64String(
            CreateProvider(requireCrossRuntimePortable: true).Encrypt(plain));

        // 两种前缀都必须是合法版本字节（非 GCM 环境下两者均为 0x03，断言仍成立）
        gcmCipher[0].Should().BeOneOf((byte)0x02, (byte)0x03);
        cbcHmacCipher[0].Should().BeOneOf((byte)0x02, (byte)0x03);

        var reader = CreateProvider(requireCrossRuntimePortable: requireCrossRuntimePortable);
        reader.Decrypt(Convert.ToBase64String(gcmCipher)).Should().Be(plain);
        reader.Decrypt(Convert.ToBase64String(cbcHmacCipher)).Should().Be(plain);
    }

    /// <summary>
    /// AE-1/AE-3：逐字节遍历所有非法版本字节（0x00、0x01、0x04~0xFF）→
    /// 均抛 <see cref="CryptographicException"/> 且消息含实际首字节。
    /// 确定性构造，不依赖随机 IV（E-4 转正）。
    /// </summary>
    [Fact]
    public void Decrypt_UnrecognizedVersion_ThrowsWithActualByte()
    {
        var provider = CreateProvider();

        for (var b = 0; b <= 0xFF; b++)
        {
            if (b is 0x02 or 0x03) continue;

            var input = new byte[1 + 16 + 32 + 16];
            input[0] = (byte)b;

            var act = () => provider.DecryptBytes(input);

            act.Should().Throw<CryptographicException>()
                .WithMessage($"*0x{b:X2}*");
        }
    }

    /// <summary>AE-6：空数组 → <see cref="CryptographicException"/>（格式类错误统一异常类型）。</summary>
    [Fact]
    public void Decrypt_ZeroLength_ThrowsCryptographicException()
    {
        var provider = CreateProvider();

        var act = () => provider.DecryptBytes(Array.Empty<byte>());

        act.Should().Throw<CryptographicException>().WithMessage("*长度*");
    }

    /// <summary>AE-3：长度校验前置 —— v3 信封长度不足时抛「格式」异常（含最少字节数），而非解密失败/索引越界。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(48)]
    [InlineData(64)]
    public void Decrypt_CbcHmacTooShort_ThrowsFormatError(int length)
    {
        var provider = CreateProvider();
        var input = new byte[length];
        input[0] = 0x03;

        var act = () => provider.DecryptBytes(input);

        act.Should().Throw<CryptographicException>()
            .WithMessage("*密文数据格式无效*")
            .WithMessage("*最少 65 字节*");
    }

    /// <summary>AE-3（续）：v2 信封长度不足时抛「格式」异常；运行时不支持 GCM 时抛出明确的不可解密提示。</summary>
    [Fact]
    public void Decrypt_GcmTooShort_ThrowsCryptographicException()
    {
        var provider = CreateProvider();
        var input = new byte[1];
        input[0] = 0x02;

        var act = () => provider.DecryptBytes(input);

        act.Should().Throw<CryptographicException>()
            .WithMessage(AesGcm.IsSupported ? "*密文数据格式无效*" : "*AesGcm*");
    }

    /// <summary>往返一致性：空明文 / 1 字节 / 1MB，覆盖 0x02 与 0x03 两种格式。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_AllProducedFormats(bool requireCrossRuntimePortable)
    {
        var provider = CreateProvider(requireCrossRuntimePortable: requireCrossRuntimePortable);
        var large = new string('x', 1024 * 1024);

        foreach (var plain in new[] { string.Empty, "a", "Hello, 世界! @#$%", large })
        {
            provider.Decrypt(provider.Encrypt(plain)).Should().Be(plain);
        }
    }

    /// <summary>边界：二进制 API 的空数组与 1 字节往返。</summary>
    [Fact]
    public void RoundTrip_EmptyAndSingleByteBytes()
    {
        var provider = CreateProvider();

        provider.DecryptBytes(provider.EncryptBytes(Array.Empty<byte>())).Should().BeEmpty();
        provider.DecryptBytes(provider.EncryptBytes(new byte[] { 0xAB })).Should().Equal(new byte[] { 0xAB });
    }

    /// <summary>完整性：0x02 与 0x03 密文的密文位 / nonce(IV) 位翻转均必须抛异常。</summary>
    [Fact]
    public void Envelope_Tamper_AnyFormat_Throws()
    {
        foreach (var portable in new[] { false, true })
        {
            var provider = CreateProvider(requireCrossRuntimePortable: portable);

            var tail = Convert.FromBase64String(provider.Encrypt("tamper me"));
            tail[^1] ^= 0x01;
            Action tailAct = () => provider.DecryptBytes(tail);
            tailAct.Should().Throw<CryptographicException>();

            var head = Convert.FromBase64String(provider.Encrypt("tamper me"));
            head[1] ^= 0x01;   // nonce / IV 首字节，均在 MAC/tag 覆盖范围内
            Action headAct = () => provider.DecryptBytes(head);
            headAct.Should().Throw<CryptographicException>();
        }
    }

    /// <summary>BC-AE2 防回潮：<c>EnableAuthenticatedEncryption</c> 已从公开面移除。</summary>
    [Fact]
    public void PublicApi_EnableAuthenticatedEncryption_Removed()
    {
        typeof(AesEncryptionOptions).GetProperty("EnableAuthenticatedEncryption").Should().BeNull();

        foreach (var name in new[]
                 {
                     "Mud.HttpUtils.Abstractions/PublicAPI/netstandard2.0/PublicAPI.Unshipped.txt",
                     "Mud.HttpUtils.Abstractions/PublicAPI/net6.0/PublicAPI.Unshipped.txt",
                     "Mud.HttpUtils.Abstractions/PublicAPI/net8.0/PublicAPI.Unshipped.txt",
                     "Mud.HttpUtils.Abstractions/PublicAPI/net10.0/PublicAPI.Unshipped.txt",
                 })
        {
            var path = Path.Combine(RepoRoot, name);
            File.Exists(path).Should().BeTrue($"{name} 应存在");
            File.ReadAllText(path).Should().NotContain("EnableAuthenticatedEncryption");
            File.ReadAllText(path).Should().Contain("RequireCrossRuntimePortable.get -> bool");
        }
    }

    /// <summary>BC-AE4：<c>RequireCrossRuntimePortable</c> 默认值为 <c>false</c>。</summary>
    [Fact]
    public void PublicApi_RequireCrossRuntimePortable_DefaultFalse()
    {
        new AesEncryptionOptions { Key = (byte[])TestKey.Clone() }
            .RequireCrossRuntimePortable.Should().BeFalse();
    }

    /// <summary>测试工程根目录下的仓库根路径（用于 PublicAPI 基线断言）。</summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mud.HttpUtils.slnx")))
                dir = dir.Parent;

            return dir?.FullName ?? AppContext.BaseDirectory;
        }
    }

    #endregion
}
