// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌<b>存储值编解码</b>约定 —— 把"过期戳 + 令牌"这一事实契约从各下游的私有发明
/// 上收为库内可替换的正式抽象。
/// </summary>
/// <remarks>
/// <para>
/// 动因：桥接持久化层（<c>TokenStoreBackedTokenCache&lt;T&gt;</c>）时，各下游都独立发明了
/// <c>{expireMs}|{token}</c> 形态的编解码（飞书 <c>TokenStoreHelper</c>、微信 <c>WechatTokenBridgeCodec</c>），
/// 属典型的"应上收而未上收"。本接口把它固化为可替换的正式扩展点，并提供
/// <see cref="DefaultTokenStoreCodec"/> 作为与既有形态等价的内建实现。
/// </para>
/// <para>
/// <b>实现契约</b>：<see cref="Encode"/> 对空令牌应返回 <c>null</c>（墓碑语义：不写入持久层）；
/// <see cref="Decode"/> 对 <c>null</c>/空白/非法格式应返回 <c>null</c>（不得抛异常）。
/// </para>
/// </remarks>
public interface ITokenStoreCodec
{
    /// <summary>
    /// 把"令牌 + 剩余有效秒数"编码为单个存储字符串。
    /// </summary>
    /// <param name="token">令牌明文；为 <c>null</c>/空时返回 <c>null</c>（墓碑语义）。</param>
    /// <param name="expiresInSeconds">剩余有效秒数（&lt;= 0 表示无 TTL 信息）。</param>
    /// <returns>存储字符串；令牌为空时返回 <c>null</c>。</returns>
    string? Encode(string? token, long expiresInSeconds);

    /// <summary>
    /// 解析存储字符串。
    /// </summary>
    /// <param name="encoded">存储字符串；为 <c>null</c>/空白/非法格式时返回 <c>null</c>。</param>
    /// <returns>解析出的（令牌, 剩余有效秒数）；解析失败返回 <c>null</c>。</returns>
    (string? Token, long ExpiresInSeconds)? Decode(string? encoded);
}
