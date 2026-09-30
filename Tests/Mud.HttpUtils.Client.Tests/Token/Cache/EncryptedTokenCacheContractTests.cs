// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mud.HttpUtils.Client.Tests.Token;

/// <summary>
/// S1-4（治理方案）：<see cref="EncryptedTokenCache{T}"/> 专用契约测试。
/// 此前该实现仅有分散引用、无专用契约文件 —— 本文件锁定加解密往返、
/// 密文损坏按 miss 处理（绝不抛出）、驱逐回调透传与生命周期转发语义。
/// </summary>
public class EncryptedTokenCacheContractTests
{
    private sealed class Payload
    {
        public string Token { get; set; } = string.Empty;
    }

    /// <summary>可注入故障的加密提供程序：默认 AES 简单异或伪装（仅测试用），可切换为抛出模式。</summary>
    private sealed class FakeEncryption : IEncryptionProvider
    {
        public bool FailDecrypt;
        public bool FailEncrypt;

        public string Encrypt(string plainText)
        {
            if (FailEncrypt)
                throw new CryptographicException("加密故障注入");
            var bytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return Convert.ToBase64String(bytes);
        }

        public string Decrypt(string cipherText)
        {
            if (FailDecrypt)
                throw new CryptographicException("密钥轮换（测试注入）");
            var bytes = Convert.FromBase64String(cipherText);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        public byte[] EncryptBytes(byte[] plainBytes) => plainBytes;

        public byte[] DecryptBytes(byte[] cipherBytes) => cipherBytes;
    }

    private static (EncryptedTokenCache<Payload> Cache, MemoryCacheTokenCache<string> Inner, FakeEncryption Encryption) Create()
    {
        var inner = new MemoryCacheTokenCache<string>();
        var encryption = new FakeEncryption();
        return (new EncryptedTokenCache<Payload>(inner, encryption), inner, encryption);
    }

    // ---- 加解密往返 ----

    [Fact]
    public void Set_TryGet_RoundTripsWithEncryption()
    {
        var (cache, inner, _) = Create();

        cache.Set("k1", new Payload { Token = "secret-1" });

        inner.TryGet("k1", out var stored).Should().BeTrue();
        stored.Should().NotContain("secret-1", "底层缓存中必须是密文（Base64 化）");

        cache.TryGet("k1", out var value).Should().BeTrue();
        value!.Token.Should().Be("secret-1");
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var (cache, _, _) = Create();

        cache.TryGet("missing", out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    // ---- 密文损坏 / 解密失败：按 miss 处理，绝不抛出（§0.3-V3） ----

    [Fact]
    public void TryGet_DecryptFailure_TreatedAsMiss_DoesNotThrow()
    {
        var (cache, _, encryption) = Create();
        cache.Set("k1", new Payload { Token = "secret-1" });

        encryption.FailDecrypt = true;   // 模拟密钥轮换 / 密文损坏

        var act = () => cache.TryGet("k1", out _);
        act.Should().NotThrow();
        cache.TryGet("k1", out var value).Should().BeFalse("解密失败按缓存未命中处理，触发上层重新获取令牌");
        value.Should().BeNull();
    }

    [Fact]
    public void Set_SerializeFailure_DegradesToNoCache_DoesNotThrow()
    {
        var inner = new MemoryCacheTokenCache<string>();
        var encryption = new FakeEncryption();
        // AOT 场景模拟：无可用 JsonTypeInfoResolver 的载荷无法序列化 → 降级为"不缓存"
        var cache = new EncryptedTokenCache<Payload>(inner, encryption,
            new JsonSerializerOptions { Converters = { new ThrowingJsonConverter() } });

        var act = () => cache.Set("k1", new Payload { Token = "secret-1" });

        act.Should().NotThrow("序列化失败降级为不缓存，绝不打断令牌流水线（TMX-11）");
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void Set_EncryptFailure_Propagates_InnerCacheUntouched()
    {
        var (cache, inner, encryption) = Create();
        encryption.FailEncrypt = true;   // 加密引擎故障（如密钥配置缺失）

        var act = () => cache.Set("k1", new Payload { Token = "secret-1" });

        // 刻意与序列化失败不同：加密故障属配置性故障，快速失败暴露问题（明文绝不落入内层缓存）；
        // 解密侧（TryGet）仍按 miss 降级 —— 读路径 fail-open、写路径 fail-fast 的非对称语义由本用例锁定。
        act.Should().Throw<CryptographicException>();
        inner.Count.Should().Be(0, "明文不得在加密失败时写入内层缓存");
    }

    private sealed class ThrowingJsonConverter : JsonConverter<Payload>
    {
        public override Payload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new JsonException("测试注入");

        public override void Write(Utf8JsonWriter writer, Payload value, JsonSerializerOptions options)
            => throw new JsonException("测试注入");
    }

    // ---- Set(null) / TryRemove / 回调 ----

    [Fact]
    public void SetNull_RemovesFromInner_AndFiresCallerCallback()
    {
        var (cache, inner, _) = Create();
        cache.Set("k1", new Payload { Token = "secret-1" });
        var evicted = new List<string>();

        cache.Set("k1", null, null, null, k => evicted.Add(k));

        inner.TryGet("k1", out _).Should().BeFalse("null 值语义 = 从底层缓存移除");
        evicted.Should().Contain("k1");
    }

    [Fact]
    public void TryRemove_ReturnsDecryptedValue_AndRemovesFromInner()
    {
        var (cache, inner, _) = Create();
        cache.Set("k1", new Payload { Token = "secret-1" });

        cache.TryRemove("k1", out var removed).Should().BeTrue();
        removed!.Token.Should().Be("secret-1", "TMX-15-1：先解密得到 removed，使失效调用方能拿到失效前令牌");
        inner.TryGet("k1", out _).Should().BeFalse();
    }

    // ---- 透传语义 ----

    [Fact]
    public void Keys_Count_DelegateToInner()
    {
        var (cache, inner, _) = Create();
        cache.Set("k1", new Payload { Token = "a" });
        cache.Set("k2", new Payload { Token = "b" });

        cache.Count.Should().Be(inner.Count);
        cache.Keys.Should().BeEquivalentTo(inner.Keys);
    }

    [Fact]
    public void Clear_Compact_DelegateToInner()
    {
        var (cache, inner, _) = Create();
        for (var i = 0; i < 4; i++)
            cache.Set("k" + i, new Payload { Token = "t" + i });

        cache.Compact(0.5);
        cache.Count.Should().Be(inner.Count);

        cache.Clear();
        cache.Count.Should().Be(0);
        inner.Count.Should().Be(0);
    }

    [Fact]
    public void Dispose_DelegatesToInner()
    {
        var (cache, inner, _) = Create();

        cache.Dispose();

        // MemoryCacheTokenCache.Dispose 后 TryGet 返回 false（不抛）
        inner.TryGet("k1", out _).Should().BeFalse();
    }

    // ---- 过期策略透传 ----

    [Fact]
    public void Set_WithExpiry_DelegatesToInnerMemoryCache()
    {
        var (cache, _, _) = Create();

        cache.Set("k1", new Payload { Token = "a" }, TimeSpan.FromMinutes(5), null);

        cache.TryGet("k1", out var value).Should().BeTrue("MemoryCacheTokenCache 支持绝对过期，加密包装应透传");
        value!.Token.Should().Be("a");
    }
}
