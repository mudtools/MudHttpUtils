// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 应用管理器"可用性"判定的<b>单一事实源</b>（<b>AM2-06 / AM2-16</b>）。
/// </summary>
/// <remarks>
/// <para>
/// 启动校验（<c>AppManagementStartupValidator</c>）与健康检查（<c>AppManagementHealthCheck</c>）
/// 此前各自写了一份"有没有应用"的判定，且都只看 <see cref="IAppManager{TAppContext}.GetAllApps"/>
/// （<b>物化项</b>）—— 各自口径漂移的风险与重复实现同时存在。此处收敛为一处。
/// </para>
/// <para>
/// <b>为什么不能判定"仅懒加载声明算可用"</b>：<see cref="IAppManager{TAppContext}"/> <b>没有</b>
/// "枚举懒加载声明"的成员，因此"lazy-only 且无默认键"与"完全空的管理器"在接口层面<b>不可区分</b>。
/// 二者在行为上也确实等价：默认键为空时首个 <c>GetDefaultApp()</c> 只会回退到已物化项，
/// 没有就抛异常 ⇒ 请求必然失败。本判定因此把这种情况一律视为"不可用"（fail-closed）。
/// 懒加载项只有在<b>同时被设为默认应用</b>（<c>RegisterLazy(..., isDefault: true)</c>，此时
/// <c>DefaultAppKey</c> 非空且 <c>HasApp</c> 为真）时才计为可用。
/// </para>
/// </remarks>
internal static class AppManagerProbe
{
    /// <summary>
    /// 判断管理器是否存在至少一个"可用"应用。
    /// </summary>
    /// <param name="manager">应用管理器；为 <c>null</c> 时返回 <c>false</c>。</param>
    /// <returns>默认键可解析（含懒加载声明）或有任一已物化应用时返回 <c>true</c>。</returns>
    internal static bool HasAnyUsableApp(IAppManager<IMudAppContext>? manager)
    {
        if (manager is null)
            return false;

        var defaultKey = manager.DefaultAppKey;
        if (!string.IsNullOrEmpty(defaultKey))
        {
            // HasApp 认"物化 ∪ 声明"，且不触发物化 —— 正是启动期/健康检查需要的无副作用判定。
            return manager.HasApp(defaultKey!);
        }

        // 无默认键：只认已物化项（GetAllApps 不物化，与 Lazy 文档语义一致）。
        return manager.GetAllApps().Any();
    }
}
