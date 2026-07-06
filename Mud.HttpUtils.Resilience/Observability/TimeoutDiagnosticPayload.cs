// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience.Observability;

/// <summary>
/// 请求超时事件的诊断负载。
/// </summary>
public sealed class TimeoutDiagnosticPayload
{
    /// <summary>策略键。</summary>
    public string PolicyKey { get; }

    /// <summary>超时时长（毫秒）。</summary>
    public double TimeoutMs { get; }

    /// <summary>事件时间戳（UTC）。</summary>
    public DateTimeOffset Timestamp { get; }

    public TimeoutDiagnosticPayload(string policyKey, double timeoutMs)
    {
        PolicyKey = policyKey;
        TimeoutMs = timeoutMs;
        Timestamp = DateTimeOffset.UtcNow;
    }
}
