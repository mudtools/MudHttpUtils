using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌主动刷新后台服务，定期检查并刷新即将过期的令牌。
/// 实现 <see cref="ITokenRefreshBackgroundService"/> 接口，兼容 netstandard2.0。
/// 支持注册多个令牌管理器，逐一刷新所有已注册的令牌。
/// </summary>
public sealed class TokenRefreshBackgroundService : ITokenRefreshBackgroundService, IDisposable
{
    private readonly ConcurrentDictionary<string, ITokenManager> _tokenManagers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;
    private readonly TokenRefreshBackgroundOptions _options;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeSpan _retryDelay;
    private Timer? _timer;
    private bool _disposed;

    /// <summary>
    /// 初始化 TokenRefreshBackgroundService 实例。
    /// </summary>
    /// <param name="options">后台刷新配置选项。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRefreshBackgroundService(
        TokenRefreshBackgroundOptions? options = null,
        ILogger<TokenRefreshBackgroundService>? logger = null)
    {
        _options = options ?? new TokenRefreshBackgroundOptions();
        _refreshInterval = TimeSpan.FromSeconds(_options.RefreshIntervalSeconds);
        _retryDelay = TimeSpan.FromSeconds(_options.RetryDelaySeconds);
        _logger = logger ?? NullLogger<TokenRefreshBackgroundService>.Instance;
    }

    /// <summary>
    /// 初始化 TokenRefreshBackgroundService 实例，使用 IOptions 配置。
    /// </summary>
    /// <param name="options">后台刷新配置选项（IOptions 模式）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRefreshBackgroundService(
        IOptions<TokenRefreshBackgroundOptions> options,
        ILogger<TokenRefreshBackgroundService>? logger = null)
        : this(options?.Value, logger)
    {
    }

    /// <summary>
    /// 初始化 TokenRefreshBackgroundService 实例，绑定单个令牌管理器（向后兼容）。
    /// </summary>
    /// <param name="tokenManager">令牌管理器。</param>
    /// <param name="options">后台刷新配置选项。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRefreshBackgroundService(
        ITokenManager tokenManager,
        TokenRefreshBackgroundOptions? options = null,
        ILogger<TokenRefreshBackgroundService>? logger = null)
        : this(options, logger)
    {
        if (tokenManager != null)
            RegisterTokenManager(tokenManager);
    }

    /// <summary>
    /// 初始化 TokenRefreshBackgroundService 实例，绑定单个令牌管理器（向后兼容）。
    /// </summary>
    /// <param name="tokenManager">令牌管理器。</param>
    /// <param name="options">后台刷新配置选项（IOptions 模式）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRefreshBackgroundService(
        ITokenManager tokenManager,
        IOptions<TokenRefreshBackgroundOptions> options,
        ILogger<TokenRefreshBackgroundService>? logger = null)
        : this(tokenManager, options?.Value, logger)
    {
    }

    /// <inheritdoc />
    public void RegisterTokenManager(ITokenManager tokenManager, string? name = null)
    {
        if (tokenManager == null)
            throw new ArgumentNullException(nameof(tokenManager));

        var key = name ?? Guid.NewGuid().ToString("N");
        _tokenManagers[key] = tokenManager;
        _logger.LogDebug("已注册令牌管理器: {Name}", key);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TokenRefreshBackgroundService));

        if (!_options.Enabled)
        {
            _logger.LogInformation("令牌主动刷新后台服务已禁用");
            return Task.CompletedTask;
        }

        if (_tokenManagers.IsEmpty)
        {
            _logger.LogWarning("未注册任何令牌管理器，后台服务不会刷新任何令牌");
        }

        _timer = new Timer(
            RefreshTokenCallback,
            null,
            _refreshInterval,
            _refreshInterval);

        _logger.LogInformation("令牌主动刷新后台服务已启动，刷新间隔: {Interval}秒，已注册 {Count} 个令牌管理器",
            _refreshInterval.TotalSeconds, _tokenManagers.Count);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("令牌主动刷新后台服务已停止");
        return Task.CompletedTask;
    }

    private async void RefreshTokenCallback(object? state)
    {
        if (_tokenManagers.IsEmpty)
            return;

        foreach (var kvp in _tokenManagers)
        {
            try
            {
                var token = await kvp.Value.GetOrRefreshTokenAsync(CancellationToken.None).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    _logger.LogDebug("令牌管理器 {Name} 主动刷新成功", kvp.Key);
                }
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("令牌管理器 {Name} 已释放，移除并停止刷新", kvp.Key);
                _tokenManagers.TryRemove(kvp.Key, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "令牌管理器 {Name} 主动刷新失败", kvp.Key);

                if (_options.StopOnError)
                {
                    _logger.LogCritical("令牌管理器 {Name} 主动刷新失败且配置为停止服务，后台服务将终止", kvp.Key);
                    _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer?.Dispose();
    }
}
