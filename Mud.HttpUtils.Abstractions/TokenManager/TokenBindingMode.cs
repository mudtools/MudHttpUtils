// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// F-2（TK-1）令牌归属绑定模式：声明令牌凭据的归属粒度，
/// 由 <see cref="TokenManagerBase.BindingMode"/> 消费并驱动租户绑定守卫（<c>BindTenantGuard</c>）分档。
/// </summary>
/// <remarks>
/// <para>
/// <b>单一真源（评审修订 #3）</b>：归属语义以本枚举为唯一裁决点。既有入口
/// <see cref="TokenManagerBase.EnforceTenantBinding"/> 与 <see cref="ISharedTokenManager"/>
/// 保留为 <see cref="TokenManagerBase.BindingMode"/> 默认值的<b>推导来源</b>，不再是守卫的独立开关；
/// 派生类应只覆写其中一处，禁止双向覆写造成分歧（默认推导矩阵有测试钉死）。
/// </para>
/// </remarks>
public enum TokenBindingMode
{
    /// <summary>
    /// 按应用（AppKey）绑定：管理器实例 bind-once 到单一 AppKey，跨 App 复用被拒绝。
    /// 现行 <c>BindTenantGuard</c> 行为的逐位等价档（scopeKey 不参与绑定）。
    /// </summary>
    PerApp = 0,

    /// <summary>
    /// 按归属对（AppKey + ScopeKey）绑定：管理器实例 bind-once 到单一归属对，
    /// 同对重复绑定幂等，任何一段不同的请求被拒绝。适用于「一企一份」等按归属键
    /// （如 <c>authCorpId</c>）隔离凭据的场景。新增档位，仅显式覆写才启用。
    /// </summary>
    PerScope = 1,

    /// <summary>
    /// 全租户共享凭据（如服务商 / 套件令牌）：跳过租户绑定守卫，任意 AppKey / ScopeKey 可复用同一实例。
    /// </summary>
    Shared = 2,
}
