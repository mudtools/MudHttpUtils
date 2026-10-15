// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// U-3：跨进程<b>令牌刷新去重租约</b>契约 —— 多实例部署下，同一归属键（scopeKey）的并发刷新
/// 经分布式租约去重，避免各实例各自发起一次真实刷新请求（3.0.8 F-3 登记的增强项）。
/// </summary>
/// <remarks>
/// <para>
/// <b>分层定位</b>：本契约定义在抽象层（<see cref="TokenManagerBase"/> 消费），<b>不得</b>感知具体存储实现；
/// Redis 实现（<c>RedisRefreshLease</c>，<c>SET NX PX</c> + Lua compare-and-del 释放）随
/// <c>Mud.HttpUtils.Redis</c> 包提供。下游自建去重器（如飞书侧）可经本契约薄包装接入。
/// </para>
/// <para>
/// <b>语义约定</b>：
/// <list type="bullet">
/// <item><description><paramref name="leaseKey"/> 由调用方保证在刷新归属维度唯一
/// （<see cref="TokenManagerBase"/> 默认以 <c>{管理器键}:{scopeKey}</c> 构建）；实现方可追加命名空间前缀。</description></item>
/// <item><description><paramref name="leaseTime"/> 为租约 TTL —— <b>持有方崩溃时租约自动过期</b>，
/// 不产生永久死锁；实现方不得颁发无 TTL 的租约。</description></item>
/// <item><description>获取成功返回句柄；<b>释放必须校验所有权</b>（compare-and-del）——
/// 租约过期后被其他实例重新获取时，前一持有方的迟到释放<b>不得</b>误删新持有方的租约。</description></item>
/// <item><description>实现须容忍 Redis/存储不可用：获取失败应返回 <c>null</c>（fail-open 语义由
/// <see cref="TokenManagerBase"/> 承接：等待超时后放行本机刷新），<b>不得抛出异常</b>阻断令牌获取主路径。</description></item>
/// </list>
/// </para>
/// </remarks>
public interface ITokenRefreshLease
{
    /// <summary>尝试获取刷新租约。</summary>
    /// <param name="leaseKey">租约键（刷新归属维度唯一）。</param>
    /// <param name="leaseTime">租约 TTL（必须 &gt; <see cref="TimeSpan.Zero"/>；实现方据此设置自动过期）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 获取成功返回租约句柄（调用方刷新完成后必须 <see cref="ITokenRefreshLeaseHandle.ReleaseAsync"/>）；
    /// 已被其他实例持有或存储不可用时返回 <c>null</c>（fail-open，不抛异常）。
    /// </returns>
    Task<ITokenRefreshLeaseHandle?> TryAcquireAsync(string leaseKey, TimeSpan leaseTime, CancellationToken cancellationToken = default);
}

/// <summary>
/// U-3：刷新租约句柄 —— 携带所有权凭据（实现方内部持有，如 Redis 租约值 GUID），
/// 释放时按所有权校验删除（compare-and-del），防止迟到释放误删他人的租约。
/// </summary>
public interface ITokenRefreshLeaseHandle
{
    /// <summary>租约键（与获取时传入一致）。</summary>
    string LeaseKey { get; }

    /// <summary>
    /// 释放租约（按所有权校验；重复释放幂等 —— 所有权已被过期/转手后为 no-op）。
    /// 实现不得抛出异常阻断主路径（存储不可用时吞异常并按 no-op 处置）。
    /// </summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
