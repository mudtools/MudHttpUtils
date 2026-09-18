// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// IP 地址准入策略（M2-#8：SSRF 连接期校验）。
/// </summary>
/// <remarks>
/// <para>实现方据此判定"实际建连的 IP"是否允许访问。策略在 <b>连接期</b> 执行（而非 URL 校验期），
/// 因此不受 DNS rebinding TOCTOU 影响 —— 见 <c>SsrfSafeSocketsHttpHandler</c>（net6.0+ 专属类型）。</para>
/// <para>默认实现 <see cref="DefaultIpAddressPolicy"/> 拒绝私网/回环/链路本地地址；
/// 如需放行特定网段（如本地调试的 localhost），请自行注册 <see cref="IIpAddressPolicy"/> 替换。</para>
/// </remarks>
public interface IIpAddressPolicy
{
    /// <summary>
    /// 判定目标 IP 是否允许建连。
    /// </summary>
    /// <param name="address">实际建连的目标 IP（已解析）。</param>
    /// <returns>true 允许连接；false 拒绝连接（<c>SsrfSafeSocketsHttpHandler</c> 将抛出异常）。</returns>
    bool IsAllowed(IPAddress address);
}
