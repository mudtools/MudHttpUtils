// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience;


/// <summary>
/// 超时策略配置选项。
/// </summary>
public class TimeoutOptions
{
    private int _timeoutSeconds = 30;

    /// <summary>
    /// 是否启用超时策略。默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 全局超时时间（秒）。默认 30。必须大于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => _timeoutSeconds = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "超时时间必须大于 0 秒。");
    }

    /// <summary>
    /// 流式枚举（SSE/NDJSON，<see cref="IAsyncEnumerable{T}"/>）的连接建立期超时（秒）。默认 0 = 禁用。
    /// </summary>
    /// <remarks>
    /// 流式读取阶段无法被 Polly 超时包装（IAsyncEnumerable 为拉取模型），本字段仅约束<b>首次
    /// MoveNextAsync（连接建立 + 首个元素产出）</b>：连接在限期内未建立即抛 OperationCanceledException；
    /// 首个元素一旦产出即解除限制，后续读取不受约束。场景类比 <see cref="HttpClient.Timeout"/>，
    /// 但仅作用于流式首元素，避免对长连接读取期间的无限期连接强制断路。
    /// </remarks>
    public int StreamConnectTimeoutSeconds { get; set; }
}
