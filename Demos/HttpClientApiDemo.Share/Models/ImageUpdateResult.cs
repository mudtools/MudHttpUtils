using System.Text.Json.Serialization;

namespace HttpClientApiTest.Models;

/// <summary>
/// 图片上传结果
/// </summary>
[FormContent]
public partial class ImageUpdateResult
{
    /// <summary>
    /// 图片key
    /// </summary>
    [JsonPropertyName("image_key")]
    public string? ImageKey { get; set; }

    /// <summary>
    /// 图片文件的本地路径（标记为 [FilePath] 会自动读取文件内容）
    /// </summary>
    [FilePath]
    [JsonPropertyName("file")]
    public string? ImagePath { get; set; }
}
