// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;

namespace Mud.HttpUtils.Testing;

/// <summary>
/// 配置 Mock HTTP 响应（状态码、内容、头、调用限制等）。
/// </summary>
public sealed class StubResponse
{
    private int _callCount;
    private int? _maxCalls;

    /// <summary>HTTP 方法（null = 匹配任意方法）。</summary>
    public HttpMethod? Method { get; }

    /// <summary>路由路径。</summary>
    public string Path { get; }

    /// <summary>响应状态码。</summary>
    public HttpStatusCode StatusCode { get; set; }

    /// <summary>响应内容。</summary>
    public string? Content { get; set; }

    /// <summary>Content-Type。</summary>
    public string ContentType { get; set; }

    /// <summary>响应头。</summary>
    public Dictionary<string, string> Headers { get; } = new();

    /// <summary>模拟延迟（毫秒）。</summary>
    public int DelayMs { get; set; }

    /// <summary>网络行为模拟器（可选）。</summary>
    public NetworkBehavior? Behavior { get; set; }

    /// <summary>已调用次数。</summary>
    public int CallCount => _callCount;

    /// <summary>是否仍然有效（未达到调用次数限制）。</summary>
    public bool IsValid => _maxCalls == null || _callCount < _maxCalls.Value;

    internal StubResponse(HttpMethod? method, string path,
        HttpStatusCode statusCode, string? content, string contentType)
    {
        Method = method;
        Path = path;
        StatusCode = statusCode;
        Content = content;
        ContentType = contentType;
    }

    /// <summary>设置最大调用次数（达到后此响应不再匹配）。</summary>
    public StubResponse WithMaxCalls(int max)
    {
        _maxCalls = max;
        return this;
    }

    /// <summary>添加响应头。</summary>
    public StubResponse WithHeader(string name, string value)
    {
        Headers[name] = value;
        return this;
    }

    /// <summary>设置模拟延迟。</summary>
    public StubResponse WithDelay(int milliseconds)
    {
        DelayMs = milliseconds;
        return this;
    }

    /// <summary>附加网络行为模拟器（延迟、丢包等）。</summary>
    public StubResponse WithBehavior(NetworkBehavior behavior)
    {
        Behavior = behavior;
        if (behavior.DelayMs > 0)
            DelayMs = behavior.DelayMs;
        return this;
    }

    /// <summary>
    /// 以流方式提供响应体：响应不携带 <c>Content-Length</c>（模拟 chunked 传输），流由框架按需读取。
    /// </summary>
    /// <param name="streamFactory">每次响应时创建新流的工厂（框架负责释放）。</param>
    /// <remarks>
    /// 用于测试"无 Content-Length 的响应体读取上限"（如 OOM 防护）与流式反序列化场景。
    /// 设置后 <see cref="Content"/> 字符串内容被忽略。
    /// </remarks>
    public StubResponse WithStreamContent(Func<Stream> streamFactory)
    {
        StreamFactory = streamFactory ?? throw new ArgumentNullException(nameof(streamFactory));
        return this;
    }

    /// <summary>
    /// 以惰性生成的字节流提供超大响应体：按 <paramref name="chunkSize"/> 分块写入，测试进程无需预先分配
    /// <paramref name="totalBytes"/> 大小的内存。响应不携带 <c>Content-Length</c>。
    /// </summary>
    /// <param name="totalBytes">响应体总字节数。</param>
    /// <param name="chunkSize">每块字节数（默认 81920）。</param>
    /// <param name="fillByte">填充字节值（默认 ASCII 'x'）。</param>
    public StubResponse WithLazyContent(long totalBytes, int chunkSize = 81920, byte fillByte = (byte)'x')
    {
        if (totalBytes < 0) throw new ArgumentOutOfRangeException(nameof(totalBytes));
        if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        StreamFactory = () => new RepeatingByteStream(totalBytes, chunkSize, fillByte);
        return this;
    }

    /// <summary>响应体流工厂（设置后优先于 <see cref="Content"/> 字符串）。</summary>
    internal Func<Stream>? StreamFactory { get; private set; }

    internal void IncrementCallCount() => Interlocked.Increment(ref _callCount);
}

/// <summary>
/// 按块重复产出填充字节的只读流，用于模拟超大响应体而不预分配全部内存。
/// </summary>
internal sealed class RepeatingByteStream : Stream
{
    private readonly long _totalBytes;
    private readonly byte[] _chunk;
    private long _position;

    public RepeatingByteStream(long totalBytes, int chunkSize, byte fillByte)
    {
        _totalBytes = totalBytes;
        _chunk = new byte[chunkSize];
        for (var i = 0; i < chunkSize; i++)
            _chunk[i] = fillByte;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _totalBytes;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _totalBytes - _position;
        if (remaining <= 0) return 0;
        var toCopy = (int)Math.Min(Math.Min(count, _chunk.Length), remaining);
        Array.Copy(_chunk, 0, buffer, offset, toCopy);
        _position += toCopy;
        return toCopy;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// 路由匹配器，构建路由键与匹配请求。
/// </summary>
internal static class RouteMatcher
{
    /// <summary>构建路由键（方法 + 路径）。</summary>
    internal static string BuildKey(HttpMethod? method, string path)
    {
        var methodStr = method?.Method ?? "*";
        var normalizedPath = path.TrimEnd('/');
        if (string.IsNullOrEmpty(normalizedPath)) normalizedPath = "/";
        return $"{methodStr} {normalizedPath}";
    }

    /// <summary>
    /// 检查请求路径是否匹配路由模板（支持 {param} 占位符）。
    /// 例如 "/api/users/{id}" 匹配 "/api/users/42"。
    /// </summary>
    /// <param name="routePath">路由模板路径。</param>
    /// <param name="requestPath">实际请求路径。</param>
    /// <returns>匹配则返回 true，否则 false。</returns>
    internal static bool MatchPath(string routePath, string requestPath)
    {
        var routeSegments = routePath.TrimEnd('/').Split('/');
        var requestSegments = requestPath.TrimEnd('/').Split('/');

        if (routeSegments.Length != requestSegments.Length)
            return false;

        for (int i = 0; i < routeSegments.Length; i++)
        {
            var routeSeg = routeSegments[i];
            var requestSeg = requestSegments[i];

            // 路由段为 {param} 占位符时匹配任意值
            if (routeSeg.StartsWith("{") && routeSeg.EndsWith("}"))
                continue;

            // 普通段需精确匹配（忽略大小写）
            if (!string.Equals(routeSeg, requestSeg, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
