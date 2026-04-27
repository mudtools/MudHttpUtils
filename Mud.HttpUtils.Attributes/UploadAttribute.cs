// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;


/// <summary>
/// 标记参数作为文件上传参数。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数，指示该参数为文件上传。支持自定义字段名、文件名和内容类型。
/// 通常用于 multipart/form-data 请求。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 基本文件上传
/// [Post("/api/upload")]
/// Task&lt;UploadResult&gt; UploadAsync([Upload] IFormFile file);
/// 
/// // 自定义字段名和文件名
/// [Post("/api/upload")]
/// Task&lt;UploadResult&gt; UploadAsync(
///     [Upload(FieldName = "document", FileName = "report.pdf")] IFormFile file);
/// 
/// // 指定内容类型
/// [Post("/api/upload")]
/// Task&lt;UploadResult&gt; UploadImageAsync(
///     [Upload(ContentType = "image/png")] IFormFile image);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class UploadAttribute : Attribute
{
    /// <summary>
    /// 获取或设置表单字段名称。如果未设置，将使用参数名。
    /// </summary>
    public string? FieldName { get; set; }

    /// <summary>
    /// 获取或设置上传的文件名。如果未设置，将使用原始文件名。
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// 获取或设置文件的内容类型（MIME 类型）。如果未设置，将自动检测。
    /// </summary>
    public string? ContentType { get; set; }
}
