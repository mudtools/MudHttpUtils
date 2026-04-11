// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;

/// <summary>
/// LoggerMessage 委托定义，用于高性能日志记录
/// </summary>
/// <remarks>
/// 使用 LoggerMessage.Define 创建的委托可以避免：
/// 1. 值类型装箱
/// 2. 不必要的数组分配
/// 3. 在日志级别未启用时的字符串格式化
/// </remarks>
internal static class EnhancedHttpClientLogs
{
    #region Debug 级别日志 (EventId: 1-10)

    private static readonly Action<ILogger, string, Exception?> s_jsonResponseBodyEmpty =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, nameof(JsonResponseBodyEmpty)),
            "JSON响应内容为空，返回默认值: {Url}");

    private static readonly Action<ILogger, string, string, Exception?> s_jsonResponseBodyRaw =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(2, nameof(JsonResponseBodyRaw)),
            "原始JSON响应内容: {Url}\n{Response}");

    private static readonly Action<ILogger, string, string, Exception?> s_jsonDeserializeSuccess =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(3, nameof(JsonDeserializeSuccess)),
            "JSON反序列化成功: {Url}, 类型: {Type}");

    private static readonly Action<ILogger, string, Exception?> s_xmlResponseBodyEmpty =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(4, nameof(XmlResponseBodyEmpty)),
            "XML响应内容为空，返回默认值: {Url}");

    private static readonly Action<ILogger, string, string, Exception?> s_xmlResponseBodyRaw =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(5, nameof(XmlResponseBodyRaw)),
            "原始XML响应内容: {Url}\n{XmlResponse}");

    private static readonly Action<ILogger, string, string, Exception?> s_xmlDeserializeSuccess =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(6, nameof(XmlDeserializeSuccess)),
            "XML反序列化成功: {Url}, 类型: {Type}");

    private static readonly Action<ILogger, string, string, Exception?> s_httpClientOperation =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(7, nameof(HttpClientOperation)),
            "[HttpClient] {Operation}: {Uri}");

    #endregion

    #region Information 级别日志 (EventId: 11-20)

    private static readonly Action<ILogger, string, Exception?> s_fileExistsWillOverwrite =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(11, nameof(FileExistsWillOverwrite)),
            "文件已存在，将被覆盖: {FilePath}");

    private static readonly Action<ILogger, string, double, string, Exception?> s_downloadFileStarted =
        LoggerMessage.Define<string, double, string>(
            LogLevel.Information,
            new EventId(12, nameof(DownloadFileStarted)),
            "开始下载文件: {Url}, 大小: {Size:F2}MB, 保存到: {FilePath}");

    private static readonly Action<ILogger, string, double, Exception?> s_downloadFileCompleted =
        LoggerMessage.Define<string, double>(
            LogLevel.Information,
            new EventId(13, nameof(DownloadFileCompleted)),
            "文件下载完成: {FilePath}, 大小: {Size:F2}MB");

    #endregion

    #region Warning 级别日志 (EventId: 21-30)

    private static readonly Action<ILogger, string, Exception?> s_httpRequestCancelled =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(21, nameof(HttpRequestCancelled)),
            "HTTP请求被取消: {Url}");

    private static readonly Action<ILogger, string, double, Exception?> s_downloadFileLarge =
        LoggerMessage.Define<string, double>(
            LogLevel.Warning,
            new EventId(22, nameof(DownloadFileLarge)),
            "下载文件较大: {Url}, 大小: {Size:F2}MB");

    private static readonly Action<ILogger, string, Exception> s_cleanupPartialFileFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(23, nameof(CleanupPartialFileFailed)),
            "清理部分下载的文件失败: {FilePath}");

    private static readonly Action<ILogger, Exception> s_readErrorResponseFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(24, nameof(ReadErrorResponseFailed)),
            "读取错误响应内容失败");

    #endregion

    #region Error 级别日志 (EventId: 31-50)

    private static readonly Action<ILogger, string, string, string, string?, Exception> s_jsonDeserializeFailedDetailed =
        LoggerMessage.Define<string, string, string, string?>(
            LogLevel.Error,
            new EventId(31, nameof(JsonDeserializeFailedDetailed)),
            "JSON反序列化失败: {Url}\n期望类型: {ExpectedType}\n原始响应: {RawResponse}\n错误位置: {Path}");

    private static readonly Action<ILogger, string, string, Exception> s_jsonDeserializeFailedSimple =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(32, nameof(JsonDeserializeFailedSimple)),
            "JSON反序列化失败: {Url}\n期望类型: {ExpectedType}");

    private static readonly Action<ILogger, string, int, Exception> s_httpRequestFailedWithStatusCode =
        LoggerMessage.Define<string, int>(
            LogLevel.Error,
            new EventId(33, nameof(HttpRequestFailedWithStatusCode)),
            "HTTP请求处理异常: {Url}, StatusCode: {StatusCode}");

    private static readonly Action<ILogger, string, Exception> s_httpRequestFailedSimple =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(34, nameof(HttpRequestFailedSimple)),
            "HTTP请求处理异常: {Url}");

    private static readonly Action<ILogger, string, double, Exception> s_httpRequestTimeout =
        LoggerMessage.Define<string, double>(
            LogLevel.Error,
            new EventId(35, nameof(HttpRequestTimeout)),
            "HTTP请求超时: {Url}, Timeout: {Timeout:F2}秒");

    private static readonly Action<ILogger, string, string, Exception> s_httpRequestFailedWithExceptionType =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(36, nameof(HttpRequestFailedWithExceptionType)),
            "HTTP请求处理异常: {Url}, ExceptionType: {ExceptionType}");

    private static readonly Action<ILogger, string, string, string, Exception> s_xmlDeserializeFailed =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            new EventId(37, nameof(XmlDeserializeFailed)),
            "XML反序列化失败: {Url}\n期望类型: {ExpectedType}\n原始XML响应: {XmlResponse}");

    private static readonly Action<ILogger, string, Exception> s_fileDownloadFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(38, nameof(FileDownloadFailed)),
            "文件下载异常: {Url}");

    private static readonly Action<ILogger, string, string, Exception> s_largeFileDownloadFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(39, nameof(LargeFileDownloadFailed)),
            "大文件下载异常: {Url}, 文件路径: {FilePath}");

    private static readonly Action<ILogger, int, string, Exception?> s_httpRequestFailedWithResponse =
        LoggerMessage.Define<int, string>(
            LogLevel.Error,
            new EventId(40, nameof(HttpRequestFailedWithResponse)),
            "HTTP请求失败: {StatusCode}, 响应（已脱敏）: {Response}");

    private static readonly Action<ILogger, string, string, Exception> s_httpClientError =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(41, nameof(HttpClientError)),
            "[HttpClient] {ErrorMessage}: {Uri}");

    #endregion

    #region 公开的扩展方法 - Debug

    /// <summary>
    /// 记录JSON响应内容为空
    /// </summary>
    public static void JsonResponseBodyEmpty(this ILogger logger, string url)
        => s_jsonResponseBodyEmpty(logger, url, null);

    /// <summary>
    /// 记录原始JSON响应内容
    /// </summary>
    public static void JsonResponseBodyRaw(this ILogger logger, string url, string response)
        => s_jsonResponseBodyRaw(logger, url, response, null);

    /// <summary>
    /// 记录JSON反序列化成功
    /// </summary>
    public static void JsonDeserializeSuccess(this ILogger logger, string url, string type)
        => s_jsonDeserializeSuccess(logger, url, type, null);

    /// <summary>
    /// 记录XML响应内容为空
    /// </summary>
    public static void XmlResponseBodyEmpty(this ILogger logger, string url)
        => s_xmlResponseBodyEmpty(logger, url, null);

    /// <summary>
    /// 记录原始XML响应内容
    /// </summary>
    public static void XmlResponseBodyRaw(this ILogger logger, string url, string xmlResponse)
        => s_xmlResponseBodyRaw(logger, url, xmlResponse, null);

    /// <summary>
    /// 记录XML反序列化成功
    /// </summary>
    public static void XmlDeserializeSuccess(this ILogger logger, string url, string type)
        => s_xmlDeserializeSuccess(logger, url, type, null);

    /// <summary>
    /// 记录HttpClient操作
    /// </summary>
    public static void HttpClientOperation(this ILogger logger, string operation, string uri)
        => s_httpClientOperation(logger, operation, uri, null);

    #endregion

    #region 公开的扩展方法 - Information

    /// <summary>
    /// 记录文件已存在将被覆盖
    /// </summary>
    public static void FileExistsWillOverwrite(this ILogger logger, string filePath)
        => s_fileExistsWillOverwrite(logger, filePath, null);

    /// <summary>
    /// 记录开始下载文件
    /// </summary>
    public static void DownloadFileStarted(this ILogger logger, string url, double size, string filePath)
        => s_downloadFileStarted(logger, url, size, filePath, null);

    /// <summary>
    /// 记录文件下载完成
    /// </summary>
    public static void DownloadFileCompleted(this ILogger logger, string filePath, double size)
        => s_downloadFileCompleted(logger, filePath, size, null);

    #endregion

    #region 公开的扩展方法 - Warning

    /// <summary>
    /// 记录HTTP请求被取消
    /// </summary>
    public static void HttpRequestCancelled(this ILogger logger, string url, Exception? exception = null)
        => s_httpRequestCancelled(logger, url, exception);

    /// <summary>
    /// 记录下载文件较大警告
    /// </summary>
    public static void DownloadFileLarge(this ILogger logger, string url, double size)
        => s_downloadFileLarge(logger, url, size, null);

    /// <summary>
    /// 记录清理部分下载文件失败
    /// </summary>
    public static void CleanupPartialFileFailed(this ILogger logger, string filePath, Exception exception)
        => s_cleanupPartialFileFailed(logger, filePath, exception);

    /// <summary>
    /// 记录读取错误响应失败
    /// </summary>
    public static void ReadErrorResponseFailed(this ILogger logger, Exception exception)
        => s_readErrorResponseFailed(logger, exception);

    #endregion

    #region 公开的扩展方法 - Error

    /// <summary>
    /// 记录JSON反序列化失败（详细信息）
    /// </summary>
    public static void JsonDeserializeFailedDetailed(this ILogger logger, string url, string expectedType, string rawResponse, string? path, Exception exception)
        => s_jsonDeserializeFailedDetailed(logger, url, expectedType, rawResponse, path, exception);

    /// <summary>
    /// 记录JSON反序列化失败（简单信息）
    /// </summary>
    public static void JsonDeserializeFailedSimple(this ILogger logger, string url, string expectedType, Exception exception)
        => s_jsonDeserializeFailedSimple(logger, url, expectedType, exception);

    /// <summary>
    /// 记录HTTP请求失败（带状态码）
    /// </summary>
    public static void HttpRequestFailedWithStatusCode(this ILogger logger, string url, int statusCode, Exception exception)
        => s_httpRequestFailedWithStatusCode(logger, url, statusCode, exception);

    /// <summary>
    /// 记录HTTP请求失败（简单信息）
    /// </summary>
    public static void HttpRequestFailedSimple(this ILogger logger, string url, Exception exception)
        => s_httpRequestFailedSimple(logger, url, exception);

    /// <summary>
    /// 记录HTTP请求超时
    /// </summary>
    public static void HttpRequestTimeout(this ILogger logger, string url, double timeout, Exception exception)
        => s_httpRequestTimeout(logger, url, timeout, exception);

    /// <summary>
    /// 记录HTTP请求失败（带异常类型）
    /// </summary>
    public static void HttpRequestFailedWithExceptionType(this ILogger logger, string url, string exceptionType, Exception exception)
        => s_httpRequestFailedWithExceptionType(logger, url, exceptionType, exception);

    /// <summary>
    /// 记录XML反序列化失败
    /// </summary>
    public static void XmlDeserializeFailed(this ILogger logger, string url, string expectedType, string xmlResponse, Exception exception)
        => s_xmlDeserializeFailed(logger, url, expectedType, xmlResponse, exception);

    /// <summary>
    /// 记录文件下载失败
    /// </summary>
    public static void FileDownloadFailed(this ILogger logger, string url, Exception exception)
        => s_fileDownloadFailed(logger, url, exception);

    /// <summary>
    /// 记录大文件下载失败
    /// </summary>
    public static void LargeFileDownloadFailed(this ILogger logger, string url, string filePath, Exception exception)
        => s_largeFileDownloadFailed(logger, url, filePath, exception);

    /// <summary>
    /// 记录HTTP请求失败（带响应）
    /// </summary>
    public static void HttpRequestFailedWithResponse(this ILogger logger, int statusCode, string response)
        => s_httpRequestFailedWithResponse(logger, statusCode, response, null);

    /// <summary>
    /// 记录HttpClient错误
    /// </summary>
    public static void HttpClientError(this ILogger logger, string errorMessage, string uri, Exception exception)
        => s_httpClientError(logger, errorMessage, uri, exception);

    #endregion
}
