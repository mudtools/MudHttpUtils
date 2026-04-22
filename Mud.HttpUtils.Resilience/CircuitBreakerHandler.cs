using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// 熔断策略处理器，当连续失败次数超过阈值时，短时间内快速拒绝请求，防止级联故障。
/// </summary>
public sealed class CircuitBreakerHandler
{
    private readonly ILogger _logger;
    private readonly int _failureThreshold;
    private readonly TimeSpan _breakDuration;
    private readonly object _lock = new();

    private int _failureCount;
    private DateTime? _lastFailureTime;
    private CircuitState _state = CircuitState.Closed;

    /// <summary>
    /// 初始化 CircuitBreakerHandler 实例。
    /// </summary>
    /// <param name="failureThreshold">触发熔断的连续失败阈值。</param>
    /// <param name="breakDuration">熔断持续时间。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    /// <exception cref="ArgumentOutOfRangeException">参数无效时抛出。</exception>
    public CircuitBreakerHandler(
        int failureThreshold = 5,
        TimeSpan breakDuration = default,
        ILogger? logger = null)
    {
        if (failureThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold), "失败阈值必须大于零。");

        _failureThreshold = failureThreshold;
        _breakDuration = breakDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : breakDuration;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// 当前熔断器状态。
    /// </summary>
    public CircuitState State
    {
        get
        {
            lock (_lock)
            {
                if (_state == CircuitState.Open)
                {
                    if (_lastFailureTime.HasValue &&
                        DateTime.UtcNow - _lastFailureTime.Value >= _breakDuration)
                    {
                        _state = CircuitState.HalfOpen;
                        _logger.LogInformation("熔断器进入半开状态，允许试探请求。");
                    }
                }

                return _state;
            }
        }
    }

    /// <summary>
    /// 执行带熔断控制的异步操作。
    /// </summary>
    /// <typeparam name="TResult">操作返回类型。</typeparam>
    /// <param name="operation">需要执行的异步操作。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    /// <exception cref="CircuitBreakerOpenException">当熔断器处于开启状态时抛出。</exception>
    public async Task<TResult?> ExecuteAsync<TResult>(
        Func<Task<TResult?>> operation,
        CancellationToken cancellationToken = default)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        var currentState = State;

        if (currentState == CircuitState.Open)
        {
            throw new CircuitBreakerOpenException(
                $"熔断器处于开启状态，请求被快速拒绝。将在 {_breakDuration.TotalSeconds} 秒后尝试恢复。");
        }

        try
        {
            var result = await operation().ConfigureAwait(false);
            RecordSuccess();
            return result;
        }
        catch
        {
            RecordFailure();
            throw;
        }
    }

    private void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Closed;
                _logger.LogInformation("熔断器关闭，服务恢复正常。");
            }

            _failureCount = 0;
            _lastFailureTime = null;
        }
    }

    private void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;

            if (_failureCount >= _failureThreshold)
            {
                _state = CircuitState.Open;
                _logger.LogWarning(
                    "熔断器开启：连续失败 {FailureCount} 次，将在 {BreakDuration}s 内快速拒绝请求。",
                    _failureCount,
                    _breakDuration.TotalSeconds);
            }
        }
    }
}

/// <summary>
/// 熔断器状态枚举。
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// 关闭状态：正常处理请求。
    /// </summary>
    Closed,

    /// <summary>
    /// 开启状态：快速拒绝请求。
    /// </summary>
    Open,

    /// <summary>
    /// 半开状态：允许试探请求以检测服务是否恢复。
    /// </summary>
    HalfOpen
}

/// <summary>
/// 熔断器开启时抛出的异常。
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    /// <summary>
    /// 初始化 CircuitBreakerOpenException 实例。
    /// </summary>
    /// <param name="message">异常消息。</param>
    public CircuitBreakerOpenException(string message) : base(message) { }
}
