// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

// -------------------------------------------------------------------------------------------------
//  租户/归属绑定：bind-once 守卫与归属模式（Shared / PerApp / PerScope）
//  TokenManagerBase partial: shares all private state with the main file.
// -------------------------------------------------------------------------------------------------
public abstract partial class TokenManagerBase
{
    /// <summary>
    /// 租户绑定键（bind-once）。null = 尚未绑定。
    /// 绑定键 = <c>IMudAppContext.AppKey</c>（多租户在本框架的投影即多 App）。
    /// <c>PerScope</c> 档的绑定键为「AppKey + ScopeKey」归属对，故绑定记录携带两段；
    /// <c>PerApp</c> 档只比对 AppKey 段（行为与既有实现逐位一致）。
    /// </summary>
    private TenantBindingRecord? _tenantBinding;

    /// <summary>
    /// 租户绑定记录（不可变，单字段 CAS 保证 bind-once 的原子性）。
    /// </summary>
    private sealed class TenantBindingRecord
    {
        public TenantBindingRecord(string appKey, string? scopeKey)
        {
            AppKey = appKey;
            ScopeKey = scopeKey;
        }

        public string AppKey { get; }
        public string? ScopeKey { get; }
    }



    /// <summary>
    /// 是否启用租户绑定守卫（防止单实例跨租户共享导致凭据错配）。
    /// 默认 true；派生类实现 <see cref="ISharedTokenManager"/>（全租户共享凭据标记，
    /// 如服务商 / 套件令牌）时默认 false。其余特殊场景可覆写为 false（需自证凭据无租户属性）；
    /// 显式覆写优先于接口标记。
    /// </summary>
    /// <remarks>
    /// 后本属性为 <see cref="BindingMode"/> 默认值的<b>推导来源</b>（不再直接驱动守卫）：
    /// 未覆写 <see cref="BindingMode"/> 时，false ⇒ <see cref="TokenBindingMode.Shared"/>、
    /// true ⇒ <see cref="TokenBindingMode.PerApp"/>，守卫行为与此前逐位一致。
    /// 归属语义请以覆写 <see cref="BindingMode"/> 为准（单一真源），勿双向覆写。
    /// </remarks>
    protected virtual bool EnforceTenantBinding => this is not ISharedTokenManager;

    /// <summary>
    /// 令牌归属绑定模式（归属语义的<b>单一真源</b>），驱动租户绑定守卫分档：
    /// <see cref="TokenBindingMode.Shared"/> 跳过绑定；<see cref="TokenBindingMode.PerScope"/>
    /// 以 AppKey + ScopeKey 归属对绑定；<see cref="TokenBindingMode.PerApp"/> 维持既有按 AppKey 绑定行为。
    /// </summary>
    /// <remarks>
    /// <para><b>默认推导（评审修订 #3）</b>：<see cref="EnforceTenantBinding"/> 为 false（含实现
    /// <see cref="ISharedTokenManager"/> 的默认情形）⇒ <see cref="TokenBindingMode.Shared"/>；
    /// 否则 ⇒ <see cref="TokenBindingMode.PerApp"/>。<see cref="ISharedTokenManager"/> /
    /// <see cref="EnforceTenantBinding"/> 保留为推导来源；显式覆写 <see cref="EnforceTenantBinding"/>
    /// 优先于接口标记（既有语义），故「实现标记但覆写 EnforceTenantBinding=true」推导为
    /// <see cref="TokenBindingMode.PerApp"/>。</para>
    /// <para>覆写本属性即接管归属语义（旧入口降为纯推导来源，守卫只读本属性）；
    /// 请勿同时覆写本属性与 <see cref="EnforceTenantBinding"/> 造成分歧。</para>
    /// </remarks>
    protected virtual TokenBindingMode BindingMode =>
        EnforceTenantBinding ? TokenBindingMode.PerApp : TokenBindingMode.Shared;

    /// <summary>
    /// 本管理器声明的令牌类型标识（与 <c>IMudAppContext.GetTokenManager(tokenType)</c>
    /// 注册键同口径）。默认 null = 未声明：此时 <see cref="TokenScopeKey"/> 定位符形态跳过
    /// tokenType 段的一致性校验。该段<b>绝不参与缓存键</b>，仅供定位符校验。
    /// </summary>
    protected virtual string? TokenType => null;

    /// <summary>
    /// F4：当前实例是否启用租户绑定守卫（即 <see cref="BindingMode"/> 非
    /// <see cref="TokenBindingMode.Shared"/>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 供消费方/测试<b>无需反射</b>即可判定"该管理器是否按归属键隔离"。语义与守卫<b>实际行为</b>
    /// 同口径（不是独立的第二真相源）：<see cref="BindingMode"/> 未被覆写时与
    /// <see cref="EnforceTenantBinding"/>（protected virtual）取值完全一致。
    /// </para>
    /// <para>
    /// 注意：<see cref="BindingMode"/> 为 <c>virtual</c> 且可在实例化后被覆写判定，
    /// 本属性每次读取都会重新求值（非缓存），保持与守卫调用点同口径。
    /// </para>
    /// </remarks>
    public bool IsTenantBindingEnforced => BindingMode != TokenBindingMode.Shared;

    /// <summary>
    /// SR-H5（P2.1，D6）bind-once 租户绑定守卫：首个租户键绑定后，不同租户键的请求被拒绝。
    /// 同键重复绑定幂等通过。internal：仅框架调用点（<c>DefaultTokenProvider</c>）触发，不进公共 API。
    /// 按 <see cref="BindingMode"/> 分档（本重载无 scopeKey，PerScope 档以「AppKey + 空归属键」绑定）。
    /// </summary>
    /// <param name="tenantKey">租户键（AppKey）。</param>
    /// <exception cref="InvalidOperationException">已绑定其他租户且守卫启用时抛出。</exception>
    internal void BindTenantGuard(string tenantKey) => BindTenantGuard(tenantKey, scopeKey: null);

    /// <summary>
    /// SR-H5 + F-2：bind-once 租户绑定守卫（带归属键）。按 <see cref="BindingMode"/> 分档：
    /// <see cref="TokenBindingMode.Shared"/> 跳过绑定；<see cref="TokenBindingMode.PerScope"/>
    /// 以 AppKey + ScopeKey 归属对绑定（同对幂等、任一段不同即拒绝）；
    /// <see cref="TokenBindingMode.PerApp"/> 只以 AppKey 绑定（与既有行为逐位一致，scopeKey 不参与）。
    /// </summary>
    /// <param name="tenantKey">租户键（AppKey）。</param>
    /// <param name="scopeKey">归属键（如 authCorpId），仅 PerScope 档参与绑定。</param>
    /// <exception cref="InvalidOperationException">已绑定其他归属且守卫启用时抛出。</exception>
    internal void BindTenantGuard(string tenantKey, string? scopeKey)
    {
        switch (BindingMode)
        {
            case TokenBindingMode.Shared:
                // 共享凭据（服务商 / 套件令牌）：无归属属性，跳过绑定。
                return;
            case TokenBindingMode.PerScope:
                BindTenantGuardCore(tenantKey, scopeKey ?? string.Empty, bindScope: true);
                return;
            default: // TokenBindingMode.PerApp：维持现行行为（scopeKey 不参与绑定）
                BindTenantGuardCore(tenantKey, scopeKey: null, bindScope: false);
                return;
        }
    }

    /// <summary>
    /// bind-once 绑定核心：单字段 CAS（<see cref="TenantBindingRecord"/> 不可变记录）保证原子性。
    /// </summary>
    private void BindTenantGuardCore(string tenantKey, string? scopeKey, bool bindScope)
    {
        if (string.IsNullOrEmpty(tenantKey))
            return;

        var incoming = new TenantBindingRecord(tenantKey, bindScope ? scopeKey : null);
        var existing = Interlocked.CompareExchange(ref _tenantBinding, incoming, null);
        if (existing == null)
            return;

        var tenantConflict = !string.Equals(existing.AppKey, tenantKey, StringComparison.Ordinal);
        var scopeConflict = bindScope
            && !string.Equals(existing.ScopeKey ?? string.Empty, scopeKey ?? string.Empty, StringComparison.Ordinal);
        if (!tenantConflict && !scopeConflict)
            return;

        // 租户键经 AppKeyValidator.ToSafeText 文本化（截断 128 + 控制字符过滤）——
        // 与 DefaultAppManager 同口径。tenantKey 来自用户实现的 IMudAppContext.AppKey（框架不强制
        // 再校验），原文插值进异常消息会构成日志注入面（与生成侧「静态消息」纪律对齐）。
        if (!bindScope)
        {
            throw new InvalidOperationException(
                $"令牌管理器（{MetricsKey}）已绑定租户 '{AppKeyValidator.ToSafeText(existing.AppKey)}'，" +
                $"不能用于租户 '{AppKeyValidator.ToSafeText(tenantKey)}' 的请求。" +
                "跨租户复用同一管理器实例会导致令牌/凭据错配；若确属共享凭据设计，请覆写 EnforceTenantBinding 返回 false。");
        }

        throw new InvalidOperationException(
            $"令牌管理器（{MetricsKey}）已绑定归属 '{AppKeyValidator.ToSafeText(existing.AppKey)}'/'{AppKeyValidator.ToSafeText(existing.ScopeKey)}'，" +
            $"不能用于归属 '{AppKeyValidator.ToSafeText(tenantKey)}'/'{AppKeyValidator.ToSafeText(scopeKey)}' 的请求。" +
            "PerScope 档以 AppKey + ScopeKey 归属对 bind-once 绑定，跨归属复用同一管理器实例会导致令牌/凭据错配；" +
            "若确属共享凭据设计，请覆写 BindingMode 返回 Shared。");
    }

    /// <summary>
    /// L-1：当前已绑定的租户键（null = 尚未绑定）。供恢复链路在守卫拒绝时输出结构化告警。
    /// PerScope 档绑定记录含归属键，此处仍只回传 AppKey 段（告警口径不变）。
    /// </summary>
    internal string? BoundTenant => Volatile.Read(ref _tenantBinding)?.AppKey;

    /// <summary>
    /// 是否启动租户层维护 Timer（过期清理 300s / 锁清理 600s）。默认 true。
    /// 用户令牌管理器覆写 false 以跳过其永不使用的租户层 Timer 资源。
    /// <para>注意：仅跳过 Timer 分配；<c>_tokenCache</c> / <c>_keyedLockTable</c> 仍保留分配（避免基类
    /// 公共路径解引用空字段的 NRE 面，见 §0.3-V5 评审修订）。</para>
    /// </summary>
    protected virtual bool SupportsTenantMaintenance => true;
}
