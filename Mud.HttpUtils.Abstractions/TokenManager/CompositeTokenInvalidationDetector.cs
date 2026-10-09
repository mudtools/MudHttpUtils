// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// B5（WX-01 扩展）：把多个 <see cref="ITokenInvalidationDetector"/> 组合为<b>并集</b>语义的单实例判定器
/// （任一命中即命中），供多产品线/多平台在同一 <see cref="TokenRecoveryOptions"/> 上各自贡献判定规则而互不覆盖。
/// </summary>
/// <remarks>
/// <para>
/// 两种等价用法：
/// <list type="number">
/// <item>直接向 <see cref="TokenRecoveryOptions.AdditionalTokenInvalidationDetectors"/> 逐个 <c>Add</c>
/// （框架执行器内部即为并集语义，无需本类）；</item>
/// <item>当判定器需经 <b>DI 单例</b>统一注册、或只能占用单个槽位
/// （<see cref="TokenRecoveryOptions.TokenInvalidationDetector"/>）时，用本类包装。</item>
/// </list>
/// </para>
/// <para>
/// <b>求值语义</b>：按构造时传入的顺序依次判定，短路返回首个 true。
/// <see cref="ITokenInvalidationDetector.ShouldInspect"/> 在<b>组合层</b>以并集方式预过滤
/// （任一为 true 即视为需要检查，响应体捕获由执行器按需执行一次）；进入
/// <see cref="IsTokenInvalidAsync"/> 后不再逐个重复预过滤 —— 该成员的既有契约中
/// <c>ShouldInspect</c> 的职责是"省去响应体捕获开销"，而捕获已在组合层统一完成，
/// 故被组合的判定器应保持幂等、无副作用（接口既有约定）。
/// </para>
/// <para>
/// <b>异常降级</b>：单个判定器抛出的异常（除 <see cref="OperationCanceledException"/>）一律降级为
/// 「该判定器未失效」并继续后续判定 —— 单个判定器的故障不得放大为调用失败，也不得使其它产品线的判定失效。
/// 取消异常原样向上传播（与 <c>TokenRecoveryExecutor</c> 的既有语义一致）。
/// </para>
/// <para>
/// 实现不消费响应流，<c>body</c> 语义与 <see cref="ITokenInvalidationDetector"/> 契约一致。
/// 空集合（或全部为 null）时等价于"无判定器"（恒返回 <c>false</c>，仅保留 401 语义）。
/// </para>
/// </remarks>
public sealed class CompositeTokenInvalidationDetector : ITokenInvalidationDetector
{
    private readonly ITokenInvalidationDetector[] _detectors;

    /// <summary>
    /// 初始化组合判定器。
    /// </summary>
    /// <param name="detectors">被组合的判定器。null 元素与 null 数组按"无判定器"处理。</param>
    public CompositeTokenInvalidationDetector(params ITokenInvalidationDetector[] detectors)
    {
        if (detectors is null || detectors.Length == 0)
        {
            _detectors = Array.Empty<ITokenInvalidationDetector>();
            return;
        }

        var filtered = new List<ITokenInvalidationDetector>(detectors.Length);
        foreach (var detector in detectors)
        {
            if (detector != null)
                filtered.Add(detector);
        }

        _detectors = filtered.Count == 0 ? Array.Empty<ITokenInvalidationDetector>() : filtered.ToArray();
    }

    /// <inheritdoc />
    public bool ShouldInspect(HttpRequestMessage request)
    {
        // 任一判定器需要检查即需要检查（响应体捕获由执行器统一按需执行一次）。
        foreach (var detector in _detectors)
        {
            if (detector.ShouldInspect(request))
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsTokenInvalidAsync(
        HttpResponseMessage response,
        ReadOnlyMemory<byte>? body,
        CancellationToken cancellationToken)
    {
        foreach (var detector in _detectors)
        {
            bool invalid;
            try
            {
                invalid = await detector.IsTokenInvalidAsync(response, body, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 单个判定器故障降级为"未失效"，继续后续判定（阻断"一个坏判定器使全网静默失效"）。
                invalid = false;
            }

            if (invalid)
                return true;
        }

        return false;
    }
}
