using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌主动刷新后台服务，定期检查并刷新即将过期的令牌。
/// 实现 <see cref="ITokenRefreshBackgroundService"/> 接口，兼容 netstandard2.0。
/// </summary>
public sealed class TokenRefreshBackgroundService : ITokenRefreshBackgroundService, IDisposable
{
    private readonly ITokenManager _tokenManager;
    private readonly ILogger _logger;
    private readonly TokenRefreshBackgroundOptions _options;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeSpan _retryDelay;
    private Timer? _timer;
    private bool _disposed;

    /// <summary>
    /// 初始化 TokenRefreshBackgroundService 实例。
    /// </summary>
    /// <param name="tokenManager">令牌管理器。</param>
    /// <param name="options">后台刷新配置选项。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRefreshBackgroundService(
        ITokenManager tokenManager,
        TokenRefreshBackgroundOptions? options = null,
        ILogger<TokenRefreshBackgroundService>? logger = null)
    {
        _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
        _options = options ?? new TokenRefreshBackgroundOptions();
        _refreshInterval = TimeSpan.FromSeconds(_options.RefreshIntervalSeconds);
        _retryDelay = TimeSpan.FromSeconds(_options.RetryDelaySeconds);
        _logger = logger ?? NullLogger<TokenRefreshBackgroundService>.Instance;
    }

    /// <summary>
    /// 初始化 TokenRefreshBackgroundService 实例，使用 IOptions 配置。
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
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TokenRefreshBackgroundService));

        if (!_options.Enabled)
        {
            _logger.LogInformation("令牌主动刷新后台服务已禁用");
            return Task.CompletedTask;
        }

        _timer = new Timer(
            RefreshTokenCallback,
            null,
            _refreshInterval,
            _refreshInterval);

        _logger.LogInformation("令牌主动刷新后台服务已启动，刷新间隔: {Interval}秒", _refreshInterval.TotalSeconds);

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
        try
        {
            var token = await _tokenManager.GetOrRefreshTokenAsync(CancellationToken.None).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
            {
                _logger.LogDebug("令牌主动刷新成功");
            }
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("令牌管理器已释放，停止刷新");
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "令牌主动刷新失败，将在 {RetryDelay}秒 后重试", _retryDelay.TotalSeconds);

            if (_options.StopOnError)
            {
                _logger.LogCritical("令牌主动刷新失败且配置为停止服务，后台服务将终止");
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);
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
