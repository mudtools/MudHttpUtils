// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  同步记录型 IProgress<long>，消除进度类测试的投递竞态
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client.Tests;

/// <summary>
/// 同步记录型 <see cref="IProgress{T}"/>：把 <c>Report</c> 的值直接写入调用方提供的列表。
/// </summary>
/// <remarks>
/// <para>
/// BCL 的 <see cref="Progress{T}"/> 通过 <c>SynchronizationContext.Post</c>（或线程池）<b>异步</b>投递回调，
/// 而测试通常在 <c>CopyToAsync</c>/<c>DownloadAsync</c> 返回后立即断言，于是出现"回调还没投递完"的竞态，
/// 断言随线程池调度随机失败（实测出现过"期望 10 次报告、实际只收到 7 次"以及"列表为空"）。
/// </para>
/// <para>
/// 被测代码的实际契约是"调用了多少次 <c>Report</c>、最后一次的值是多少"，
/// 因此使用同步记录实现既保持语义、又消除投递竞态。
/// </para>
/// </remarks>
internal sealed class RecordingProgress : IProgress<long>
{
    private readonly List<long> _reports;

    /// <summary>初始化 <see cref="RecordingProgress"/> 实例。</summary>
    /// <param name="reports">用于接收进度值的列表。</param>
    public RecordingProgress(List<long> reports) => _reports = reports;

    /// <inheritdoc/>
    public void Report(long value) => _reports.Add(value);
}
