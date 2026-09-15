// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 白名单整体替换的审计事件。
/// </summary>
/// <param name="Previous">变更前的域名集合快照。</param>
/// <param name="Next">变更后的域名集合快照。</param>
/// <param name="Added">新增的域名集合。</param>
/// <param name="Removed">移除的域名集合。</param>
public readonly struct AllowedDomainsChangedEvent(
    System.Collections.Generic.IReadOnlyCollection<string> previous,
    System.Collections.Generic.IReadOnlyCollection<string> next,
    System.Collections.Generic.IReadOnlyCollection<string> added,
    System.Collections.Generic.IReadOnlyCollection<string> removed)
{
    /// <summary>变更前的域名集合快照。</summary>
    public System.Collections.Generic.IReadOnlyCollection<string> Previous { get; } = previous;

    /// <summary>变更后的域名集合快照。</summary>
    public System.Collections.Generic.IReadOnlyCollection<string> Next { get; } = next;

    /// <summary>新增的域名集合。</summary>
    public System.Collections.Generic.IReadOnlyCollection<string> Added { get; } = added;

    /// <summary>移除的域名集合。</summary>
    public System.Collections.Generic.IReadOnlyCollection<string> Removed { get; } = removed;
}

/// <summary>
/// 白名单整体替换审计日志（C4-P1）。
/// </summary>
/// <remarks>
/// <para>
/// 白名单是全局安全边界，任何整体替换都必须可追溯（谁、从什么变成什么）。
/// 本类持有 <see cref="Sink"/> 委托，由 DI 注册时挂接 <c>ILogger</c> 或 SIEM 输出。
/// </para>
/// <para>
/// 默认 <see cref="Sink"/> 为 null，静默不记录。宿主应在启动时挂接：
/// <code>
/// AllowedDomainAuditLog.Sink = e => logger.LogInformation("白名单变更：+{Added}, -{Removed}", e.Added, e.Removed);
/// </code>
/// </para>
/// </remarks>
internal static class AllowedDomainAuditLog
{
    /// <summary>
    /// 审计输出委托。默认 null（静默）。由宿主在启动时挂接。
    /// </summary>
    internal static Action<AllowedDomainsChangedEvent>? Sink { get; set; }

    /// <summary>
    /// 记录白名单整体替换事件，计算 added/removed 差异集合并输出到 <see cref="Sink"/>。
    /// </summary>
    /// <param name="previous">变更前的域名集合。</param>
    /// <param name="next">变更后的域名集合。</param>
    internal static void Record(
        System.Collections.Generic.IReadOnlyCollection<string> previous,
        System.Collections.Generic.IReadOnlyCollection<string> next)
    {
        if (Sink is null)
            return;

        var added = new System.Collections.Generic.HashSet<string>(next, System.StringComparer.OrdinalIgnoreCase);
        foreach (var p in previous)
            added.Remove(p);

        var removed = new System.Collections.Generic.HashSet<string>(previous, System.StringComparer.OrdinalIgnoreCase);
        foreach (var n in next)
            removed.Remove(n);

        Sink(new AllowedDomainsChangedEvent(previous, next, added, removed));
    }
}
