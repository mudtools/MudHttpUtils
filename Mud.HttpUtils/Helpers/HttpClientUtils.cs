// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net.Http.Headers;

namespace Mud.HttpUtils;

/// <summary>
/// HttpClient 工具类，提供根据请求对象构建 MultipartFormDataContent 和根据文件路径获取 ByteArrayContent 的方法，支持异步操作和取消功能。
/// </summary>
public sealed class HttpClientUtils
{
    /// <summary>
    /// 根据文件路径异步获取 ByteArrayContent 对象
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含文件内容的 ByteArrayContent 对象</returns>
    public static async Task<ByteArrayContent> GetByteArrayContentAsync(string? filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"文件未找到: {filePath}");

#if NETSTANDARD2_0
        var fileBytes = File.ReadAllBytes(filePath);
#else
        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken)
                                  .ConfigureAwait(false);
#endif
        return CreateFileContent(filePath, fileBytes);
    }

    /// <summary>
    /// 创建包含文件数据的 ByteArrayContent 对象，并设置适当的内容类型头
    /// </summary>
    /// <param name="fileName">文件名，用于确定内容类型</param>
    /// <param name="fileBytes">文件二进制数据</param>
    /// <returns>配置好的 ByteArrayContent 对象</returns>
    /// <exception cref="ArgumentNullException">当 fileName 或 fileBytes 为 null 或空字符串时抛出</exception>
    public static ByteArrayContent CreateFileContent(string? fileName, byte[] fileBytes)
    {
        // 参数验证 - 使用更精确的异常消息
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentNullException(nameof(fileName), "文件名不能为空或仅包含空白字符。");

        if (fileBytes == null || fileBytes.Length == 0)
            throw new ArgumentNullException(nameof(fileBytes), "文件数据不能为 null 或空数组。");

        // 创建 ByteArrayContent 对象
        var fileContent = new ByteArrayContent(fileBytes);

        try
        {
            // 获取文件的内容类型并设置到头部
            string contentType = GetContentType(fileName);

            // 验证内容类型是否有效
            if (string.IsNullOrWhiteSpace(contentType))
                throw new InvalidOperationException($"无法为文件 '{fileName}' 确定有效的内容类型。");

            // 解析并设置 Content-Type 头
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }
        catch (FormatException ex)
        {
            // 处理内容类型格式错误的情况
            throw new InvalidOperationException($"内容类型格式无效: {GetContentType(fileName)}", ex);
        }

        return fileContent;
    }

    private static readonly Dictionary<string, string> ContentTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // 图片
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".tiff"] = "image/tiff",
        [".tif"] = "image/tiff",
        [".heic"] = "image/heic",
        [".svg"] = "image/svg+xml",

        // 文档
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".md"] = "text/markdown",

        // 音频/视频
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
        [".wav"] = "audio/wav",
        [".avi"] = "video/x-msvideo",
        [".mov"] = "video/quicktime",

        // 压缩文件
        [".zip"] = "application/zip",
        [".rar"] = "application/vnd.rar",
        [".7z"] = "application/x-7z-compressed",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",

        // JSON/XML
        [".json"] = "application/json",
        [".xml"] = "application/xml",
    };

    // 根据文件扩展名获取对应的 Content-Type
    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return ContentTypeMappings.TryGetValue(extension, out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}
