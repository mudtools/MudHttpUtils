// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

#if NET6_0_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
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
/// <para>TMX-10：支持配置热更新——运行期修改 <c>RefreshIntervalSeconds</c>/<c>StopOnError</c>/
/// <c>MaxConsecutiveFailures</c> 等选项在下一轮刷新周期生效。</para>
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
    private readonly IOptionsMonitor<TokenRefreshBackgroundOptions> _optionsMonitor;
    private readonly ILogger<TokenRefreshHostedService> _logger;
    private readonly TokenRefreshLoopState _loopState = new(); // P1.6（TK-10-max）跨周期保留连续失败计数

    // L-9：主动停止（StopOnError / MaxConsecutiveFailures）后的可观测与恢复通道。
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private volatile bool _stopped;

    // L-9：宿主停止令牌。BackgroundService.StoppingToken 在 net6.0 不可用（本项目依赖的
    // Microsoft.Extensions.Hosting.Abstractions 版本亦未在 net8/net10 提供该属性），
    // 故统一捕获 ExecuteAsync 收到的令牌：单次写入、只读消费，竞态良性。
    // 该字段仅在循环已运行（_stopped 可能为 true）后才被 RestartAsync 读取，届时必然已赋值。
    private CancellationToken _hostStoppingToken;

    /// <inheritdoc />
    /// <remarks>L-9：仅反映「因刷新失败而主动停止调度」，宿主正常停止不会使其为 true。</remarks>
    public bool IsStopped => _stopped;

    /// <summary>
    /// TMX-10：当前生效的配置（每次循环读取 CurrentValue，支持运行期热更新）。
    /// </summary>
    private TokenRefreshBackgroundOptions Options => _optionsMonitor.CurrentValue;

    /// <summary>
    /// 初始化 <see cref="TokenRefreshHostedService"/> 类的新实例。
    /// TMX-10/D8：DI 激活唯一构造入口——<see cref="ActivatorUtilitiesConstructorAttribute"/> 标注确保
    /// <c>ActivatorUtilities</c> 确定性地选择此 ctor，支持配置热更新。
    /// </summary>
    /// <param name="optionsMonitor">令牌刷新后台服务配置选项监视器，支持热更新。</param>
    /// <param name="logger">日志记录器。</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> 或 <paramref name="logger"/> 为 null。</exception>
    [ActivatorUtilitiesConstructor]
    public TokenRefreshHostedService(
        IOptionsMonitor<TokenRefreshBackgroundOptions> optionsMonitor,
        ILogger<TokenRefreshHostedService> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 初始化 <see cref="TokenRefreshHostedService"/> 类的新实例（向后兼容 IOptions 重载，不支持热更新）。
    /// </summary>
    /// <remarks>
    /// TMX-19（P0）：internal —— 容器默认构造选择要求「唯一公共构造」。
    /// 多个公共构造会使 <c>AddSingleton&lt;TokenRefreshHostedService&gt;()</c> 在解析时抛
    /// "The following constructors are ambiguous"（<see cref="ActivatorUtilitiesConstructorAttribute"/>
    /// 仅对 <c>ActivatorUtilities</c> 生效，不参与容器默认构造选择）。
    /// 外部直接 <c>new</c> 场景请使用唯一公共构造（IOptionsMonitor 重载）。
    /// </remarks>
    /// <param name="options">令牌刷新后台服务配置选项（快照）。</param>
    /// <param name="logger">日志记录器。</param>
    internal TokenRefreshHostedService(
        IOptions<TokenRefreshBackgroundOptions> options,
        ILogger<TokenRefreshHostedService> logger)
        : this(options != null
            ? new OptionsWrapperMonitor<TokenRefreshBackgroundOptions>(options.Value)
            : throw new ArgumentNullException(nameof(options)),
            logger)
    {
    }

    /// <summary>
    /// 初始化 <see cref="TokenRefreshHostedService"/> 类的新实例，绑定单个令牌管理器（向后兼容）。
    /// </summary>
    /// <remarks>TMX-19（P0）：internal，理由见 <see cref="TokenRefreshHostedService(IOptions{TokenRefreshBackgroundOptions}, ILogger{TokenRefreshHostedService})"/>。</remarks>
    /// <param name="tokenManager">令牌管理器实例。</param>
    /// <param name="options">令牌刷新后台服务配置选项。</param>
    /// <param name="logger">日志记录器。</param>
    /// <exception cref="ArgumentNullException"><paramref name="tokenManager"/>、<paramref name="options"/> 或 <paramref name="logger"/> 为 null。</exception>
    internal TokenRefreshHostedService(
        ITokenManager tokenManager,
        IOptions<TokenRefreshBackgroundOptions> options,
        ILogger<TokenRefreshHostedService> logger)
        : this(options, logger)
    {
        if (tokenManager != null)
            RegisterTokenManager(tokenManager);
    }

    /// <summary>
    /// TMX-10：初始化并绑定单个令牌管理器（IOptionsMonitor 重载，支持热更新）。
    /// </summary>
    /// <remarks>TMX-19（P0）：internal，理由见 <see cref="TokenRefreshHostedService(IOptions{TokenRefreshBackgroundOptions}, ILogger{TokenRefreshHostedService})"/>。</remarks>
    internal TokenRefreshHostedService(
        ITokenManager tokenManager,
        IOptionsMonitor<TokenRefreshBackgroundOptions> optionsMonitor,
        ILogger<TokenRefreshHostedService> logger)
        : this(optionsMonitor, logger)
    {
        if (tokenManager != null)
            RegisterTokenManager(tokenManager);
    }

    /// <inheritdoc />
    public void RegisterTokenManager(ITokenManager tokenManager, string? name = null)
    {
        if (tokenManager == null)
            throw new ArgumentNullException(nameof(tokenManager));

        // SR-M4（P3.2，D14）注册守卫统一：经 TokenRefreshHelper.TryRegisterTokenManager 单一实现，
        // 与 TokenRefreshBackgroundService（netstandard2.0）同源，消除双实现行为漂移。
        TokenRefreshHelper.TryRegisterTokenManager(_tokenManagers, tokenManager, name, _logger);
    }

    /// <summary>
    /// 执行后台服务的主要逻辑,定时刷新令牌。
    /// </summary>
    /// <param name="stoppingToken">用于监控是否请求停止服务的取消令牌。</param>
    /// <returns>表示后台服务执行操作的任务。</returns>
    /// <remarks>
    /// <para>此方法实现了一个定时循环,按照配置的间隔逐一调用所有已注册令牌管理器的 <c>GetOrRefreshTokenAsync</c> 刷新令牌。</para>
    /// <para>异常处理策略:</para>
    /// <list type="number">
    ///   <item><see cref="OperationCanceledException"/>: 服务正常停止时捕获,优雅退出</item>
    ///   <item><see cref="ObjectDisposedException"/>: 令牌管理器已释放时捕获,移除该管理器</item>
    ///   <item>其他异常: 记录错误日志,根据配置决定是否重试或停止服务</item>
    /// </list>
    /// <para>如果 <see cref="TokenRefreshBackgroundOptions.StopOnError"/> 为 <c>true</c>，或连续失败次数达到
    /// <see cref="TokenRefreshBackgroundOptions.MaxConsecutiveFailures"/>，刷新失败后本服务将记录 Critical 并优雅停止
    /// （仅停止本服务，宿主继续运行——刻意不抛异常，避免 .NET BackgroundServiceExceptionBehavior 默认 StopHost 连带停止整个应用）。</para>
    /// <para>TMX-10：配置在每轮循环开始时读取 <see cref="IOptionsMonitor{T}"/>.CurrentValue，
    /// 运行期修改的 <c>RefreshIntervalSeconds</c>/<c>StopOnError</c>/<c>MaxConsecutiveFailures</c> 等在下一轮生效。</para>
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var current = Options;  // TMX-10：每轮循环读取 CurrentValue，支持热更新
        if (!current.Enabled)
        {
            MudHttpClientLog.TokenRefreshServiceDisabled(_logger);
            return;
        }

        if (_tokenManagers.IsEmpty)
        {
            MudHttpClientLog.TokenRefreshNoManagersRegistered(_logger);
        }

        _hostStoppingToken = stoppingToken;
        await RunLoopAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// L-9：刷新调度循环主体。抽出为独立方法，使 <see cref="RestartAsync"/> 能在主动停止后重新进入循环。
    /// </summary>
    /// <param name="stoppingToken">宿主停止令牌。</param>
    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        MudHttpClientLog.TokenRefreshServiceStarted(_logger, Options.RefreshIntervalSeconds, _tokenManagers.Count);

        while (!stoppingToken.IsCancellationRequested)
        {
            var current = Options;  // TMX-10：每轮拾取最新配置
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(current.RefreshIntervalSeconds), stoppingToken);

                var shouldContinue = await TokenRefreshHelper.RefreshAllTokenManagersAsync(
                    _tokenManagers, _logger, current, stoppingToken, _loopState);
                if (!shouldContinue)
                {
                    // P1.6（TK-10）StopOnError / MaxConsecutiveFailures 触发停止：记录 Critical 后优雅 break。
                    // 刻意不抛异常——.NET 6+ BackgroundServiceExceptionBehavior 默认 StopHost 会将宿主一并停止。
                    // 本服务停止后宿主继续运行，其余托管服务不受影响。
                    // L-9：置位 IsStopped，使运维侧能区分「服务在跑但没活干」与「已停止调度」，
                    // 并可通过 RestartAsync 复位恢复（无需重启进程）。
                    _stopped = true;
                    MudHttpClientLog.TokenRefreshFailedAndStopped(_logger, string.Join(",", _tokenManagers.Keys));
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                MudHttpClientLog.TokenRefreshServiceStopping(_logger);
                break;
            }
            // MT-15：StopOnError=true 时原 when 过滤器不成立（!StopOnError 为 false），异常会逃出 ExecuteAsync，
            // 由 BackgroundService 按 BackgroundServiceExceptionBehavior 处理（默认 StopHost —— 连同宿主一起停止），
            // 与本节注释「刻意不抛异常」直接矛盾。这里补上该分支：记 Critical 后优雅 break。
            catch (Exception ex) when (Options.StopOnError)
            {
                _stopped = true;
                MudHttpClientLog.TokenRefreshFailedAndStopped(_logger, string.Join(",", _tokenManagers.Keys));
                System.Diagnostics.Debug.WriteLine(
                    $"[Mud.HttpUtils] TokenRefreshHostedService: StopOnError=true，刷新异常后停止调度。{ex}");
                break;
            }
            catch (Exception ex) when (!_tokenManagers.IsEmpty)
            {
                MudHttpClientLog.TokenRefreshFailedWithRetry(_logger, current.RetryDelaySeconds, ex);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(current.RetryDelaySeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (Exception ex)                                   // TMX-10：任何未归类异常都不得逃出 ExecuteAsync
            {
                MudHttpClientLog.TokenRefreshUnhandledException(_logger, ex);
                MudHttpClientLog.TokenRefreshFailedAndStopped(_logger, string.Join(",", _tokenManagers.Keys));
                break;                                             // 仅停本服务，宿主继续
            }
        }

        MudHttpClientLog.TokenRefreshServiceStopped(_logger);
    }

    /// <inheritdoc />
    /// <remarks>
    /// L-9：复位连续失败计数后重新进入调度循环；服务仍在运行或宿主正在停止时为无操作（幂等）。
    /// 新循环以 fire-and-forget 方式运行（内部已 try/catch，异常不会逃逸）。
    /// </remarks>
    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        if (!Options.Enabled)
        {
            MudHttpClientLog.TokenRefreshServiceDisabled(_logger);
            return;
        }

        await _restartGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_stopped)
                return;   // 幂等：仍在运行

            var hostToken = _hostStoppingToken;
            if (hostToken.IsCancellationRequested)
                return;   // 宿主正在停止，重启无意义

            _loopState.Reset();
            _stopped = false;

            // 不 await：RestartAsync 需立即返回，循环在后台继续运行（与 BackgroundService 的启动语义一致）。
            _ = Task.Run(() => RunLoopAsync(hostToken), CancellationToken.None);
        }
        finally
        {
            _restartGate.Release();
        }
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
