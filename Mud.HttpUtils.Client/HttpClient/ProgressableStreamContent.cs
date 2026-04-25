namespace Mud.HttpUtils;

public class ProgressableStreamContent : HttpContent
{
    private const int DefaultBufferSize = 4096;

    private readonly HttpContent _content;
    private readonly int _bufferSize;
    private readonly IProgress<long>? _progress;

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

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[_bufferSize];
        long totalBytesRead = 0;

#if NETSTANDARD2_0
        using var contentStream = await _content.ReadAsStreamAsync().ConfigureAwait(false);
#else
        using var contentStream = await _content.ReadAsStreamAsync().ConfigureAwait(false);
#endif

        while (true)
        {
#if NETSTANDARD2_0
            var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#else
            var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
#endif

            if (bytesRead == 0)
                break;

            totalBytesRead += bytesRead;
#if NETSTANDARD2_0
            await stream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
#else
            await stream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
#endif

            _progress?.Report(totalBytesRead);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _content.Headers.ContentLength ?? -1;
        return length != -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content.Dispose();
        }
        base.Dispose(disposing);
    }
}
