// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text;

namespace Mud.HttpUtils.Helpers;

/// <summary>
/// 限量读取工具：在 <b>读取阶段</b> 施加上限，而不是读入内存后再截断。
/// </summary>
/// <remarks>
/// 供 <c>EnhancedHttpClient</c>（错误响应体）与 <c>DefaultHttpRequestExecutor</c>（错误响应体）
/// 两条执行路径共用，确保 <c>MaxExceptionContentLength</c> 语义一致：
/// <list type="bullet">
/// <item><c>maxChars &lt;= 0</c> 表示不限制；</item>
/// <item>按 <b>字符</b> 读取（多字节字符不会被切断），并自动剥离 UTF-8 BOM；</item>
/// <item>无论响应是否携带 <c>Content-Length</c>（chunked 场景），都不会读取超过上限的字符；</item>
/// <item>截断时在末尾追加 <c>...[已截断]</c> 标记。</item>
/// </list>
/// </remarks>
internal static class LimitedContentReader
{
    private const string TruncatedSuffix = "...[已截断]";
    private const int BufferSize = 4096;

    /// <summary>
    /// 以字符数上限读取 <see cref="HttpContent"/> 的字符串形式。
    /// </summary>
    /// <param name="content">要读取的 HTTP 内容。</param>
    /// <param name="maxChars">最大字符数；<c>0</c> 或负数表示不限制。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>读取到的内容与是否发生截断。截断时内容末尾已带 <c>...[已截断]</c> 后缀。</returns>
    public static async Task<(string Content, bool Truncated)> ReadLimitedStringAsync(
        HttpContent content,
        int maxChars,
        CancellationToken cancellationToken)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        // 快路径：已知长度为 0 的内容（如 204/304 或显式空体）
        if (content.Headers.ContentLength is 0)
            return (string.Empty, false);

        var stream = await GetContentStreamAsync(content, cancellationToken).ConfigureAwait(false);
        // 注意：不 dispose 该流 —— ReadAsStreamAsync 返回的是 HttpContent 自有流
        // （已缓冲内容复用内部 MemoryStream），生命周期归 HttpContent / HttpResponseMessage 所有。
        // 可 seek 时复位到 0 读取，保证"同一内容可多次限量读取"（重放语义，
        // 克隆重试 / 请求体捕获 / Debug 日志多次读取都依赖该语义）。
        if (stream.CanSeek && stream.Position != 0)
            stream.Position = 0;

        // 不限制：直接读取（保持原语义，避免 StreamReader 逐字符慢路径）
        if (maxChars <= 0)
        {
#if NET6_0_OR_GREATER
            var full = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var full = await content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            return (full ?? string.Empty, false);
        }

        // 限量：按字符读取，UTF-8 + BOM 探测（顺带解决 BOM 隐患，见 #29）
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            BufferSize,
            leaveOpen: true);

        var buffer = new char[maxChars];
        var totalRead = 0;
        while (totalRead < maxChars)
        {
#if NET6_0_OR_GREATER
            var read = await reader.ReadAsync(buffer.AsMemory(totalRead, maxChars - totalRead), cancellationToken).ConfigureAwait(false);
#else
            var read = await reader.ReadAsync(buffer, totalRead, maxChars - totalRead).ConfigureAwait(false);
#endif
            if (read == 0) break;
            totalRead += read;
        }

        if (totalRead < maxChars)
        {
            ResetIfSeekable(stream);
            return (new string(buffer, 0, totalRead), false);   // 内容不足上限，未截断
        }

        // 读取上限后再探测 1 个字符，判断是否截断
        var probe = new char[1];
#if NET6_0_OR_GREATER
        var extra = await reader.ReadAsync(buffer: probe, cancellationToken).ConfigureAwait(false);
#else
        var extra = await reader.ReadAsync(probe, 0, 1).ConfigureAwait(false);
#endif
        ResetIfSeekable(stream);
        return extra > 0
            ? (new string(buffer, 0, totalRead) + TruncatedSuffix, true)
            : (new string(buffer, 0, totalRead), false);
    }

    /// <summary>读取完成后把可 seek 的内容流复位到 0，保持 HttpContent 可被后续读取（重放语义）。</summary>
    private static void ResetIfSeekable(Stream stream)
    {
        if (stream.CanSeek && stream.Position != 0)
            stream.Position = 0;
    }

    private static async Task<Stream> GetContentStreamAsync(HttpContent content, CancellationToken cancellationToken)
    {
#if NET5_0_OR_GREATER
        return await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        // netstandard2.0 无 ReadAsStreamAsync(ct) 重载，取消经外层 StreamReader 读取时生效
        return await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// 以字节数上限缓冲 <see cref="HttpContent"/>（用于请求克隆前的限量拷贝）。
    /// </summary>
    /// <param name="content">要缓冲的 HTTP 内容。</param>
    /// <param name="maxBytes">最大字节数（&lt; 0 表示不限制）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缓冲的字节与是否达到/超过上限。达到上限时调用方应视为"内容超过限制"。</returns>
    public static async Task<(byte[] Bytes, bool Exceeded)> BufferUpToAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        if (maxBytes < 0)
        {
            var all = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return (all, false);
        }

        var stream = await GetContentStreamAsync(content, cancellationToken).ConfigureAwait(false);
        // 注意：不 dispose 该流 —— HttpContent 自有流，生命周期归 HttpContent 所有。
        // 可 seek 时复位到 0 读取（同 ReadLimitedStringAsync 的重放语义说明）。
        if (stream.CanSeek && stream.Position != 0)
            stream.Position = 0;

        var limit = maxBytes == long.MaxValue ? maxBytes : maxBytes + 1;
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        while (total < limit)
        {
            var toRead = (int)Math.Min(buffer.Length, limit - total);
            int read;
#if NETSTANDARD2_0
            read = await stream.ReadAsync(buffer, 0, toRead, cancellationToken).ConfigureAwait(false);
#else
            read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
#endif
            if (read == 0) break;
            await ms.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            total += read;
        }

        ResetIfSeekable(stream);
        return total > maxBytes
            ? (ms.ToArray(), true)
            : (ms.ToArray(), false);
    }
}
