// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="MudHttpClientApplicationOptions"/> 的后置配置器（CFG-02）。
/// </summary>
/// <remarks>
/// 对「未配置 <see cref="MudHttpClientOptions.BaseAddress"/> 因而被跳过注册」的客户端记录警告，
/// 使「已配置但不生效」不再静默。仅在 <c>AddMudHttpClientsFromConfiguration</c> 路径注册。
/// </remarks>
internal sealed class MudHttpClientApplicationOptionsPostConfigure
    : IPostConfigureOptions<MudHttpClientApplicationOptions>
{
    private readonly ILogger<MudHttpClientApplicationOptionsPostConfigure> _logger;
    private readonly bool _explicitResponseCacheRegistered;

    public MudHttpClientApplicationOptionsPostConfigure(
        ILogger<MudHttpClientApplicationOptionsPostConfigure>? logger = null,
        ExplicitResponseCacheRegistration? explicitResponseCache = null)
    {
        _logger = logger ?? NullLogger<MudHttpClientApplicationOptionsPostConfigure>.Instance;
        _explicitResponseCacheRegistered = explicitResponseCache is not null;
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, MudHttpClientApplicationOptions options)
    {
        if (options?.Clients is null)
            return;

        foreach (var kvp in options.Clients)
        {
            if (string.IsNullOrWhiteSpace(kvp.Value.BaseAddress))
            {
                MudHttpClientLog.ClientSkippedMissingBaseAddress(_logger, kvp.Key);
            }
        }

        // MT-13：客户端名区分大小写（Ordinal）。若配置中存在仅大小写不同的多个键，
        // 它们会被视为不同客户端 —— 极易误配（例如同时写 Default 与 default 时只有一个被解析到）。
        // 注意：此处迭代的是大小写敏感字典，重复实例仍会各自进入循环，故用显式分组检测。
        var caseCollisions = options.Clients.Keys
            .GroupBy(static k => k, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => string.Join(" / ", g))
            .ToList();

        foreach (var collision in caseCollisions)
        {
            MudHttpClientLog.MudHttpClientNameCaseCollision(_logger, collision);
        }

        // CFG-16：双入口冲突（AddHttpResponseCache 显式注册 + 配置节设置了非默认 ResponseCache）。
        if (_explicitResponseCacheRegistered
            && options.ResponseCache is { } cache
            && (cache.MaxCacheSize != ResponseCacheOptions.DefaultMaxCacheSize
                || cache.CleanupIntervalSeconds != ResponseCacheOptions.DefaultCleanupIntervalSeconds))
        {
            MudHttpClientLog.ResponseCacheConfigurationIgnored(_logger);
        }
    }
}
