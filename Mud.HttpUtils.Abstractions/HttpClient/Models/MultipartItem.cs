// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System.IO;
using System.Net.Http;

namespace Mud.HttpUtils;

/// <summary>
/// multipart/form-data 上传项的抽象基类。
/// </summary>
/// <remarks>
/// 允许运行时多态选择上传来源（字节数组、流、文件）。
/// 派生类通过 <see cref="ToContent"/> 提供具体的 <see cref="HttpContent"/> 实现。
/// </remarks>
public abstract class MultipartItem
{
    /// <summary>字段名（form 参数名）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>文件名（可选，用于 Content-Disposition）。</summary>
    public string? FileName { get; set; }

    /// <summary>内容类型（可选，如 image/jpeg）。</summary>
    public string? ContentType { get; set; }

    /// <summary>转换为 <see cref="HttpContent"/>。</summary>
    /// <returns>表示此上传项的 <see cref="HttpContent"/> 实例。</returns>
    public abstract HttpContent ToContent();
}

/// <summary>
/// 字节数组上传项。
/// </summary>
public class ByteArrayPart : MultipartItem
{
    /// <summary>要上传的字节数组。</summary>
    public byte[] Bytes { get; }

    /// <summary>初始化 <see cref="ByteArrayPart"/> 实例。</summary>
    /// <param name="bytes">字节数组。</param>
    /// <param name="name">字段名。</param>
    /// <param name="fileName">文件名（可选）。</param>
    /// <param name="contentType">内容类型（可选）。</param>
    public ByteArrayPart(byte[] bytes, string name, string? fileName = null, string? contentType = null)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        FileName = fileName;
        ContentType = contentType;
    }

    /// <inheritdoc/>
    public override HttpContent ToContent() => new ByteArrayContent(Bytes);
}

/// <summary>
/// 流上传项。
/// </summary>
public class StreamPart : MultipartItem
{
    /// <summary>要上传的流。</summary>
    public Stream Stream { get; }

    /// <summary>初始化 <see cref="StreamPart"/> 实例。</summary>
    /// <param name="stream">流。</param>
    /// <param name="name">字段名。</param>
    /// <param name="fileName">文件名（可选）。</param>
    /// <param name="contentType">内容类型（可选）。</param>
    public StreamPart(Stream stream, string name, string? fileName = null, string? contentType = null)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        FileName = fileName;
        ContentType = contentType;
    }

    /// <inheritdoc/>
    public override HttpContent ToContent() => new StreamContent(Stream);
}

/// <summary>
/// 文件上传项。
/// </summary>
public class FileInfoPart : MultipartItem
{
    /// <summary>要上传的文件信息。</summary>
    public FileInfo FileInfo { get; }

    /// <summary>初始化 <see cref="FileInfoPart"/> 实例。</summary>
    /// <param name="fileInfo">文件信息。</param>
    /// <param name="name">字段名。</param>
    /// <param name="contentType">内容类型（可选）。</param>
    public FileInfoPart(FileInfo fileInfo, string name, string? contentType = null)
    {
        FileInfo = fileInfo ?? throw new ArgumentNullException(nameof(fileInfo));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        FileName = fileInfo.Name;
        ContentType = contentType;
    }

    /// <inheritdoc/>
    public override HttpContent ToContent()
    {
#if NET5_0_OR_GREATER
        return new StreamContent(FileInfo.OpenRead());
#else
        return new StreamContent(FileInfo.OpenRead());
#endif
    }
}
