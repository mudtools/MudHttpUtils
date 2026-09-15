// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// 配置变更通知器：订阅 <see cref="IOptionsMonitor{T}"/> 变更并在配置变更时清空
/// <see cref="IEnhancedHttpClientFactory"/> 的缓存，使 AllowCustomBaseUrls / DefaultHeaders
/// 等变更在下次解析时生效。
/// </summary>
/// <remarks>
/// 仅在 net6+ 有效（<see cref="EnhancedHttpClientFactory"/> 的 netstandard2.0 分支走
/// <c>ClientFactories</c> 字典不缓存，语义天然一致）。
/// D4：keyed 客户端提升为 Singleton 后，配置变更需显式触发失效。
/// </remarks>
#if NET6_0_OR_GREATER
internal sealed class EnhancedHttpClientFactoryChangeNotifier : IDisposable
{
    private readonly IDisposable? _subscription;

    public EnhancedHttpClientFactoryChangeNotifier(
        IOptionsMonitor<MudHttpClientApplicationOptions> monitor,
        IEnhancedHttpClientFactory factory)
    {
        // 配置节变更 → 清空缓存，使 AllowCustomBaseUrls / DefaultHeaders 等变更生效
        _subscription = monitor.OnChange((_, _) => factory.InvalidateAll());
    }

    public void Dispose() => _subscription?.Dispose();
}
#endif
