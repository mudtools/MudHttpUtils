// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;


/// <summary>
/// 标记参数或属性作为文件路径。
/// </summary>
/// <remarks>
/// <para>
/// 应用于参数或属性上，指示该字段表示文件路径。在发送请求时会读取文件内容并作为请求体或表单数据发送。
/// 支持自定义缓冲区大小以优化大文件读取性能。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 上传文件
/// [Post("/api/upload")]
/// Task&lt;UploadResult&gt; UploadFileAsync([FilePath] string filePath);
/// 
/// // 自定义缓冲区大小（128KB）
/// [Post("/api/upload-large")]
/// Task&lt;UploadResult&gt; UploadLargeFileAsync([FilePath(BufferSize = 131072)] string filePath);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false)]
public sealed class FilePathAttribute : Attribute
{
    /// <summary>
    /// 获取或设置读取文件时的缓冲区大小（字节）。
    /// </summary>
    /// <value>默认为 81920 字节（80KB）。</value>
    public int BufferSize { get; set; } = 81920;
}
