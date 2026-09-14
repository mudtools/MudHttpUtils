// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// M2-#12：重试幂等性判定。供全局路径（<see cref="ResilientHttpClient"/>）与
/// 方法级弹性路径（<see cref="ResiliencePolicyResolver"/>）共用，确保两条路径的重试防护语义一致。
/// </summary>
internal static class RetryGuard
{
    /// <summary>
    /// 默认幂等方法白名单（与 <c>RetryOptions.RetryableHttpMethods</c> 默认值一致）。
    /// options 为 null（未配置弹性选项）时按此判定，保持幂等方法默认可重试。
    /// </summary>
    private static readonly HashSet<string> DefaultRetryableMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS", "PUT", "DELETE", "TRACE",
    };

    /// <summary>
    /// 判断请求（按其 HTTP 方法）是否允许重试。
    /// 判定顺序：全局开关 → 请求属性标记（<c>[Retry(AllowNonIdempotent)]</c> 生成代码写入）→ 可重试方法集合。
    /// </summary>
    public static bool IsRetryAllowedForMethod(
        HttpRequestMessage request,
        ResilienceOptions? options)
    {
#if NETSTANDARD2_0
        if (request.Properties.TryGetValue(
                HttpExecutionConstants.AllowNonIdempotentRetryPropertyKey, out var v) && v is true)
            return true;
#else
        if (request.Options.TryGetValue(new HttpRequestOptionsKey<bool>(
                HttpExecutionConstants.AllowNonIdempotentRetryPropertyKey), out var v) && v)
            return true;
#endif

        return IsRetryAllowedForMethod(request.Method.Method, options);
    }

    /// <summary>
    /// 便捷方法路径判定（无 <see cref="HttpRequestMessage"/> —— 便捷方法闭包内每次重建请求）。
    /// 判定与请求版一致：全局开关（<see cref="RetryOptions.AllowNonIdempotentRetry"/>）→ 可重试方法集合。
    /// </summary>
    public static bool IsRetryAllowedForMethod(string httpMethod, ResilienceOptions? options)
    {
        if (options?.Retry?.AllowNonIdempotentRetry == true)
            return true;

        var retryableMethods = options?.Retry?.RetryableHttpMethods ?? DefaultRetryableMethods;
        return retryableMethods.Contains(httpMethod);
    }
}

/// <summary>
/// M2-#10：Polly 第三方异常归一化。把 <see cref="TimeoutRejectedException"/> / <see cref="BrokenCircuitException"/>
/// 包装为框架自身的 <see cref="ApiRequestException"/>，避免 Polly 类型泄漏到公共 API 面。
/// </summary>
/// <remarks>
/// 包装点必须在 Polly 策略边界<b>之外</b>（<c>policy.ExecuteAsync</c> 返回之后）——
/// 若在策略内部包装，异常会被重试判定（<c>Or&lt;TimeoutRejectedException&gt;()</c>）吞掉，破坏重试语义。
/// 两条路径（ResilientHttpClient 出口与 ResiliencePolicyResolver 出口）共用本归一化。
/// </remarks>
internal static class PollyExceptionNormalizer
{
    /// <summary>
    /// 尝试把 Polly 内部异常归一化为 <see cref="ApiRequestException"/>；无需包装时返回 null。
    /// </summary>
    public static ApiRequestException? TryNormalize(Exception ex, string? requestUri)
    {
        if (ex is TimeoutRejectedException)
        {
            return new ApiRequestException(
                $"请求超时: {requestUri}", ex, isTimeout: true, requestUri: requestUri);
        }

        if (ex is BrokenCircuitException)
        {
            return new ApiRequestException(
                $"熔断器已打开: {requestUri}", ex, isCircuitOpen: true, requestUri: requestUri);
        }

        return null;
    }

    /// <summary>
    /// 尝试把 Polly 内部异常归一化为 <see cref="ApiRequestException"/>；无需包装时返回 null。
    /// </summary>
    /// <param name="ex">待归一化的异常。</param>
    /// <param name="request">触发请求（用于异常的 RequestUri 语义）。</param>
    /// <returns>包装后的异常；输入不需要包装时返回 null（调用方原样重抛）。</returns>
    public static ApiRequestException? TryNormalize(Exception ex, HttpRequestMessage? request)
        => TryNormalize(ex, request?.RequestUri?.ToString());

    /// <summary>
    /// 在 Polly 策略边界外执行委托并归一化抛出的 Polly 异常。
    /// </summary>
    public static async Task<TResult> ExecuteAsync<TResult>(
        HttpRequestMessage request,
        Func<Task<TResult>> execute)
    {
        try
        {
            return await execute().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutRejectedException or BrokenCircuitException)
        {
            var normalized = TryNormalize(ex, request?.RequestUri?.ToString());
            if (normalized != null)
                throw normalized;
            throw;
        }
    }

    /// <summary>
    /// 在 Polly 策略边界外执行委托并归一化抛出的 Polly 异常（便捷方法路径 —— 无 <see cref="HttpRequestMessage"/>，仅有 URI）。
    /// </summary>
    public static async Task<TResult> ExecuteAsync<TResult>(
        string? requestUri,
        Func<Task<TResult>> execute)
    {
        try
        {
            return await execute().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutRejectedException or BrokenCircuitException)
        {
            var normalized = TryNormalize(ex, requestUri);
            if (normalized != null)
                throw normalized;
            throw;
        }
    }
}
