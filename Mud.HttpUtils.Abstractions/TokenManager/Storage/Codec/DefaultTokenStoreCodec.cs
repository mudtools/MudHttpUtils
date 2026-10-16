// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 内建默认编解码器：<c>{expiresInSeconds}|{token}</c>（与下游既有的"过期戳 + 令牌"形态等价，
/// 但格式由库定义并文档化，可安全替换）。
/// </summary>
/// <remarks>
/// <para>
/// 选择"剩余秒数"而非"绝对过期戳"：绝对戳在持久层滞留后会被误判为有效（时钟回拨/长期留存），
/// 而相对秒数由写入方按当前时钟推导，与 <see cref="TokenStoreValue.ExpiresInSeconds"/> 语义一致。
/// </para>
/// <para>
/// 读取面扩展：<see cref="Decode"/> 识别 <see cref="VersionedTokenStoreCodec.VersionPrefix"/>
/// 前缀（v2 信封）——即读取方升级本库即可读 v2 值，<b>无需</b>改配置；<see cref="Encode"/> 输出形态不变
/// （写入侧是否改 v2 由显式开关决定）。
/// </para>
/// </remarks>
public sealed class DefaultTokenStoreCodec : ITokenStoreCodec
{
    /// <summary>分隔符（单字符，不得出现在令牌中；令牌本身不含 <c>|</c>）。</summary>
    public const char Separator = '|';

    /// <summary>共享默认实例（无状态、线程安全）。</summary>
    public static DefaultTokenStoreCodec Instance { get; } = new();

    /// <inheritdoc />
    public string? Encode(string? token, long expiresInSeconds)
    {
        // 墓碑语义：令牌为空 ⇒ 不写入持久层（桥接器据此跳过写穿）。
        if (string.IsNullOrEmpty(token))
            return null;

        return string.Concat(expiresInSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), Separator.ToString(), token);
    }

    /// <inheritdoc />
    /// <remarks>
    /// v2 信封前缀（<see cref="VersionedTokenStoreCodec.VersionPrefix"/>）走 v2 解析分支
    /// （<see cref="VersionedTokenStoreCodec.DecodeEnvelopeToTuple"/>）；非前缀形态按本 codec 的 v1 规则解析。
    /// 旧版本（无前缀识别的）reader 读到 v2 值会因前导段非数值而解析失败返回 <c>null</c>（miss，安全方向）。
    /// </remarks>
    public (string? Token, long ExpiresInSeconds)? Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;

        if (VersionedTokenStoreCodec.IsVersionedEnvelope(encoded))
            return VersionedTokenStoreCodec.DecodeEnvelopeToTuple(encoded);

        var index = encoded.IndexOf(Separator);
        if (index <= 0)
            return null;   // 无分隔符 / 分隔符在首位 ⇒ 非法格式（不抛异常）

        var secondsText = encoded.Substring(0, index);
        if (!long.TryParse(secondsText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var expiresInSeconds))
            return null;

        var token = encoded.Substring(index + 1);
        return (token.Length == 0 ? null : token, expiresInSeconds);
    }
}
