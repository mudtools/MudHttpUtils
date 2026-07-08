// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Observability;

/// <summary>
/// Mud.HttpUtils 专用 DiagnosticListener 单例。
/// 在关键路径（请求/重试/熔断/令牌刷新/缓存）写入事件，外部 APM 探针可订阅。
/// 无订阅者时 <see cref="HasSubscribers"/> 返回 false，调用方应先检查再构造事件负载，避免无谓分配。
/// </summary>
public sealed class MudHttpDiagnosticListener : DiagnosticListener
{
    /// <summary>
    /// 单例实例。延迟初始化，第一次访问时创建并自动注册到全局 DiagnosticListener.AllListeners。
    /// </summary>
    public static MudHttpDiagnosticListener Instance { get; } = new();

    private MudHttpDiagnosticListener() : base(MudHttpDiagnosticNames.ListenerName)
    {
    }

    /// <summary>
    /// 是否有任何订阅者（按事件名称过滤）。
    /// 调用方在构造事件负载前应先检查此属性，避免无订阅者时的分配。
    /// </summary>
    public bool HasSubscribers => IsEnabled();

    /// <summary>
    /// 当有订阅者时写入事件，否则跳过。
    /// </summary>
    /// <param name="eventName">事件名称（来自 <see cref="MudHttpDiagnosticNames"/>）。</param>
    /// <param name="payloadFactory">事件负载工厂，仅在存在订阅者时调用。</param>
    public void WriteIfEnabled(string eventName, Func<object?> payloadFactory)
    {
        if (!IsEnabled(eventName))
            return;

        try
        {
            var payload = payloadFactory();
            Write(eventName, payload);
        }
        catch
        {
            // DiagnosticSource 写入失败不应影响业务路径
        }
    }
}
