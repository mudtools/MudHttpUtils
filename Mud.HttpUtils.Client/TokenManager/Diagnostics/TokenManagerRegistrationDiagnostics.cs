// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace Mud.HttpUtils;

/// <summary>
/// B-1（诊断 A/B）：令牌管理器注册期诊断事实的按容器暂存（注册期无日志器，
/// 由 <c>AppManagementStartupValidator</c> 在启动期消费并输出 EventId 194/195 告警）。
/// </summary>
/// <remarks>
/// 以 <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/> 上的
/// <c>ImplementationInstance</c> 单例形式暂存，随容器隔离、不跨容器污染。
/// </remarks>
internal sealed class TokenManagerRegistrationDiagnostics
{
    /// <summary>
    /// 诊断 A：<see cref="ITokenManagerRegistry"/> 已存在、后续注册被 TryAdd 静默忽略。
    /// </summary>
    public bool RegistryRegistrationSuppressed { get; set; }

    /// <summary>
    /// 诊断 B：<c>AddMudHttpTokenManager&lt;T&gt;</c> 对同一管理器类型重复注册（描述符叠加）的类型集合。
    /// </summary>
    public ISet<Type> DuplicatedManagerTypes { get; } = new HashSet<Type>();
}
