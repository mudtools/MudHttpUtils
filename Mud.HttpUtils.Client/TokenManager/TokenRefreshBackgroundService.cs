// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
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
    private readonly TokenRefreshLoopState _loopState = new();
    // SR-L3（P3.6，D14）：Timer 交换加锁；_timer 读取处 Volatile.Read。
    private readonly object _timerLock = new();
    private Timer? _timer;
    private int _running; // P1.6（TK-11）重入闸：同一时刻只允许一个刷新编排在运行
    private bool _disposed;

    // L-9：主动停止（StopOnError / MaxConsecutiveFailures）后的可观测与恢复通道。
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private volatile bool _stopped;

    /// <inheritdoc />
    /// <remarks>L-9：仅反映「因刷新失败而主动停止调度」，<see cref="StopAsync"/> 不会使其为 true。</remarks>
    public bool IsStopped => _stopped;

    /// <summary>
    /// 初始化 TokenRefreshBackgroundService 实例。
    /// </summary>
    /// <remarks>
    /// TMX-19（P0）：internal —— 容器默认构造选择要求「唯一公共构造」，
    /// 多个公共构造会使 DI 解析抛 "The following constructors are ambiguous"。
    /// 外部直接 <c>new</c> 场景请使用唯一公共构造（<see cref="TokenRefreshBackgroundService(IOptions{TokenRefreshBackgroundOptions}, ILogger{TokenRefreshBackgroundService}?)"/>）。
    /// </remarks>
    /// <param name="options">后台刷新配置选项。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    internal TokenRefreshBackgroundService(
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
    /// TMX-10/D8：DI 激活唯一构造入口（Timer 版不支持热更新，仍为快照）。
    /// </summary>
    /// <param name="options">后台刷新配置选项（IOptions 模式）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    [ActivatorUtilitiesConstructor]
    public TokenRefreshBackgroundService(
        IOptions<TokenRefreshBackgroundOptions> options,
        ILogger<TokenRefreshBackgroundService>? logger = null)
        : this(options?.Value, logger)
    {
    }

    /// <summary>
    /// 初始化 TokenRefreshBackgroundService 实例，绑定单个令牌管理器（向后兼容）。
    /// </summary>
    /// <remarks>TMX-19（P0）：internal，理由见 <see cref="TokenRefreshBackgroundService(TokenRefreshBackgroundOptions?, ILogger{TokenRefreshBackgroundService}?)"/>。</remarks>
    /// <param name="tokenManager">令牌管理器。</param>
    /// <param name="options">后台刷新配置选项。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    internal TokenRefreshBackgroundService(
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
    /// <remarks>TMX-19（P0）：internal，理由见 <see cref="TokenRefreshBackgroundService(TokenRefreshBackgroundOptions?, ILogger{TokenRefreshBackgroundService}?)"/>。</remarks>
    /// <param name="tokenManager">令牌管理器。</param>
    /// <param name="options">后台刷新配置选项（IOptions 模式）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    internal TokenRefreshBackgroundService(
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

        // SR-M4（P3.2，D14）注册守卫统一：经 TokenRefreshHelper.TryRegisterTokenManager 单一实现，
        // 与 TokenRefreshHostedService（net6+）同源，消除 ns2.0 / net6+ 双实现行为漂移。
        TokenRefreshHelper.TryRegisterTokenManager(_tokenManagers, tokenManager, name, _logger);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TokenRefreshBackgroundService));
        }

        if (!_options.Enabled)
        {
            MudHttpClientLog.TokenRefreshServiceDisabled(_logger);
            return Task.CompletedTask;
        }

        if (_tokenManagers.IsEmpty)
        {
            MudHttpClientLog.TokenRefreshNoManagersRegistered(_logger);
        }

        // P1.6（TK-11）StartAsync 幂等：先停止旧 Timer 再创建新 Timer，避免重复启动导致多 Timer 并发刷新
        // SR-L3（P3.6，D14）Timer 交换加锁：并发 StartAsync 下双 Timer 交换竞态（读旧引用/泄漏）消除。
        lock (_timerLock)
        {
            var oldTimer = _timer;
            _timer = null;
            oldTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            oldTimer?.Dispose();

            _timer = new Timer(
                RefreshTokenCallback,
                null,
                _refreshInterval,
                _refreshInterval);
        }

        MudHttpClientLog.TokenRefreshServiceStarted(_logger, _refreshInterval.TotalSeconds, _tokenManagers.Count);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_timerLock)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        MudHttpClientLog.TokenRefreshServiceStopped(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// L-9：复位连续失败计数后重建 Timer 恢复调度；服务仍在运行或已释放时分别无操作 / 抛
    /// <see cref="ObjectDisposedException"/>（与 <see cref="StartAsync"/> 一致）。
    /// </remarks>
    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TokenRefreshBackgroundService));

        await _restartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_stopped)
                return;   // 幂等：仍在运行

            _loopState.Reset();
            _stopped = false;

            // StartAsync 内部为「先停旧 Timer 再建新 Timer」的幂等实现（P1.6/TK-11），
            // 可直接复用，无需重复 Timer 交换逻辑。
            await StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _restartGate.Release();
        }
    }

    private async void RefreshTokenCallback(object? state)
    {
        // P1.6（TK-11）重入闸：Timer 可能在上一轮刷新尚未结束时再次触发，
        // 通过 Interlocked.CompareExchange 保证同一时刻只有一个刷新编排在运行，
        // 避免共享令牌被并发刷新或刷新风暴。
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        try
        {
            if (_disposed)
                return;

            var shouldContinue = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
                _tokenManagers, _logger, _options, CancellationToken.None, _loopState).ConfigureAwait(false);
            if (!shouldContinue)
            {
                // L-9：置位 IsStopped，使运维侧可探测"已停止调度"并可通过 RestartAsync 恢复。
                _stopped = true;
                // SR-L3（P3.6）：回调读 Timer 引用经 Volatile.Read（锁外安全读）
                Volatile.Read(ref _timer)?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        catch (Exception ex)
        {
            MudHttpClientLog.TokenRefreshUnhandledException(_logger, ex);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
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
        lock (_timerLock)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
        }
    }
}
