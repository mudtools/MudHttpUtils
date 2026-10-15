// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// U-1：<see cref="MigrationTokenStoreCodec"/> 迁移期双读编解码契约 ——
/// Encode 与 <see cref="DefaultTokenStoreCodec"/> 逐字节一致（C-2 裁决对表守卫）、
/// Decode 双读矩阵（旧绝对毫秒戳 / 新相对秒 / 阈值边界 / 非法格式 / 空串墓碑）、探针钩子兜底。
/// </summary>
public class MigrationTokenStoreCodecTests
{
    /// <summary>固定时钟：2026-10-15T00:00:00Z（Unix 毫秒 13 位量级）。</summary>
    private static readonly DateTimeOffset FixedNow =
        new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private static long NowMs => FixedNow.ToUnixTimeMilliseconds();

    private static MigrationTokenStoreCodec CreateCodec(
        long? threshold = null,
        ILegacyFormatProbe? probe = null,
        ITokenStoreCodec? newFormatCodec = null)
        => new(threshold, probe, newFormatCodec, () => FixedNow);

    // ── C-2 裁决对表：Encode 逐字节一致守卫 ─────────────────────────────

    [Theory]
    [InlineData("token-value", 3600L)]
    [InlineData("t", 0L)]
    [InlineData("t", -5L)]
    [InlineData("access-token-abc", 7200L)]
    public void Encode_Should_ByteEqual_DefaultCodec(string token, long expiresInSeconds)
    {
        var migration = CreateCodec();
        var expected = DefaultTokenStoreCodec.Instance.Encode(token, expiresInSeconds);

        migration.Encode(token, expiresInSeconds).Should().Be(expected,
            "C-2 裁决：Encode 单写相对秒，与默认 codec 逐字节一致");
    }

    [Fact]
    public void Encode_EmptyToken_Should_ReturnNull_Tombstone()
    {
        var migration = CreateCodec();
        migration.Encode(null, 3600).Should().BeNull();
        migration.Encode("", 3600).Should().BeNull();
    }

    // ── Decode 双读矩阵 ────────────────────────────────────────────────

    [Theory]
    [InlineData("token-a", 3600L)]
    [InlineData("token-b", 0L)]
    [InlineData("token-c", 9_999_999_999L)]   // 阈值下的大量级相对秒（10 位）仍判相对秒
    public void Decode_NewFormat_RelativeSeconds_Should_PassThrough(string token, long seconds)
    {
        var codec = CreateCodec();
        var encoded = $"{seconds}|{token}";

        var decoded = codec.Decode(encoded);

        decoded.Should().NotBeNull();
        decoded!.Value.Token.Should().Be(token);
        decoded.Value.ExpiresInSeconds.Should().Be(seconds, "前导数 < 阈值 → 相对秒直读");
    }

    [Fact]
    public void Decode_LegacyAbsoluteMilliseconds_Should_ProjectToRemainingSeconds()
    {
        var codec = CreateCodec();
        // 旧格式：{expiresAtMs}|{token}；expiresAtMs = now + 30 分钟
        var expiresAtMs = NowMs + 30 * 60 * 1000;
        var encoded = $"{expiresAtMs}|legacy-token";

        var decoded = codec.Decode(encoded);

        decoded.Should().NotBeNull();
        decoded!.Value.Token.Should().Be("legacy-token");
        decoded.Value.ExpiresInSeconds.Should().Be(1800, "旧绝对毫秒戳按 expiresAtMs - now 投影剩余秒");
    }

    [Fact]
    public void Decode_LegacyExpired_Should_ProjectToNegativeRemaining()
    {
        var codec = CreateCodec();
        // 已过期旧值：expiresAtMs < now，floor 取整后必须为负（不得被"无 TTL 信息 = 0"语义吞掉）
        var expiresAtMs = NowMs - 500;   // 过期 500ms → floor(-0.5) = -1
        var encoded = $"{expiresAtMs}|stale-token";

        var decoded = codec.Decode(encoded);

        decoded.Should().NotBeNull();
        decoded!.Value.Token.Should().Be("stale-token");
        decoded.Value.ExpiresInSeconds.Should().Be(-1, "floor 取整保证过期旧值投影为负数");
    }

    [Theory]
    [InlineData(100_000_000_000L, true)]    // == 阈值 → 旧格式
    [InlineData(100_000_000_000L - 1, false)] // < 阈值 → 相对秒
    public void Decode_ThresholdBoundary_Should_SplitFormats(long leadingValue, bool expectLegacy)
    {
        var codec = CreateCodec();
        var encoded = leadingValue == 100_000_000_000L
            ? $"{NowMs + 60_000}|boundary-token"       // 旧格式路径（投影剩 60 秒）
            : $"{leadingValue}|boundary-token";        // 相对秒路径

        var decoded = codec.Decode(encoded);

        decoded.Should().NotBeNull();
        if (expectLegacy)
            decoded!.Value.ExpiresInSeconds.Should().Be(60, "≥ 阈值判旧格式并投影");
        else
            decoded!.Value.ExpiresInSeconds.Should().Be(leadingValue, "< 阈值判相对秒直读");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("|token-only")]
    [InlineData("not-a-number|token")]
    public void Decode_InvalidInput_Should_ReturnNull(string? encoded)
    {
        CreateCodec().Decode(encoded).Should().BeNull("非法格式不得抛异常（miss 语义）");
    }

    [Fact]
    public void Decode_EmptyTokenSegment_Should_PreserveDefaultCodecNullTokenSemantics()
    {
        var codec = CreateCodec();
        // "3600|" 尾段为空：默认 codec Decode 返回 (null, 3600)，迁移 codec 不得改变该语义
        var decoded = codec.Decode("3600|");

        decoded.Should().NotBeNull();
        decoded!.Value.Token.Should().BeNull();
        decoded.Value.ExpiresInSeconds.Should().Be(3600);
    }

    // ── 探针钩子兜底 ───────────────────────────────────────────────────

    private sealed class PrefixedLegacyProbe : ILegacyFormatProbe
    {
        public bool TryDecodeLegacy(string encoded, out string? token, out long expiresInSeconds)
        {
            // 假想下游旧格式："legacy:{expireMs}|{token}"
            const string prefix = "legacy:";
            if (!encoded.StartsWith(prefix, StringComparison.Ordinal))
            {
                token = null;
                expiresInSeconds = 0;
                return false;
            }

            var separatorIndex = encoded.IndexOf('|', prefix.Length);
            if (separatorIndex < 0 || !long.TryParse(
                    encoded.Substring(prefix.Length, separatorIndex - prefix.Length),
                    out var expireMs))
            {
                token = null;
                expiresInSeconds = 0;
                return false;
            }

            token = encoded.Substring(separatorIndex + 1);
            expiresInSeconds = (expireMs - NowMs) / 1000;
            return true;
        }
    }

    [Fact]
    public void Decode_ProbeHook_Should_HandleCustomLegacyFormat()
    {
        var codec = CreateCodec(probe: new PrefixedLegacyProbe());
        var encoded = $"legacy:{NowMs + 120_000}|probed-token";

        var decoded = codec.Decode(encoded);

        decoded.Should().NotBeNull("内建判别失败 → 探针兜底");
        decoded!.Value.Token.Should().Be("probed-token");
        decoded.Value.ExpiresInSeconds.Should().Be(120);
    }

    [Fact]
    public void Decode_BuiltinFormats_Should_NotInvokeProbe()
    {
        var probeInvoked = false;
        var probe = new RecordingProbe(() => probeInvoked = true);
        var codec = CreateCodec(probe: probe);

        // 新格式直读：探针不得被回调
        codec.Decode($"{3600}|tok").Should().NotBeNull();
        probeInvoked.Should().BeFalse();

        // 旧格式内建判别命中：探针同样不得被回调
        codec.Decode($"{NowMs + 60_000}|tok").Should().NotBeNull();
        probeInvoked.Should().BeFalse();
    }

    [Fact]
    public void Decode_ProbeFails_Should_ReturnNull()
    {
        var codec = CreateCodec(probe: new PrefixedLegacyProbe());

        codec.Decode("completely-unknown-format").Should().BeNull("内建与探针均失败 → miss");
    }

    private sealed class RecordingProbe(Action onInvoke) : ILegacyFormatProbe
    {
        public bool TryDecodeLegacy(string encoded, out string? token, out long expiresInSeconds)
        {
            onInvoke();
            token = null;
            expiresInSeconds = 0;
            return false;
        }
    }

    // ── 构造守卫 ───────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NonPositiveThreshold_Should_Throw()
    {
        var act = () => new MigrationTokenStoreCodec(legacyAbsoluteMillisecondsThreshold: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Decode_ProjectedRemaining_UsesInjectedClock_Deterministically()
    {
        // 同一输入在固定时钟下两次解码结果必须逐位一致（无 flaky 面）
        var codec = CreateCodec();
        var encoded = $"{NowMs + 60_000}|tok";

        codec.Decode(encoded).Should().Be(codec.Decode(encoded));
    }
}
