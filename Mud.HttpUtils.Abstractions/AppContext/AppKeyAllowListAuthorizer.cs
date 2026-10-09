// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// G10：按 <b>appKey 白名单</b>授权应用切换的 <see cref="IAppAccessAuthorizer"/> 内建实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>MT-02（BC-18）</b>把"未注册 <see cref="IAppAccessAuthorizer"/> 即静默放行"改为<b>默认拒绝</b>后，
/// 多租户宿主必须显式提供授权器。此前库内<b>没有任何</b>内建实现，各下游 SDK 只能各自自建
/// （放行器或白名单器），本类型补齐这一"应有而未有"的缺口。
/// </para>
/// <para>
/// <b>安全边界</b>：本实现只做"appKey 是否在名单内"的静态判定，<b>不</b>感知当前调用主体。
/// 适用于"appKey 来自服务端可信配置（非请求参数）"的场景；
/// 若 appKey 可能来自请求参数，必须实现与当前调用主体（租户 / 用户）绑定的授权器 ——
/// 名单式授权无法阻止"A 租户传入 B 租户的 appKey"。
/// </para>
/// <para>
/// 未提供的形态：本库<b>刻意不提供</b>"无条件放行"实现（会重新引入 BC-18 修复前的越权面）。
/// 单应用 / 完全受信场景若确需放行，请在宿主内自行实现一个语义明确、命名体现风险的授权器。
/// </para>
/// </remarks>
public sealed class AppKeyAllowListAuthorizer : IAppAccessAuthorizer
{
    private readonly HashSet<string>? _allowedAppKeys;
    private readonly Func<string, bool>? _predicate;

    /// <summary>
    /// 以 appKey 集合初始化白名单。
    /// </summary>
    /// <param name="allowedAppKeys">允许切换到的 appKey 集合；为 <c>null</c> 时等价于空名单（一律拒绝）。</param>
    /// <remarks>
    /// appKey 大小写<b>区分</b>（与 <see cref="IAppManager{TAppContext}"/> 的既有契约一致）；
    /// 空项与 <c>null</c> 项被忽略；重复项去重。
    /// </remarks>
    public AppKeyAllowListAuthorizer(IEnumerable<string>? allowedAppKeys)
    {
        if (allowedAppKeys is null)
        {
            _allowedAppKeys = new HashSet<string>(StringComparer.Ordinal);
            return;
        }

        _allowedAppKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var appKey in allowedAppKeys)
        {
            if (!string.IsNullOrEmpty(appKey))
                _allowedAppKeys.Add(appKey);
        }
    }

    private AppKeyAllowListAuthorizer(Func<string, bool> predicate)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    /// <summary>
    /// 以自定义判定委托创建授权器（用于"名单由配置/租户服务动态提供"的场景）。
    /// </summary>
    /// <param name="predicate">判定委托：给定 appKey 返回是否允许切换。不得为 <c>null</c>。</param>
    /// <returns>授权器实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> 为 null。</exception>
    /// <remarks>命名为 <c>FromPredicate</c> 而非构造重载：避免 <c>new AppKeyAllowListAuthorizer(null)</c>
    /// 在两个引用类型参数间产生 CS0121 二义性（本仓既有同类约定，见 <see cref="SensitiveUrlKeys.RegisterAll"/>）。</remarks>
    public static AppKeyAllowListAuthorizer FromPredicate(Func<string, bool> predicate)
        => new(predicate);

    /// <inheritdoc />
    /// <remarks>appKey 为 <c>null</c>/空白时一律拒绝（fail-closed）。</remarks>
    public bool CanSwitchTo(string appKey)
    {
        if (string.IsNullOrWhiteSpace(appKey))
            return false;

        return _predicate is not null
            ? _predicate(appKey)
            : _allowedAppKeys!.Contains(appKey);
    }
}
