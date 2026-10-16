// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Observability;

namespace Mud.HttpUtils;

// -------------------------------------------------------------------------------------------------
//  可观测性：RefreshFailed 事件分发（订阅者异常隔离）与指标 / Activity / DiagnosticSource 记录
//  TokenManagerBase partial: shares all private state with the main file.
// -------------------------------------------------------------------------------------------------
public abstract partial class TokenManagerBase
{
    /// <summary>
    /// 触发令牌刷新失败事件。
    /// NEW-TM-04 修复：捕获订阅者异常，避免掩盖原始令牌刷新失败的根因。
    /// </summary>
    /// <param name="e">事件参数。</param>
    private void OnRefreshFailed(TokenRefreshFailedEventArgs e)
    {
        try
        {
            RefreshFailed?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            // 订阅者异常不应掩盖原始刷新失败，仅记录
            System.Diagnostics.Debug.WriteLine($"TokenManagerBase: RefreshFailed 订阅者抛出异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 记录令牌刷新指标（无锁零分配）。
    /// </summary>
    private void RecordTokenRefresh(bool success, string? tokenManagerKey, double elapsedMs, bool isFallback)
    {
        var outcome = success
            ? (isFallback ? "fallback" : "success")
            : "failure";

        var tmKey = tokenManagerKey ?? "(unknown)";

        // 指标 tag 白名单过滤
        var refreshTags = MudHttpMeter.FilterTags(
            new KeyValuePair<string, object?>[] { new("token_manager_key", tmKey), new("outcome", outcome) });
        MudHttpMeter.TokenRefreshCounter.Add(1, refreshTags);

        MudHttpMeter.TokenRefreshDuration.Record(elapsedMs, MudHttpMeter.FilterTags(
            new KeyValuePair<string, object?>[] { new("token_manager_key", tmKey) }));

        // 同步写入无锁统计收集器，供健康检查使用
        TokenRefreshStatsCollector.Record(success, tokenManagerKey, elapsedMs, isFallback);

        // 将令牌管理器键写入当前 Activity tag（仅 Mud Activity）
        var activity = Activity.Current;
        if (activity != null && MudHttpActivitySource.IsMudActivity(activity))
            activity.SetTag(MudHttpActivitySource.Tags.MudTokenManagerKey, tmKey);

        // 写入 DiagnosticSource 事件 + Activity Event（G28：门控前移 + 惰性 tags 工厂）
        if (MudHttpActivitySource.EventsEnabled)
        {
            MudHttpActivitySource.AddActivityEvent(
                MudHttpDiagnosticNames.TokenRefreshed,
                () => new TokenRefreshDiagnosticPayload(tokenManagerKey, success, isFallback, elapsedMs),
                MudHttpDiagnosticNames.TokenRefreshed,
                () => new[]
                {
                    new KeyValuePair<string, object?>("token_manager_key", tmKey),
                    new KeyValuePair<string, object?>("success", success),
                    new KeyValuePair<string, object?>("is_fallback", isFallback),
                    new KeyValuePair<string, object?>("elapsed_ms", elapsedMs),
                });
        }
    }
}
