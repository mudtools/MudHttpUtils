// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Observability;

/// <summary>熔断器状态变更事件的诊断负载。</summary>
public sealed class CircuitBreakerDiagnosticPayload
{
    /// <summary>策略键。</summary>
    public string PolicyKey { get; }

    /// <summary>新状态名称（Open/HalfOpen/Closed）。</summary>
    public string State { get; }

    /// <summary>事件时间戳（UTC）。</summary>
    public DateTimeOffset Timestamp { get; }

    public CircuitBreakerDiagnosticPayload(string policyKey, string state)
    {
        PolicyKey = policyKey;
        State = state;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>令牌刷新完成事件的诊断负载。</summary>
public sealed class TokenRefreshDiagnosticPayload
{
    public string? TokenManagerKey { get; }
    public bool Success { get; }
    public bool IsFallback { get; }
    public double ElapsedMs { get; }
    public DateTimeOffset Timestamp { get; }

    public TokenRefreshDiagnosticPayload(string? tokenManagerKey, bool success, bool isFallback, double elapsedMs)
    {
        TokenManagerKey = tokenManagerKey;
        Success = success;
        IsFallback = isFallback;
        ElapsedMs = elapsedMs;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>缓存命中/未命中事件的诊断负载。</summary>
public sealed class CacheDiagnosticPayload
{
    public string? Key { get; }
    public bool Hit { get; }
    public DateTimeOffset Timestamp { get; }

    public CacheDiagnosticPayload(string? key, bool hit)
    {
        Key = key;
        Hit = hit;
        Timestamp = DateTimeOffset.UtcNow;
    }
}
