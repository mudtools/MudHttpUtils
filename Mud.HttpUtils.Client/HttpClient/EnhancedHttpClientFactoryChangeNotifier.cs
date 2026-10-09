// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

#if NET6_0_OR_GREATER
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
internal sealed class EnhancedHttpClientFactoryChangeNotifier : IDisposable
{
    private readonly IDisposable? _subscription;
    private readonly object _gate = new();

    // G5：上一份 per-client 配置指纹（用于"仅失效变更过的客户端"，而非全量 InvalidateAll）。
    private Dictionary<string, ClientFingerprint> _fingerprints;

    public EnhancedHttpClientFactoryChangeNotifier(
        IOptionsMonitor<MudHttpClientApplicationOptions> monitor,
        IEnhancedHttpClientFactory factory)
    {
        _fingerprints = Snapshot(monitor.CurrentValue);

        // G5：配置节变更 → **逐客户端**失效（此前为全量 InvalidateAll：任一客户端变更都会丢弃全部缓存实例）。
        _subscription = monitor.OnChange((_, _) =>
        {
            Dictionary<string, ClientFingerprint> changed;
            lock (_gate)
            {
                if (_disposed)
                    return;

                var next = Snapshot(monitor.CurrentValue);
                changed = Diff(_fingerprints, next);
                _fingerprints = next;
            }

            foreach (var name in changed.Keys)
            {
                factory.Invalidate(name);
            }
        });
    }

    private volatile bool _disposed;

    /// <summary>
    /// G5：计算"需要失效"的客户端名集合 = 新增 + 变更 + 移除。
    /// 未变更的客户端**不**失效（避免无关客户端被连带丢弃）。
    /// </summary>
    private static Dictionary<string, ClientFingerprint> Diff(
        Dictionary<string, ClientFingerprint> previous,
        Dictionary<string, ClientFingerprint> next)
    {
        var changed = new Dictionary<string, ClientFingerprint>(StringComparer.Ordinal);

        foreach (var kvp in next)
        {
            if (!previous.TryGetValue(kvp.Key, out var old) || !old.Equals(kvp.Value))
                changed[kvp.Key] = kvp.Value;
        }

        // 被移除的客户端：其缓存实例同样需要失效（配置里已不存在）。
        foreach (var kvp in previous)
        {
            if (!next.ContainsKey(kvp.Key))
                changed[kvp.Key] = kvp.Value;
        }

        return changed;
    }

    private static Dictionary<string, ClientFingerprint> Snapshot(MudHttpClientApplicationOptions options)
    {
        var snapshot = new Dictionary<string, ClientFingerprint>(StringComparer.Ordinal);
        if (options?.Clients is null)
            return snapshot;

        foreach (var kvp in options.Clients)
        {
            snapshot[kvp.Key] = ClientFingerprint.From(kvp.Value);
        }

        return snapshot;
    }

    /// <summary>per-client 配置的轻量指纹（仅覆盖"可在创建期生效"的字段）。</summary>
    /// <remarks>
    /// G5 语义边界：<c>BaseAddress</c> / <c>TimeoutSeconds</c> / <c>DefaultHeaders</c>
    /// 在<b>注册期</b>固化于命名 HttpClient（<c>IHttpClientFactory</c> 语义），仅失效缓存<b>不能</b>使其变更生效
    /// （需由宿主重建命名客户端或经 <c>WithBaseAddress</c> 派生）。此处纳入指纹只是为了在它们变化时
    /// 也触发一次失效，不留"改了却毫无动作"的静默面。
    /// </remarks>
    private readonly struct ClientFingerprint : IEquatable<ClientFingerprint>
    {
        private readonly string? _baseAddress;
        private readonly double? _timeoutSeconds;
        private readonly bool _allowCustomBaseUrls;
        private readonly int _headersHash;

        private ClientFingerprint(string? baseAddress, double? timeoutSeconds, bool allowCustomBaseUrls, int headersHash)
        {
            _baseAddress = baseAddress;
            _timeoutSeconds = timeoutSeconds;
            _allowCustomBaseUrls = allowCustomBaseUrls;
            _headersHash = headersHash;
        }

        public static ClientFingerprint From(MudHttpClientOptions options)
        {
            var headersHash = 0;
            if (options.DefaultHeaders is not null)
            {
                foreach (var header in options.DefaultHeaders)
                {
                    unchecked
                    {
                        headersHash = (headersHash * 397)
                            ^ (header.Key is null ? 0 : StringComparer.Ordinal.GetHashCode(header.Key))
                            ^ (header.Value is null ? 0 : StringComparer.Ordinal.GetHashCode(header.Value));
                    }
                }
            }

            return new ClientFingerprint(
                options.BaseAddress, options.TimeoutSeconds, options.AllowCustomBaseUrls, headersHash);
        }

        public bool Equals(ClientFingerprint other)
            => string.Equals(_baseAddress, other._baseAddress, StringComparison.Ordinal)
               && _timeoutSeconds == other._timeoutSeconds
               && _allowCustomBaseUrls == other._allowCustomBaseUrls
               && _headersHash == other._headersHash;

        public override bool Equals(object? obj) => obj is ClientFingerprint other && Equals(other);

        public override int GetHashCode() => _headersHash;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _subscription?.Dispose();
    }
}
#endif
