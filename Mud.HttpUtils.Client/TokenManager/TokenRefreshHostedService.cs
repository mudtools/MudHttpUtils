#if NET6_0_OR_GREATER
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌主动刷新后台服务,用于定时自动刷新访问令牌。
/// 支持注册多个令牌管理器，逐一刷新所有已注册的令牌。
/// </summary>
/// <remarks>
/// <para>此类继承自 <see cref="BackgroundService"/>,是一个ASP.NET Core托管服务,
/// 在应用程序后台运行,定时刷新令牌以确保令牌始终有效。</para>
/// <para>主要功能:</para>
/// <list type="bullet">
///   <item>按配置的间隔定时刷新令牌</item>
///   <item>支持错误重试机制</item>
///   <item>支持错误时停止服务的配置选项</item>
///   <item>完整的日志记录</item>
///   <item>优雅的服务启动和停止</item>
///   <item>支持多令牌管理器注册</item>
/// </list>
/// <para>此服务仅在 .NET 6.0 或更高版本中可用。</para>
/// </remarks>
/// <example>
/// <code>
/// // 在 Program.cs 或 Startup.cs 中注册服务
/// builder.Services.AddTokenRefreshBackgroundService(options =>
/// {
///     options.Enabled = true;
///     options.RefreshIntervalSeconds = 300; // 5分钟
///     options.RetryDelaySeconds = 30;
///     options.StopOnError = false;
/// });
/// </code>
/// </example>
/// <seealso cref="BackgroundService"/>
/// <seealso cref="ITokenManager"/>
/// <seealso cref="TokenRefreshBackgroundOptions"/>
public sealed class TokenRefreshHostedService : BackgroundService, ITokenRefreshBackgroundService
{
    private readonly ConcurrentDictionary<string, ITokenManager> _tokenManagers = new(StringComparer.OrdinalIgnoreCase);
    private readonly TokenRefreshBackgroundOptions _options;
    private readonly ILogger<TokenRefreshHostedService> _logger;

    /// <summary>
    /// 初始化 <see cref="TokenRefreshHostedService"/> 类的新实例。
    /// </summary>
    /// <param name="options">令牌刷新后台服务配置选项。</param>
    /// <param name="logger">日志记录器。</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 或 <paramref name="logger"/> 为 null。</exception>
    public TokenRefreshHostedService(
        IOptions<TokenRefreshBackgroundOptions> options,
        ILogger<TokenRefreshHostedService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 初始化 <see cref="TokenRefreshHostedService"/> 类的新实例，绑定单个令牌管理器（向后兼容）。
    /// </summary>
    /// <param name="tokenManager">令牌管理器实例。</param>
    /// <param name="options">令牌刷新后台服务配置选项。</param>
    /// <param name="logger">日志记录器。</param>
    /// <exception cref="ArgumentNullException"><paramref name="tokenManager"/>、<paramref name="options"/> 或 <paramref name="logger"/> 为 null。</exception>
    public TokenRefreshHostedService(
        ITokenManager tokenManager,
        IOptions<TokenRefreshBackgroundOptions> options,
        ILogger<TokenRefreshHostedService> logger)
        : this(options, logger)
    {
        if (tokenManager != null)
            RegisterTokenManager(tokenManager);
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

    /// <summary>
    /// 执行后台服务的主要逻辑,定时刷新令牌。
    /// </summary>
    /// <param name="stoppingToken">用于监控是否请求停止服务的取消令牌。</param>
    /// <returns>表示后台服务执行操作的任务。</returns>
    /// <remarks>
    /// <para>此方法实现了一个定时循环,按照配置的间隔逐一调用所有已注册令牌管理器的 <see cref="ITokenManager.GetOrRefreshTokenAsync"/> 刷新令牌。</para>
    /// <para>异常处理策略:</para>
    /// <list type="number">
    ///   <item><see cref="OperationCanceledException"/>: 服务正常停止时捕获,优雅退出</item>
    ///   <item><see cref="ObjectDisposedException"/>: 令牌管理器已释放时捕获,移除该管理器</item>
    ///   <item>其他异常: 记录错误日志,根据配置决定是否重试或停止服务</item>
    /// </list>
    /// <para>如果 <see cref="TokenRefreshBackgroundOptions.StopOnError"/> 为 <c>true</c>,刷新失败时将抛出异常终止服务。</para>
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("令牌主动刷新后台服务已禁用");
            return;
        }

        if (_tokenManagers.IsEmpty)
        {
            _logger.LogWarning("未注册任何令牌管理器，后台服务不会刷新任何令牌");
        }

        _logger.LogInformation("令牌主动刷新后台服务已启动，刷新间隔: {Interval}秒，已注册 {Count} 个令牌管理器",
            _options.RefreshIntervalSeconds, _tokenManagers.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.RefreshIntervalSeconds), stoppingToken);

                foreach (var kvp in _tokenManagers)
                {
                    try
                    {
                        _logger.LogDebug("开始主动刷新令牌管理器 {Name}", kvp.Key);
                        await kvp.Value.GetOrRefreshTokenAsync(stoppingToken);
                        _logger.LogDebug("令牌管理器 {Name} 主动刷新完成", kvp.Key);
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger.LogWarning("令牌管理器 {Name} 已释放，移除", kvp.Key);
                        _tokenManagers.TryRemove(kvp.Key, out _);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "令牌管理器 {Name} 主动刷新失败", kvp.Key);

                        if (_options.StopOnError)
                        {
                            _logger.LogCritical("令牌管理器 {Name} 主动刷新失败且配置为停止服务，后台服务将终止", kvp.Key);
                            throw;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("令牌主动刷新后台服务正在停止");
                break;
            }
            catch (Exception ex) when (!_tokenManagers.IsEmpty)
            {
                _logger.LogError(ex, "令牌主动刷新失败，将在 {RetryDelay}秒 后重试", _options.RetryDelaySeconds);

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

    /// <inheritdoc />
    public new Task StartAsync(CancellationToken cancellationToken = default)
    {
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public new Task StopAsync(CancellationToken cancellationToken = default)
    {
        return base.StopAsync(cancellationToken);
    }
}
#endif
