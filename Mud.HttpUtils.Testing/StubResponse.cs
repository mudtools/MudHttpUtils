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

    internal void IncrementCallCount() => Interlocked.Increment(ref _callCount);
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
}
