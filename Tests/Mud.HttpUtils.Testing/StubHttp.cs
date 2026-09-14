// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Mud.HttpUtils.Testing;

/// <summary>
/// Mock HTTP 服务器，基于 <see cref="HttpMessageHandler"/> 实现请求拦截与响应配置。
/// </summary>
/// <remarks>
/// 用于单元测试中模拟 HTTP 服务器响应，无需真实网络请求。
/// </remarks>
public sealed class StubHttp : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, List<StubResponse>> _routes = new();
    private readonly List<StubResponse> _catchAll = new();

    /// <summary>
    /// 配置指定路由的响应。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">路由路径（如 <c>/api/users/{id}</c>）。</param>
    /// <param name="statusCode">响应状态码。</param>
    /// <param name="content">响应内容。</param>
    /// <param name="contentType">Content-Type。</param>
    /// <returns>配置的 <see cref="StubResponse"/> 实例，可链式配置头等。</returns>
    public StubResponse Respond(HttpMethod method, string path,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string? content = null,
        string contentType = "application/json")
    {
        var response = new StubResponse(method, path, statusCode, content, contentType);
        var key = RouteMatcher.BuildKey(method, path);
        _routes.AddOrUpdate(key, [response], (_, list) => { list.Add(response); return list; });
        return response;
    }

    /// <summary>
    /// 配置捕获所有未匹配请求的默认响应。
    /// </summary>
    public StubResponse RespondToAnyRequest(HttpStatusCode statusCode = HttpStatusCode.OK,
        string? content = null,
        string contentType = "application/json")
    {
        var response = new StubResponse(null, "*", statusCode, content, contentType);
        _catchAll.Add(response);
        return response;
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestPath = request.RequestUri?.AbsolutePath ?? "/";
        StubResponse? matched = null;

        // 遍历所有路由，支持精确匹配和模板参数匹配（如 /api/users/{id}）
        foreach (var kvp in _routes)
        {
            var responses = kvp.Value;
            // 尝试精确匹配
            var exactKey = RouteMatcher.BuildKey(request.Method, requestPath);
            if (kvp.Key == exactKey)
            {
                matched = responses.FirstOrDefault(r => r.IsValid);
                break;
            }
            // 尝试模板匹配（路由路径含 {param} 占位符）
            foreach (var resp in responses)
            {
                if (resp.IsValid && RouteMatcher.MatchPath(resp.Path, requestPath)
                    && (resp.Method == null || resp.Method == request.Method))
                {
                    matched = resp;
                    break;
                }
            }
            if (matched != null) break;
        }

        if (matched != null)
        {
            matched.IncrementCallCount();
        }

        // Fallback 到 catch-all
        matched ??= _catchAll.FirstOrDefault(r => r.IsValid);

        if (matched == null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No stub response configured for {request.Method} {request.RequestUri?.AbsolutePath}")
            };
        }

        // 模拟网络延迟（如已配置）
        if (matched.DelayMs > 0)
        {
            await Task.Delay(matched.DelayMs, cancellationToken).ConfigureAwait(false);
        }

        // 模拟真实 HttpClient 行为：响应携带原始请求引用（ApiException.RequestContent 等依赖 RequestMessage）
        var responseMessage = new HttpResponseMessage(matched.StatusCode)
        {
            RequestMessage = request,
            Version = request.Version,
        };

        if (matched.StreamFactory != null)
        {
            // 流式响应：不设置 Content-Length（模拟 chunked 传输），框架按需读取
            var stream = matched.StreamFactory();
            responseMessage.Content = new StreamContentWithoutLength(stream, matched.ContentType);
        }
        else if (!string.IsNullOrEmpty(matched.Content))
        {
            responseMessage.Content = new StringContent(matched.Content, System.Text.Encoding.UTF8, matched.ContentType);
        }

        // 添加配置的头
        foreach (var header in matched.Headers)
        {
            responseMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return responseMessage;
    }

    /// <summary>
    /// 清除所有已配置的路由。
    /// </summary>
    public void Clear()
    {
        _routes.Clear();
        _catchAll.Clear();
    }

    /// <summary>
    /// 创建一个感知 <see cref="CancellationToken"/> 的上传流（读取方尊重取消令牌时抛
    /// <see cref="OperationCanceledException"/>），用于测试上传取消传播（如 <c>ProgressableStreamContent</c>）。
    /// </summary>
    /// <param name="totalBytes">流总字节数。</param>
    /// <param name="chunkSize">每块字节数（默认 8192）。</param>
    /// <param name="emitBytesBeforeCancelSignal">
    /// 返回前产出的字节数（模拟"已发送部分数据后才收到取消"）。达到该字节数后，流在 <paramref name="cancelAfter"/> 时间内保持可读，
    /// 若取消令牌在期间触发则抛 <see cref="OperationCanceledException"/>。
    /// </param>
    /// <param name="cancelAfter">可读保持时长。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static Stream CreateCancellableUploadStream(
        long totalBytes,
        int chunkSize = 8192,
        long emitBytesBeforeCancelSignal = 0,
        TimeSpan? cancelAfter = null,
        CancellationToken cancellationToken = default)
        => new CancellableUploadStream(totalBytes, chunkSize, emitBytesBeforeCancelSignal, cancelAfter, cancellationToken);
}

/// <summary>
/// 不设置 Content-Length 的流式 HttpContent（框架读取时按 chunked 语义处理）。
/// </summary>
internal sealed class StreamContentWithoutLength : HttpContent
{
    private readonly Stream _stream;
    private readonly string _contentType;

    internal StreamContentWithoutLength(Stream stream, string contentType)
    {
        _stream = stream;
        _contentType = contentType;
        Headers.TryAddWithoutValidation("Content-Type", contentType);
    }

    // 不重写 TryComputeLength → Content-Length 保持缺失（chunked 语义）
    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[81920];
        int bytesRead;
#if NETSTANDARD2_0
        while ((bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
            await stream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
#else
        while ((bytesRead = await _stream.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
            await stream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
#endif
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _stream.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// 感知取消的上传流：产出 <see cref="_emitBeforeCancel"/> 字节后，若取消令牌在 <see cref="_readWindow"/> 内触发
/// 则抛 <see cref="OperationCanceledException"/>；否则持续返回数据直至读满。
/// </summary>
internal sealed class CancellableUploadStream : Stream
{
    private readonly long _totalBytes;
    private readonly byte[] _chunk;
    private readonly long _emitBeforeCancel;
    private readonly TimeSpan _readWindow;
    private readonly CancellationToken _cancellationToken;
    private long _position;
    private bool _cancelWindowEntered;

    public CancellableUploadStream(long totalBytes, int chunkSize, long emitBytesBeforeCancel, TimeSpan? readWindow, CancellationToken cancellationToken)
    {
        _totalBytes = totalBytes;
        _chunk = new byte[chunkSize];
        for (var i = 0; i < chunkSize; i++)
            _chunk[i] = (byte)'u';
        _emitBeforeCancel = emitBytesBeforeCancel;
        _readWindow = readWindow ?? TimeSpan.FromSeconds(30);
        _cancellationToken = cancellationToken;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _totalBytes;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        // 先产出"已发送"部分
        if (_position < _emitBeforeCancel)
        {
            var toCopy = (int)Math.Min(Math.Min(count, _chunk.Length), _emitBeforeCancel - _position);
            Array.Copy(_chunk, 0, buffer, offset, toCopy);
            _position += toCopy;
            return toCopy;
        }

        // 进入取消窗口：等待取消令牌或窗口超时
        if (!_cancelWindowEntered)
        {
            _cancelWindowEntered = true;
            _cancellationToken.ThrowIfCancellationRequested();
            var remaining = _readWindow;
            while (remaining > TimeSpan.Zero)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                System.Threading.Thread.Sleep(10);
                remaining -= TimeSpan.FromMilliseconds(10);
            }
        }

        // 窗口内未取消 → 正常产出剩余数据
        var left = _totalBytes - _position;
        if (left <= 0) return 0;
        var copy = (int)Math.Min(Math.Min(count, _chunk.Length), left);
        Array.Copy(_chunk, 0, buffer, offset, copy);
        _position += copy;
        return copy;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
