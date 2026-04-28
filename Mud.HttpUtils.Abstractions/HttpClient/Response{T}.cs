// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;

namespace Mud.HttpUtils;

/// <summary>
/// 表示 HTTP 响应的包装类型，同时提供响应内容和原始 HTTP 响应信息。
/// </summary>
/// <typeparam name="T">响应内容的类型。</typeparam>
/// <remarks>
/// <para>
/// 当方法返回类型为 <see cref="Response{T}"/> 时，即使响应状态码表示错误（如 4xx、5xx），
/// 也不会抛出异常，而是将状态码和错误信息存储在 <see cref="Response{T}"/> 对象中。
/// 这允许调用者自行决定如何处理错误响应。
/// </para>
/// <para>
/// 使用 <see cref="AllowAnyStatusCodeAttribute"/> 标记接口或方法时，
/// 返回类型应使用 <see cref="Response{T}"/> 以获取完整的响应信息。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [AllowAnyStatusCode]
/// [Get("/api/users/{id}")]
/// Task&lt;Response&lt;User&gt;&gt; GetUserAsync(int id);
/// 
/// // 使用示例
/// var response = await api.GetUserAsync(1);
/// if (response.StatusCode == HttpStatusCode.OK)
/// {
///     var user = response.Content; // 访问反序列化的内容
/// }
/// else
/// {
///     var error = response.ErrorContent; // 访问原始错误响应内容
/// }
/// </code>
/// </example>
public sealed class Response<T>
{
    /// <summary>
    /// 初始化表示成功响应的 <see cref="Response{T}"/> 实例。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="content">反序列化的响应内容。</param>
    /// <param name="rawContent">原始响应内容字符串。</param>
    /// <param name="responseHeaders">响应头。</param>
    public Response(HttpStatusCode statusCode, T? content, string? rawContent, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>? responseHeaders)
    {
        StatusCode = statusCode;
        Content = content;
        RawContent = rawContent;
        ResponseHeaders = responseHeaders;
        ErrorContent = null;
    }

    /// <summary>
    /// 初始化表示错误响应的 <see cref="Response{T}"/> 实例。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="errorContent">错误响应内容字符串。</param>
    /// <param name="responseHeaders">响应头。</param>
    public Response(HttpStatusCode statusCode, string? errorContent, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>? responseHeaders)
    {
        StatusCode = statusCode;
        Content = default;
        RawContent = errorContent;
        ErrorContent = errorContent;
        ResponseHeaders = responseHeaders;
    }

    /// <summary>
    /// 获取 HTTP 状态码。
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// 获取反序列化的响应内容。当响应表示错误时，此值为 <c>default</c>。
    /// </summary>
    public T? Content { get; }

    /// <summary>
    /// 获取原始响应内容字符串。
    /// </summary>
    public string? RawContent { get; }

    /// <summary>
    /// 获取错误响应内容。当响应成功时，此值为 <c>null</c>。
    /// </summary>
    public string? ErrorContent { get; }

    /// <summary>
    /// 获取响应头集合。
    /// </summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>? ResponseHeaders { get; }

    /// <summary>
    /// 获取一个值，该值指示响应是否成功（状态码在 200-299 范围内）。
    /// </summary>
    public bool IsSuccessStatusCode => (int)StatusCode >= 200 && (int)StatusCode <= 299;

    /// <summary>
    /// 获取响应内容。如果响应不成功，则抛出 <see cref="ApiException"/>。
    /// </summary>
    /// <returns>反序列化的响应内容。</returns>
    /// <exception cref="ApiException">当响应状态码表示错误时抛出。</exception>
    public T GetContentOrThrow()
    {
        if (!IsSuccessStatusCode)
        {
            throw new ApiException(StatusCode, ErrorContent);
        }

        return Content!;
    }

    /// <summary>
    /// 隐式转换为响应内容。
    /// </summary>
    /// <remarks>
    /// 当响应成功时返回 <see cref="Content"/>，否则返回 <c>default</c>。
    /// </remarks>
    public static implicit operator T?(Response<T> response) => response.Content;
}
