#if NET6_0_OR_GREATER
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mud.HttpUtils;

public sealed class TokenRefreshHostedService : BackgroundService
{
    private readonly ITokenManager _tokenManager;
    private readonly TokenRefreshBackgroundOptions _options;
    private readonly ILogger<TokenRefreshHostedService> _logger;

    public TokenRefreshHostedService(
        ITokenManager tokenManager,
        IOptions<TokenRefreshBackgroundOptions> options,
        ILogger<TokenRefreshHostedService> logger)
    {
        _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("令牌主动刷新后台服务已禁用");
            return;
        }

        _logger.LogInformation("令牌主动刷新后台服务已启动，刷新间隔: {Interval}秒", _options.RefreshIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.RefreshIntervalSeconds), stoppingToken);

                _logger.LogDebug("开始主动刷新令牌");
                await _tokenManager.GetOrRefreshTokenAsync(stoppingToken);
                _logger.LogDebug("令牌主动刷新完成");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("令牌主动刷新后台服务正在停止");
                break;
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("令牌管理器已释放，停止刷新");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "令牌主动刷新失败，将在 {RetryDelay}秒 后重试", _options.RetryDelaySeconds);

                if (_options.StopOnError)
                {
                    _logger.LogCritical("令牌主动刷新失败且配置为停止服务，后台服务将终止");
                    throw;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("令牌主动刷新后台服务已停止");
    }
}
#endif
