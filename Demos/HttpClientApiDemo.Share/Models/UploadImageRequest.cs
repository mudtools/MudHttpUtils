using System.Text.Json.Serialization;

namespace HttpClientApiTest.Models;

/// <summary>
/// 上传图片请求对象
/// </summary>
[FormContent]
public partial class UploadImageRequest
{
    /// <summary>
    /// 图片文件的本地路径（标记为 [FilePath] 会自动读取文件内容）
    /// </summary>
    [FilePath]
    [JsonPropertyName("file")]
    public string? ImagePath { get; set; }

    /// <summary>
    /// 图片类型
    /// </summary>
    [JsonPropertyName("image_type")]
    public string? ImageType { get; set; }

    /// <summary>
    /// 父资源key（可选）
    /// </summary>
    [JsonPropertyName("parent_key")]
    public string? ParentKey { get; set; }
}
