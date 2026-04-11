using System.Text.Json.Serialization;

namespace HttpClientApiTest.Models;

/// <summary>
/// 上传文件请求数据
/// </summary>
[FormContent]
public partial class UploadAllFileRequest
{
    /// <summary>
    /// 文件名
    /// </summary>
    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 父类型
    /// </summary>
    [JsonPropertyName("parent_type")]
    public string ParentType { get; set; } = string.Empty;

    /// <summary>
    /// 父节点
    /// </summary>
    [JsonPropertyName("parent_node")]
    public string ParentNode { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小
    /// </summary>
    [JsonPropertyName("size")]
    public int Size { get; set; }

    /// <summary>
    /// 校验和
    /// </summary>
    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    /// <summary>
    /// 文件路径
    /// </summary>
    [FilePath]
    [JsonPropertyName("file")]
    public string? FilePath { get; set; }
}

/// <summary>
/// 简单表单内容请求（注意：此类不使用 FormContentGenerator，因为没有文件上传需求）
/// </summary>
[FormContent]
public partial class SimpleFormRequest
{
    /// <summary>
    /// 用户名
    /// </summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>
    /// 年龄
    /// </summary>
    [JsonPropertyName("age")]
    public int Age { get; set; }

    /// <summary>
    /// 文件路径
    /// </summary>
    [FilePath]
    [JsonPropertyName("file")]
    public string? FilePath { get; set; }

    /// <summary>
    /// 文件二进制内容（当 FilePath 不提供时使用此属性上传文件内容）
    /// </summary>
    public byte[] FileContent { get; set; } = Array.Empty<byte>();
}
