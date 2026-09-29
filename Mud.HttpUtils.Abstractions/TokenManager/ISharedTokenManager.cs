// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 标记接口：声明令牌管理器所持凭据为「全租户共享」（凭据无租户属性），豁免租户绑定守卫。
/// </summary>
/// <remarks>
/// <para>
/// WX-02（Phase B，共享令牌管理器一等公民标记）。适用场景：一个凭据为所有租户 / 授权企业
/// 共用的令牌（如企业微信服务商 <c>provider_access_token</c>、第三方应用套件
/// <c>suite_access_token</c>——一个服务商 / 套件的凭据为所有授权企业共享）。
/// </para>
/// <para>
/// <see cref="TokenManagerBase"/> 派生类实现本接口后，<see cref="TokenManagerBase.EnforceTenantBinding"/>
/// 默认值即为 <c>false</c>（基类默认表达式判 <c>this is ISharedTokenManager</c>），
/// 全链路租户绑定守卫（<c>DefaultTokenProvider</c> 取令牌路径、恢复链路
/// <c>TryEnforceTenantBinding</c> → <c>BindTenantGuard</c>）自动豁免——多个应用上下文
/// （AppKey）可共享同一管理器实例，不再抛 <see cref="InvalidOperationException"/>。
/// </para>
/// <para>
/// 优先级：派生类显式覆写 <c>EnforceTenantBinding</c> 时以覆写为准（例如实现本接口后仍可
/// 显式覆写返回 <c>true</c> 收紧为单租户绑定）。自证责任随实现转移：实现即声明凭据无租户属性。
/// </para>
/// </remarks>
public interface ISharedTokenManager
{
}
