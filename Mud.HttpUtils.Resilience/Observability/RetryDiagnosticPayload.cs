// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience.Observability;

/// <summary>
/// 重试发生事件的诊断负载。
/// </summary>
public sealed class RetryDiagnosticPayload
{
    /// <summary>策略键。</summary>
    public string PolicyKey { get; }

    /// <summary>重试次数（第几次重试）。</summary>
    public int RetryCount { get; }

    /// <summary>本次重试延迟（毫秒）。</summary>
    public double DelayMs { get; }

    /// <summary>触发重试的异常类型名（可能为 null）。</summary>
    public string? ExceptionType { get; }

    /// <summary>事件时间戳（UTC）。</summary>
    public DateTimeOffset Timestamp { get; }

    public RetryDiagnosticPayload(string policyKey, int retryCount, double delayMs, string? exceptionType)
    {
        PolicyKey = policyKey;
        RetryCount = retryCount;
        DelayMs = delayMs;
        ExceptionType = exceptionType;
        Timestamp = DateTimeOffset.UtcNow;
    }
}
