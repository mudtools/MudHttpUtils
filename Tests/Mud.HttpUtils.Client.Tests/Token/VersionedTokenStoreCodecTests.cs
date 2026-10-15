// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

using System.Globalization;

/// <summary>
/// U-2：<see cref="VersionedTokenStoreCodec"/> 的 v2 信封 —— 三态编解码矩阵、读取兼容（v1 直读）、
/// 「默认形态零影响」锚用例，以及<b>混部署负例</b>（旧版本 reader 读 v2 值 = miss）。
/// </summary>
/// <remarks>
/// 混部署负例无法在单进程内同时加载两个库版本，故以
/// <see cref="PreV2ReaderCodec"/>（3.0.7 之前的解析算法复刻，无 v2 前缀识别）作为「旧 reader」替身钉死。
/// </remarks>
public class VersionedTokenStoreCodecTests
{
    private const string ActiveValue = "v2:3600|tok-1";

    [Fact]
    public void Encode_ShouldWriteVersionedForm()
    {
        VersionedTokenStoreCodec.Instance.Encode("tok-1", 3600).Should().Be(ActiveValue);
    }

    [Fact]
    public void Encode_EmptyToken_ShouldReturnNull()
    {
        VersionedTokenStoreCodec.Instance.Encode(null, 3600).Should().BeNull("墓碑语义：不写入持久层");
        VersionedTokenStoreCodec.Instance.Encode(string.Empty, 3600).Should().BeNull();
    }

    [Theory]
    [InlineData("v2:3600|tok-1", TokenStoreEnvelopeState.Active, "tok-1", 3600)]
    [InlineData("v2:0|tok-1", TokenStoreEnvelopeState.NoTtlInformation, "tok-1", 0)]
    [InlineData("v2:-5|tok-1", TokenStoreEnvelopeState.NoTtlInformation, "tok-1", -5)]
    [InlineData(VersionedTokenStoreCodec.ExpiredValue, TokenStoreEnvelopeState.Expired, null, 0)]
    public void ThreeStateMatrix_Should_DecodeStateAndTuple(string encoded, TokenStoreEnvelopeState state, string? token, long expiresInSeconds)
    {
        VersionedTokenStoreCodec.TryDecodeEnvelope(encoded, out var actualState, out var actualToken, out var actualSeconds)
            .Should().BeTrue();
        actualState.Should().Be(state);
        actualToken.Should().Be(token);
        actualSeconds.Should().Be(expiresInSeconds);

        // 二元组投影：三态中的「显式已过期」投影为「无令牌」（miss 语义），与墓碑/缺失同口径。
        var decoded = VersionedTokenStoreCodec.Instance.Decode(encoded);
        decoded.Should().NotBeNull();
        decoded!.Value.Token.Should().Be(token);
        decoded.Value.ExpiresInSeconds.Should().Be(expiresInSeconds);
    }

    [Fact]
    public void ExpiredValue_Should_BeV2MarkerWithPlaceholderTail()
    {
        VersionedTokenStoreCodec.ExpiredValue.Should().Be("v2:expired|");
        VersionedTokenStoreCodec.IsVersionedEnvelope(VersionedTokenStoreCodec.ExpiredValue).Should().BeTrue();
    }

    [Theory]
    [InlineData("v2:")]
    [InlineData("v2:|tok-1")]
    [InlineData("v2:expired")]
    [InlineData("v2:not-a-number|tok-1")]
    [InlineData("v2:3600")]
    public void Decode_InvalidVersionedPayload_ShouldReturnNull(string encoded)
    {
        VersionedTokenStoreCodec.Instance.Decode(encoded).Should().BeNull("结构非法必须 miss（不得抛异常）");
        VersionedTokenStoreCodec.TryDecodeEnvelope(encoded, out _, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("v2:3600|tok-1", true)]
    [InlineData("v2:expired|", true)]
    [InlineData("3600|tok-1", false)]
    [InlineData("V2:3600|tok-1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsVersionedEnvelope_Should_DetectPrefix_Ordinal(string? encoded, bool expected)
    {
        VersionedTokenStoreCodec.IsVersionedEnvelope(encoded).Should().Be(expected);
    }

    [Fact]
    public void Decode_NonVersionedPayload_Should_FallBackToLegacyForm()
    {
        var decoded = VersionedTokenStoreCodec.Instance.Decode("3600|tok-1");

        decoded.Should().NotBeNull("读取兼容：换用 v2 codec 后 v1 存量值仍可读");
        decoded!.Value.Token.Should().Be("tok-1");
        decoded.Value.ExpiresInSeconds.Should().Be(3600);
    }

    // ── 读取方先升级：默认 codec 已识别 v2 前缀 ──────────────────────────

    [Fact]
    public void DefaultCodec_Should_ReadVersionedValues()
    {
        var active = DefaultTokenStoreCodec.Instance.Decode(ActiveValue);
        active.Should().NotBeNull();
        active!.Value.Token.Should().Be("tok-1");
        active.Value.ExpiresInSeconds.Should().Be(3600);

        var expired = DefaultTokenStoreCodec.Instance.Decode(VersionedTokenStoreCodec.ExpiredValue);
        expired.Should().NotBeNull("显式过期态是「有值但已过期」，解析成功但无令牌");
        expired!.Value.Token.Should().BeNull();
    }

    [Fact]
    public void DefaultCodec_Encode_Should_StayLegacyForm()
    {
        // 验收①：开关关闭 / 未启用 v2 时写入形态逐字节不变（C-2 裁决：默认形态不得漂移）。
        DefaultTokenStoreCodec.Instance.Encode("tok-1", 3600).Should().Be("3600|tok-1");
        MigrationTokenStoreCodec.Instance.Encode("tok-1", 3600).Should().Be("3600|tok-1",
            "U-1 迁移 codec 的 Encode 全权委托默认形态（单写相对秒）");
    }

    // ── 混部署负例：旧版本 reader 读 v2 值 = miss（安全方向）─────────────

    [Theory]
    [InlineData("v2:3600|tok-1")]
    [InlineData(VersionedTokenStoreCodec.ExpiredValue)]
    public void PreV2Reader_Should_MissVersionedValues(string encoded)
    {
        new PreV2ReaderCodec().Decode(encoded).Should().BeNull(
            "旧 reader 的 long.TryParse(\"v2:...\") 必然失败 ⇒ miss（走刷新路径，不会误判为有效令牌）");
    }

    // ── 与 U-1 迁移 codec 的组合 ────────────────────────────────────────

    [Fact]
    public void MigrationCodec_WithVersionedNewFormat_Should_ComposeBothCapabilities()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var codec = new MigrationTokenStoreCodec(
            newFormatCodec: VersionedTokenStoreCodec.Instance,
            utcNowProvider: () => DateTimeOffset.FromUnixTimeMilliseconds(nowMs));

        codec.Encode("tok-1", 120).Should().Be("v2:120|tok-1", "Encode 全权委托新格式 codec ⇒ 写 v2 形态");

        // 自描述值不参与旧格式量级判别：即便相对秒已达阈值量级也不得被当成绝对毫秒戳。
        var thresholdSeconds = MigrationTokenStoreCodec.DefaultLegacyAbsoluteMillisecondsThreshold;
        codec.Decode(VersionedTokenStoreCodec.Instance.Encode("tok-1", thresholdSeconds))!
            .Value.ExpiresInSeconds.Should().Be(thresholdSeconds);

        // v2 显式过期态 ⇒ 无令牌（miss），不被量级启发式误伤。
        codec.Decode(VersionedTokenStoreCodec.ExpiredValue)!.Value.Token.Should().BeNull();

        // U-1 双读能力不受 v2 分支影响：绝对毫秒戳旧值仍按 expiresAtMs - now 投影。
        var legacy = codec.Decode((nowMs + 90_000).ToString(CultureInfo.InvariantCulture) + "|old-token");
        legacy.Should().NotBeNull();
        legacy!.Value.Token.Should().Be("old-token");
        legacy.Value.ExpiresInSeconds.Should().Be(90);
    }

    /// <summary>
    /// 3.0.7 之前 <see cref="DefaultTokenStoreCodec"/> 解析算法的<b>复刻</b>（无 v2 前缀识别）——
    /// 作为「旧版本 reader」替身钉死混部署负例（单进程无法同时加载两个库版本）。
    /// </summary>
    private sealed class PreV2ReaderCodec : ITokenStoreCodec
    {
        public string? Encode(string? token, long expiresInSeconds)
            => string.IsNullOrEmpty(token)
                ? null
                : string.Concat(expiresInSeconds.ToString(CultureInfo.InvariantCulture), "|", token);

        public (string? Token, long ExpiresInSeconds)? Decode(string? encoded)
        {
            if (string.IsNullOrEmpty(encoded))
                return null;

            var index = encoded.IndexOf('|');
            if (index <= 0)
                return null;

            if (!long.TryParse(encoded.Substring(0, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresInSeconds))
                return null;

            var token = encoded.Substring(index + 1);
            return (token.Length == 0 ? null : token, expiresInSeconds);
        }
    }
}
