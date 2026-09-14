// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// <see cref="ResilienceOptions"/> 的后置配置器（CFG-09）。
/// </summary>
/// <remarks>
/// <para>
/// 当 <see cref="RetryOptions.AllowNonIdempotentRetry"/> 为 <c>true</c> 且
/// <see cref="RetryOptions.RetryableHttpMethods"/> 偏离默认集合（即被显式收窄/改写）时记录警告，
/// 避免「以为只放开部分方法，实际所有 HTTP 方法都可重试」的语义漂移
/// （<c>RetryGuard</c> 在 <c>AllowNonIdempotentRetry=true</c> 时提前返回，方法集合被忽略）。
/// </para>
/// <para>不阻断启动：该组合可能是用户有意为之。</para>
/// </remarks>
internal sealed class ResilienceOptionsPostConfigure : IPostConfigureOptions<ResilienceOptions>
{
    /// <summary>与 <see cref="RetryOptions.RetryableHttpMethods"/> 默认值一致的基线。</summary>
    private static readonly HashSet<string> DefaultMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "PUT", "DELETE", "TRACE" };

    private readonly ILogger<ResilienceOptionsPostConfigure> _logger;

    public ResilienceOptionsPostConfigure(ILogger<ResilienceOptionsPostConfigure>? logger = null)
        => _logger = logger ?? NullLogger<ResilienceOptionsPostConfigure>.Instance;

    /// <inheritdoc />
    public void PostConfigure(string? name, ResilienceOptions options)
    {
        if (options?.Retry is null)
            return;

        if (!options.Retry.AllowNonIdempotentRetry)
            return;

        var methods = options.Retry.RetryableHttpMethods;
        if (methods is null)
            return;

        if (!methods.SetEquals(DefaultMethods))
        {
            MudHttpClientLog.RetryableHttpMethodsIgnored(_logger);
        }
    }
}
