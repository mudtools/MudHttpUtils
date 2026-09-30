// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// R-P1-04：可安全重放的 HTTP 方法白名单（幂等语义）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为何是白名单</b>：401 恢复会对请求做<b>无损重放</b>。对非幂等方法（POST/PATCH）重放可能造成
/// 重复下单、重复扣款、重复提交等业务副作用 —— 故默认只放行"重复执行不改变服务端状态"的方法。
/// </para>
/// <para>
/// <b>与 Resilience 侧的关系</b>：<c>Resilience/RetryGuard.cs</c> 存在同一份白名单。评审已确认
/// Client↔Resilience 双向引用会引入跨程序集关注点、收益低于回归成本，故两份白名单在 P1 阶段
/// <b>刻意并存</b>（行为必须一致：GET/HEAD/OPTIONS/TRACE/PUT/DELETE 放行，POST/PATCH/CONNECT 拒绝）。
/// 后续如需合一，由 Resilience 侧改为委托调用本类型（列为 P3 可选项）。
/// </para>
/// <para>
/// <b>逃生门</b>：① 全局 <see cref="TokenRecoveryOptions.AllowNonIdempotentRecovery"/>；
/// ② 契约级 <see cref="TokenRecoveryContext.IsRetryAllowedExplicitly"/>（生成器按方法/接口特性写入）。
/// </para>
/// </remarks>
internal static class RetryableMethodPolicy
{
    /// <summary>可安全重放（幂等）的方法集合。</summary>
    private static readonly HashSet<HttpMethod> SafeMethods = new()
    {
        HttpMethod.Get,
        HttpMethod.Head,
        HttpMethod.Options,
        HttpMethod.Trace,
        HttpMethod.Put,
        HttpMethod.Delete,
    };

    /// <summary>
    /// 判断指定方法是否可安全重放。
    /// </summary>
    /// <param name="method">HTTP 方法（<see cref="HttpMethod"/> 按方法名做值相等比较，自定义实例同样适用）。</param>
    /// <returns>可安全重放返回 <c>true</c>；否则 <c>false</c>。</returns>
    public static bool IsRetryable(HttpMethod? method)
        => method != null && SafeMethods.Contains(method);
}
