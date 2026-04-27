// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// HTTP 方法特性基类，用于定义 HTTP 请求方法和请求 URI。
/// </summary>
/// <remarks>
/// <para>
/// 此特性是所有 HTTP 方法特性（如 <see cref="GetAttribute"/>、<see cref="PostAttribute"/> 等）的基类。
/// 通常不直接使用此类，而是使用其派生类。
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class HttpMethodAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="HttpMethodAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="httpMethod">HTTP 请求方法（GET、POST、PUT、DELETE 等）。</param>
    /// <param name="requestUri">请求 URI，可以为相对路径或绝对路径。</param>
    public HttpMethodAttribute(HttpMethod httpMethod, string? requestUri = null)
    {
        HttpMethod = httpMethod;
        RequestUri = requestUri;
    }

    /// <summary>
    /// 获取或设置 HTTP 请求方法。
    /// </summary>
    public HttpMethod HttpMethod { get; set; }

    /// <summary>
    /// 获取或设置请求 URI，支持路径参数（如 /api/users/{id}）。
    /// </summary>
    public string? RequestUri { get; set; }

    /// <summary>
    /// 获取或设置此请求的内容类型（Content-Type）。
    /// </summary>
    /// <remarks>
    /// 如果设置，将覆盖接口级别的 <see cref="HttpClientApiAttribute.ContentType"/> 设置。
    /// </remarks>
    public string? ContentType { get; set; }

    /// <summary>
    /// 获取或设置期望的响应内容类型。
    /// </summary>
    /// <remarks>
    /// 用于设置请求头 Accept，告知服务器期望的响应格式。
    /// </remarks>
    public string? ResponseContentType { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示是否对响应内容进行解密。
    /// </summary>
    /// <remarks>
    /// 当 API 返回加密数据时，设置为 true 以自动解密响应内容。
    /// 需要配合加密提供程序使用。
    /// </remarks>
    public bool ResponseEnableDecrypt { get; set; }
}
