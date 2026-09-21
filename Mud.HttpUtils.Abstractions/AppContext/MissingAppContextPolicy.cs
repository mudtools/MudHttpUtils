// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// EL-9：多租户场景下"应用上下文缺失"时的策略。
/// 统一控制四条路径的 fail-open/fail-closed 决策：
/// ① 生成代码（编译期保持 ?? throw，不依赖运行期选项）；
/// ② 缓存作用域键解析；
/// ③ 弹性 per-app 解析；
/// ④ 401 恢复守卫。
/// </summary>
public enum MissingAppContextPolicy
{
    /// <summary>
    /// 默认：fail-closed。上下文缺失时拒绝操作（抛异常或返回真实 401）。
    /// </summary>
    Reject,

    /// <summary>
    /// 逃生舱：回退到默认应用。维持旧行为但发出 Warning 日志。
    /// </summary>
    FallbackToDefaultApp,
}

/// <summary>
/// EL-9：多租户选项，控制上下文缺失时的行为。
/// 默认 <see cref="MissingContextPolicy"/> 为 <see cref="MissingAppContextPolicy.Reject"/>（fail-closed）。
/// </summary>
public sealed class MudMultiTenantOptions
{
    /// <summary>
    /// 获取或设置应用上下文缺失时的策略。
    /// 默认为 <see cref="MissingAppContextPolicy.Reject"/>（fail-closed，新行为）。
    /// 设为 <see cref="MissingAppContextPolicy.FallbackToDefaultApp"/> 可回退到旧行为（fail-open）。
    /// </summary>
    public MissingAppContextPolicy MissingContextPolicy { get; set; } = MissingAppContextPolicy.Reject;
}
