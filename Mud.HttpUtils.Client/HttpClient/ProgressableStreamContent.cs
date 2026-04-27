namespace Mud.HttpUtils;

/// <summary>
/// 支持上传进度报告的HTTP内容包装器。
/// </summary>
/// <remarks>
/// <para>此类继承自 <see cref="HttpContent"/>,用于包装现有的 <see cref="HttpContent"/> 实例,
/// 并在数据上传过程中提供进度报告功能。</para>
/// <para>主要用途:</para>
/// <list type="bullet">
///   <item>大文件上传时显示进度条</item>
///   <item>监控HTTP请求体的上传进度</item>
///   <item>实现上传速度计算和预计剩余时间</item>
/// </list>
/// <para>此类通过读取内部内容的流,并在每次写入时报告已传输的字节数来实现进度跟踪。</para>
/// </remarks>
/// <example>
/// <code>
/// var progress = new Progress&lt;long&gt;(bytes =&gt; 
///     Console.WriteLine($"已上传: {bytes} 字节"));
/// 
/// using var fileContent = new StreamContent(File.OpenRead("largefile.zip"));
/// using var progressContent = new ProgressableStreamContent(fileContent, progress);
/// 
/// var response = await httpClient.PostAsync("/upload", progressContent);
/// </code>
/// </example>
/// <seealso cref="HttpContent"/>
/// <seealso cref="IProgress{T}"/>
public class ProgressableStreamContent : HttpContent
{
    private const int DefaultBufferSize = 4096;

    private readonly HttpContent _content;
    private readonly int _bufferSize;
    private readonly IProgress<long>? _progress;

    /// <summary>
    /// 初始化 <see cref="ProgressableStreamContent"/> 类的新实例。
    /// </summary>
    /// <param name="content">要包装的HTTP内容。</param>
    /// <param name="progress">进度报告回调。如果为 null,则不报告进度。</param>
    /// <param name="bufferSize">缓冲区大小(字节),默认为4096。</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> 为 null。</exception>
    /// <remarks>
    /// 构造函数会复制原始内容的所有头部信息到包装器中。
    /// </remarks>
    public ProgressableStreamContent(HttpContent content, IProgress<long>? progress, int bufferSize = DefaultBufferSize)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _progress = progress;
        _bufferSize = bufferSize;

        foreach (var header in _content.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    /// <summary>
    /// 将HTTP内容序列化到流中,并在传输过程中报告进度。
    /// </summary>
    /// <param name="stream">目标流,内容将被写入此流。</param>
    /// <param name="context">传输上下文。</param>
    /// <returns>表示异步序列化操作的任务。</returns>
    /// <remarks>
    /// 此方法通过缓冲区读取内部内容的流,并在每次写入目标流后报告累计已传输的字节数。
    /// 进度报告通过 <see cref="IProgress{T}.Report"/> 方法实现。
    /// </remarks>
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[_bufferSize];
        long totalBytesRead = 0;

        using var contentStream = await _content.ReadAsStreamAsync().ConfigureAwait(false);

        while (true)
        {
#if NETSTANDARD2_0
            var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#else
            var bytesRead = await contentStream.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
#endif

            if (bytesRead == 0)
                break;

            totalBytesRead += bytesRead;

#if NETSTANDARD2_0
            await stream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
#else
            await stream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
#endif

            _progress?.Report(totalBytesRead);
        }
    }

    /// <summary>
    /// 尝试计算内容的长度。
    /// </summary>
    /// <param name="length">当返回时,包含内容的长度(如果可计算);否则为 -1。</param>
    /// <returns>如果可以计算内容长度,则为 <c>true</c>;否则为 <c>false</c>。</returns>
    /// <remarks>
    /// 此方法从内部内容的 <see cref="HttpContentHeaders.ContentLength"/> 头部获取长度信息。
    /// 如果内部内容未指定 Content-Length,则返回 <c>false</c>。
    /// </remarks>
    protected override bool TryComputeLength(out long length)
    {
        length = _content.Headers.ContentLength ?? -1;
        return length != -1;
    }

    /// <summary>
    /// 释放由 <see cref="HttpContent"/> 使用的非托管资源,并可选择释放托管资源。
    /// </summary>
    /// <param name="disposing">如果为 <c>true</c>,则释放托管和非托管资源;如果为 <c>false</c>,则仅释放非托管资源。</param>
    /// <remarks>
    /// 此方法会处置内部包装的 <see cref="HttpContent"/> 实例。
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content.Dispose();
        }
        base.Dispose(disposing);
    }
}
