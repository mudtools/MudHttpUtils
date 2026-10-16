// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 下游<b>旧存储值格式</b>的解码探针钩子 —— 供存量格式既非「绝对毫秒戳」也非「相对秒」的下游
/// （如自带前缀的自定义形态）注入自己的判别逻辑。
/// </summary>
/// <remarks>
/// <para>
/// <b>调用时机</b>：<see cref="MigrationTokenStoreCodec"/> 的内建判别（绝对毫秒戳 / 相对秒）对给定值
/// 解析失败时才回调本探针（内建优先，钩子兜底）；探针返回 <c>false</c> 时整体判为解析失败（返回 null，不抛异常）。
/// </para>
/// <para>
/// <b>契约</b>：与 <see cref="ITokenStoreCodec.Decode"/> 同口径 —— 实现对非法格式返回 <c>false</c>
/// 且不得抛异常；解析出的 <paramref name="expiresInSeconds"/> 语义为<b>剩余有效秒数</b>
/// （≤ 0 表示无 TTL 信息 / 已过期，由调用方按既有约定处置）。
/// </para>
/// </remarks>
public interface ILegacyFormatProbe
{
    /// <summary>尝试按下游旧格式解析存储值。</summary>
    /// <param name="encoded">存储字符串（非空，由调用方保证）。</param>
    /// <param name="token">解析出的令牌明文；返回 <c>false</c> 时无意义。</param>
    /// <param name="expiresInSeconds">解析出的剩余有效秒数；返回 <c>false</c> 时无意义。</param>
    /// <returns>解析成功返回 <c>true</c>；非本格式 / 非法输入返回 <c>false</c>（不抛异常）。</returns>
    bool TryDecodeLegacy(string encoded, out string? token, out long expiresInSeconds);
}
