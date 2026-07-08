// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// XML HTTP 客户端接口，提供基于 XML 数据格式的 HTTP 请求方法。
/// </summary>
public interface IXmlHttpClient : IBaseHttpClient
{
    /// <summary>
    /// 异步发送 HTTP 请求并返回 XML 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="request">HTTP 请求消息。</param>
    /// <param name="encoding">XML 内容的编码方式，默认为 null。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> SendXmlAsync<TResult>(HttpRequestMessage request, Encoding? encoding = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 POST 请求，将请求数据序列化为 XML 并返回 XML 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="requestData">要发送的请求数据。</param>
    /// <param name="encoding">XML 内容的编码方式，默认为 null。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> PostAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 PUT 请求，将请求数据序列化为 XML 并返回 XML 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TRequest">请求数据的类型。</typeparam>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="requestData">要发送的请求数据。</param>
    /// <param name="encoding">XML 内容的编码方式，默认为 null。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> PutAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步发送 GET 请求并返回 XML 反序列化后的结果。
    /// </summary>
    /// <typeparam name="TResult">响应数据的类型。</typeparam>
    /// <param name="requestUri">请求的 URI。</param>
    /// <param name="encoding">XML 内容的编码方式，默认为 null。</param>
    /// <param name="cancellationToken">用于取消异步操作的取消令牌。</param>
    /// <returns>反序列化后的响应结果任务。</returns>
    Task<TResult?> GetXmlAsync<TResult>(string requestUri, Encoding? encoding = null, CancellationToken cancellationToken = default);
}
