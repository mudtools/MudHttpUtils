// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;


/// <summary>
/// 增强的HTTP客户端接口，用于发送各种HTTP请求
/// </summary>
public interface IEnhancedHttpClient
{
    /// <summary>
    /// 发送请求并返回指定类型的结果（JSON格式）
    /// </summary>
    /// <typeparam name="TResult">期望的返回结果类型</typeparam>
    /// <param name="request">要发送的HTTP请求消息</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>返回类型为TResult的异步任务，可能为null</returns>
    Task<TResult?> SendAsync<TResult>(HttpRequestMessage request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载文件内容并以字节数组形式返回
    /// </summary>
    /// <param name="request">要发送的HTTP请求消息</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>返回字节数组的异步任务，可能为null</returns>
    Task<byte[]?> DownloadAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步下载大文件并保存到指定路径
    /// </summary>
    /// <param name="request">要发送的HTTP请求消息</param>
    /// <param name="filePath">用于保存文件的本地路径</param>
    /// <param name="overwrite">是否覆盖已存在的文件</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task<FileInfo> DownloadLargeAsync(HttpRequestMessage request, string filePath, bool overwrite = true, CancellationToken cancellationToken = default);

    #region XML 序列化支持

    /// <summary>
    /// 发送请求并返回XML反序列化后的结果
    /// </summary>
    /// <typeparam name="TResult">期望的返回结果类型</typeparam>
    /// <param name="request">要发送的HTTP请求消息</param>
    /// <param name="encoding">XML编码方式，默认为UTF8</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>返回类型为TResult的异步任务，可能为null</returns>
    Task<TResult?> SendXmlAsync<TResult>(HttpRequestMessage request, Encoding? encoding = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送XML格式的POST请求并反序列化响应
    /// </summary>
    /// <typeparam name="TRequest">请求数据类型</typeparam>
    /// <typeparam name="TResult">响应数据类型</typeparam>
    /// <param name="requestUri">请求URI</param>
    /// <param name="requestData">要序列化为XML的请求数据</param>
    /// <param name="encoding">XML编码方式，默认为UTF8</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>返回类型为TResult的异步任务，可能为null</returns>
    Task<TResult?> PostAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送XML格式的PUT请求并反序列化响应
    /// </summary>
    /// <typeparam name="TRequest">请求数据类型</typeparam>
    /// <typeparam name="TResult">响应数据类型</typeparam>
    /// <param name="requestUri">请求URI</param>
    /// <param name="requestData">要序列化为XML的请求数据</param>
    /// <param name="encoding">XML编码方式，默认为UTF8</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>返回类型为TResult的异步任务，可能为null</returns>
    Task<TResult?> PutAsXmlAsync<TRequest, TResult>(string requestUri, TRequest requestData, Encoding? encoding = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送简单的GET请求并返回XML反序列化后的结果
    /// </summary>
    /// <typeparam name="TResult">期望的返回结果类型</typeparam>
    /// <param name="requestUri">请求URI</param>
    /// <param name="encoding">XML编码方式，默认为UTF8</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>返回类型为TResult的异步任务，可能为null</returns>
    Task<TResult?> GetXmlAsync<TResult>(string requestUri, Encoding? encoding = null, CancellationToken cancellationToken = default);

    #endregion

    #region JSON 辅助方法

    /// <summary>
    /// 发送简单的GET请求并返回JSON反序列化后的结果
    /// </summary>
    /// <typeparam name="TResult">期望的返回结果类型</typeparam>
    /// <param name="requestUri">请求URI</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>返回类型为TResult的异步任务，可能为null</returns>
    Task<TResult?> GetAsync<TResult>(string requestUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送JSON格式的POST请求并反序列化响应
    /// </summary>
    /// <typeparam name="TRequest">请求数据类型</typeparam>
    /// <typeparam name="TResult">响应数据类型</typeparam>
    /// <param name="requestUri">请求URI</param>
    /// <param name="requestData">要序列化为JSON的请求数据</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>返回类型为TResult的异步任务，可能为null</returns>
    Task<TResult?> PostAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送JSON格式的PUT请求并反序列化响应
    /// </summary>
    /// <typeparam name="TRequest">请求数据类型</typeparam>
    /// <typeparam name="TResult">响应数据类型</typeparam>
    /// <param name="requestUri">请求URI</param>
    /// <param name="requestData">要序列化为JSON的请求数据</param>
    /// <param name="cancellationToken">用于取消操作的取消令牌</param>
    /// <returns>返回类型为TResult的异步任务，可能为null</returns>
    Task<TResult?> PutAsJsonAsync<TRequest, TResult>(string requestUri, TRequest requestData, CancellationToken cancellationToken = default);

    #endregion

    /// <summary>
    /// 将指定内容进行加密处理，并返回加密后的字符串。默认使用JSON序列化方式，可以选择XML序列化。
    /// </summary>
    /// <param name="content">需要进行加密处理的对象。</param>
    /// <param name="propertyName">加密后的属性名。</param>
    /// <param name="serializeType">对象的序列化类型。</param>
    /// <returns></returns>
    string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json);
}

/// <summary>
/// 序列化类型。
/// </summary>
public enum SerializeType
{
    /// <summary>
    ///  JSON序列化，使用System.Text.Json进行序列化和反序列化
    /// </summary>
    Json,
    /// <summary>
    /// XML序列化，使用System.Xml.Serialization进行序列化和反序列化
    /// </summary>
    Xml
}