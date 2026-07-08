// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Observability;

/// <summary>熔断器状态变更事件的诊断负载。</summary>
public sealed class CircuitBreakerDiagnosticPayload(string policyKey, string state)
{
    /// <summary>策略键。</summary>
    public string PolicyKey { get; } = policyKey;

    /// <summary>新状态名称（Open/HalfOpen/Closed）。</summary>
    public string State { get; } = state;

    /// <summary>事件时间戳（UTC）。</summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}

/// <summary>令牌刷新完成事件的诊断负载。</summary>
public sealed class TokenRefreshDiagnosticPayload(string? tokenManagerKey, bool success, bool isFallback, double elapsedMs)
{
    public string? TokenManagerKey { get; } = tokenManagerKey;
    public bool Success { get; } = success;
    public bool IsFallback { get; } = isFallback;
    public double ElapsedMs { get; } = elapsedMs;
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}

/// <summary>缓存命中/未命中事件的诊断负载。</summary>
public sealed class CacheDiagnosticPayload(string? key, bool hit)
{
    public string? Key { get; } = key;
    public bool Hit { get; } = hit;
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
}
