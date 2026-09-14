// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 默认 IP 地址准入策略：拒绝私网 / 回环 / 链路本地地址（fail-closed）。
/// </summary>
/// <remarks>
/// 判定清单与 <see cref="UrlValidator"/> 的内网 IP 判定同源
/// （10/8、172.16/12、192.168/16、127/8、169.254/16、0.0.0.0/8、::1、fc00::/7、fe80::/10），
/// 确保连接期防线与 URL 校验期防线语义一致。
/// 本地调试需访问 localhost 时，请注册自定义 <see cref="IIpAddressPolicy"/>（放行回环网段）。
/// </remarks>
public sealed class DefaultIpAddressPolicy : IIpAddressPolicy
{
    /// <inheritdoc/>
    public bool IsAllowed(IPAddress address)
        => !UrlValidator.IsPrivateIpAddress(address);
}
