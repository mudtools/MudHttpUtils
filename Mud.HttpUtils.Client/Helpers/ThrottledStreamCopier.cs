// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Diagnostics;

namespace Mud.HttpUtils.Helpers;

/// <summary>
/// M6-HC-15：带时间节流的流复制 —— 下载/上传进度回调的统一实现。
/// </summary>
/// <remarks>
/// <para>
/// 若每个缓冲区（默认 81920 字节）都触发一次 <see cref="IProgress{T}.Report"/>，
/// GB 级传输每秒会产生数百至数千次回调，导致 UI 线程高频刷新、
/// <see cref="IProgress{T}"/> 投递排队堆积、日志海量输出。
/// </para>
/// <para>节流策略：首次立即上报 + 距上次上报超过 100ms 再上报 + 收尾补齐最终值（不重复上报相同值）。</para>
/// <para>
/// 使用 <see cref="Stopwatch.GetTimestamp"/> 而非 <c>Environment.TickCount64</c>，兼容 netstandard2.0。
/// </para>
/// </remarks>
internal static class ThrottledStreamCopier
{
    private const double ProgressReportIntervalMs = 100;

    /// <summary>
    /// 将 <paramref name="source"/> 复制到 <paramref name="destination"/>，并按 100ms 节流上报累计字节数。
    /// </summary>
    /// <param name="source">源流。</param>
    /// <param name="destination">目标流。</param>
    /// <param name="bufferSize">缓冲区大小（字节）。</param>
    /// <param name="progress">进度回调（不可为 null；无回调的调用方应直接使用 <c>CopyToAsync</c> 快路径）。</param>
    /// <param name="initialTotal">初始已传输字节数（用于拼接既有进度）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal static async Task CopyWithThrottledProgressAsync(
        Stream source,
        Stream destination,
        int bufferSize,
        IProgress<long> progress,
        long initialTotal,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[bufferSize];
        var total = initialTotal;
        var lastReportedValue = initialTotal;
        var hasReported = false;

        var timestampToMs = 1000.0 / Stopwatch.Frequency;
        var lastReportTimestamp = Stopwatch.GetTimestamp();

        while (true)
        {
#if NETSTANDARD2_0
            var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
#else
            var bytesRead = await source.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false);
#endif
            if (bytesRead == 0)
                break;

#if NETSTANDARD2_0
            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
#else
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
#endif

            total += bytesRead;

            // 首次立即上报，其后按 100ms 间隔节流
            var currentTimestamp = Stopwatch.GetTimestamp();
            if (!hasReported || (currentTimestamp - lastReportTimestamp) * timestampToMs >= ProgressReportIntervalMs)
            {
                progress.Report(total);
                lastReportedValue = total;
                hasReported = true;
                lastReportTimestamp = currentTimestamp;
            }
        }

        // 收尾补齐：节流可能吞掉最后一次上报；零字节内容不产生任何回调
        if (hasReported && total != lastReportedValue)
            progress.Report(total);
    }
}