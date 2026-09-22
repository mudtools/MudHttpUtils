// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;

namespace Mud.HttpUtils;

/// <summary>
/// 表示 HTTP API 调用失败时抛出的异常。
/// </summary>
/// <remarks>
/// <para>
/// 当 HTTP 响应的状态码表示错误（4xx 或 5xx）时，如果方法未标记
/// <c>AllowAnyStatusCodeAttribute</c>（定义于 Attributes 程序集）且返回类型不是 <see cref="Response{T}"/>，
/// 则会抛出此异常。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// try
/// {
///     var user = await api.GetUserAsync(1);
/// }
/// catch (ApiException ex)
/// {
///     Console.WriteLine($"请求失败: {ex.StatusCode}");
///     Console.WriteLine($"请求URI: {ex.RequestUri}");
///     Console.WriteLine($"错误内容: {ex.Content}");
/// }
/// </code>
/// </example>
public class ApiException : HttpRequestException
{
    /// <summary>
    /// 初始化 <see cref="ApiException"/> 类的新实例。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="content">响应内容。</param>
    public ApiException(HttpStatusCode statusCode, string? content)
#if NET5_0_OR_GREATER
        : base($"HTTP request failed with status code {(int)statusCode} ({statusCode}).", null, statusCode)
#else
        : base($"HTTP request failed with status code {(int)statusCode} ({statusCode}).")
#endif
    {
        StatusCode = statusCode;
        Content = content;
    }

    /// <summary>
    /// 初始化 <see cref="ApiException"/> 类的新实例。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="content">响应内容。</param>
    /// <param name="requestUri">请求 URI。</param>
    /// <remarks>
    /// FIX-12：异常消息中的 URI 仅保留 scheme://host/path（不含 query），防止敏感查询参数（如 access_token）随 Message 泄漏。
    /// M6-HC-21：<see cref="RequestUri"/> 由各构造点在传入前统一经 <c>SensitiveUrlRedactor</c> 脱敏
    /// （剥离 userinfo、掩码敏感 query 值），与 <see cref="ApiRequestException"/> 口径一致；
    /// 仍可由 <see cref="IExceptionRedactor"/> 在传播前进一步擦除。
    /// </remarks>
    public ApiException(HttpStatusCode statusCode, string? content, string? requestUri)
#if NET5_0_OR_GREATER
        : base(BuildMessage(statusCode, requestUri), null, statusCode)
#else
        : base(BuildMessage(statusCode, requestUri))
#endif
    {
        StatusCode = statusCode;
        Content = content;
        RequestUri = requestUri;
    }

    /// <summary>
    /// 初始化 <see cref="ApiException"/> 类的新实例。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="content">响应内容。</param>
    /// <param name="innerException">内部异常。</param>
    public ApiException(HttpStatusCode statusCode, string? content, Exception innerException)
#if NET5_0_OR_GREATER
        : base($"HTTP request failed with status code {(int)statusCode} ({statusCode}).", innerException, statusCode)
#else
        : base($"HTTP request failed with status code {(int)statusCode} ({statusCode}).", innerException)
#endif
    {
        StatusCode = statusCode;
        Content = content;
    }

    /// <summary>
    /// 初始化 <see cref="ApiException"/> 类的新实例。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="content">响应内容。</param>
    /// <param name="requestUri">请求 URI。</param>
    /// <param name="innerException">内部异常。</param>
    /// <remarks>
    /// FIX-12：异常消息中的 URI 仅保留 scheme://host/path（不含 query），防止敏感查询参数泄漏。
    /// </remarks>
    public ApiException(HttpStatusCode statusCode, string? content, string? requestUri, Exception innerException)
#if NET5_0_OR_GREATER
        : base(BuildMessage(statusCode, requestUri), innerException, statusCode)
#else
        : base(BuildMessage(statusCode, requestUri), innerException)
#endif
    {
        StatusCode = statusCode;
        Content = content;
        RequestUri = requestUri;
    }

    /// <summary>
    /// 供运输层异常子类（如 <see cref="ApiRequestException"/>）使用的受保护构造函数。
    /// 运输层失败无 HTTP 状态码，故不要求 <c>statusCode</c>，<see cref="StatusCode"/> 取默认值 0。
    /// </summary>
    /// <param name="message">异常消息。</param>
    /// <param name="requestUri">请求 URI。</param>
    /// <param name="innerException">内部异常（通常为原始运输层异常）。</param>
    protected ApiException(string message, string? requestUri, Exception? innerException)
        : base(message, innerException)
    {
        RequestUri = requestUri;
    }

    /// <summary>
    /// FIX-12：构建异常消息，对 URI 进行脱敏（仅保留 scheme://host/path，不含 query）。
    /// 防止敏感查询参数（如 access_token、api_key）随异常消息泄漏到日志和调用方。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="requestUri">请求 URI（可能含敏感 query 参数）。</param>
    /// <returns>脱敏后的异常消息。</returns>
    private static string BuildMessage(HttpStatusCode statusCode, string? requestUri)
    {
        var redactedUri = RedactUri(requestUri);
        return string.IsNullOrEmpty(redactedUri)
            ? $"HTTP request failed with status code {(int)statusCode} ({statusCode})."
            : $"HTTP request failed with status code {(int)statusCode} ({statusCode}) for request: {redactedUri}.";
    }

    /// <summary>
    /// FIX-12：对 URI 进行脱敏——仅保留 scheme://host/path，不含 query 部分。
    /// query 中可能包含 access_token、api_key 等敏感参数，直接嵌入异常消息会造成信息泄漏。
    /// </summary>
    /// <param name="uri">原始 URI 字符串。</param>
    /// <returns>脱敏后的 URI（无 query 部分）；无效 URI 原样返回以保持排障信息。</returns>
    private static string? RedactUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
            return uri;

        // 尝试移除 query 部分（? 及之后的所有内容）
        var qIndex = uri!.IndexOf('?');
        if (qIndex >= 0)
            return uri.Substring(0, qIndex);

        return uri;
    }

    /// <summary>
    /// 获取 HTTP 状态码。
    /// </summary>
    /// <remarks>
    /// 在 NET 5+ 上，此属性使用 <c>new</c> 关键字隐藏基类 <c>HttpRequestException.StatusCode</c>（nullable），
    /// 提供 non-nullable 版本。两者返回相同的逻辑值。
    /// </remarks>
#if NET5_0_OR_GREATER
    public new HttpStatusCode StatusCode { get; }
#else
    public HttpStatusCode StatusCode { get; }
#endif

    /// <summary>
    /// 获取或设置响应内容。
    /// </summary>
    /// <remarks>
    /// setter 供 <see cref="IExceptionRedactor"/> 在异常传播前清除敏感数据。
    /// </remarks>
    public string? Content { get; set; }

    /// <summary>
    /// 获取或设置请求 URI。
    /// </summary>
    /// <remarks>
    /// setter 供 <see cref="IExceptionRedactor"/> 在异常传播前清除敏感数据。
    /// </remarks>
    public string? RequestUri { get; set; }

    /// <summary>
    /// 获取或设置请求体内容（捕获发送前的请求体字符串）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 仅在 <c>CaptureRequestContent</c> 启用时填充。请求体常含凭据或 PII，
    /// 应配合 <see cref="IExceptionRedactor"/> 在传播前擦除。
    /// </para>
    /// </remarks>
    public string? RequestContent { get; set; }

    /// <summary>
    /// 获取一个值，指示是否已捕获请求内容。
    /// </summary>
    public bool HasRequestContent => !string.IsNullOrEmpty(RequestContent);

    /// <summary>
    /// 尝试将响应内容反序列化为指定类型。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="deserialize">反序列化函数，接受 JSON 字符串并返回反序列化的对象。</param>
    /// <param name="result">反序列化结果。如果反序列化失败，则为 <c>default</c>。</param>
    /// <returns>如果反序列化成功，则为 <c>true</c>；否则为 <c>false</c>。</returns>
    public bool TryDeserializeContent<T>(Func<string, T?> deserialize, out T? result)
    {
        result = default;

        if (string.IsNullOrEmpty(Content) || deserialize == null)
            return false;

        try
        {
            result = deserialize(Content!);
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 将响应内容反序列化为指定类型。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="deserialize">反序列化函数，接受 JSON 字符串并返回反序列化的对象。</param>
    /// <returns>反序列化的结果。</returns>
    /// <exception cref="InvalidOperationException">当内容为空或反序列化函数为 null 时抛出。</exception>
    public T? DeserializeContent<T>(Func<string, T?> deserialize)
    {
        if (string.IsNullOrEmpty(Content))
            throw new InvalidOperationException("Cannot deserialize null or empty content.");

        if (deserialize == null)
            throw new ArgumentNullException(nameof(deserialize));

        return deserialize(Content!);
    }
}
