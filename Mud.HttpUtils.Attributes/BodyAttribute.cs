using Mud.HttpUtils;

namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class BodyAttribute : Attribute
{
    public BodyAttribute()
    {
    }

    public BodyAttribute(string contentType) => ContentType = contentType;

    public string? ContentType { get; set; }

    public bool UseStringContent { get; set; }

    /// <summary>
    /// 是否将参数作为原始字符串发送（不进行 JSON 序列化，也不调用 ToString()）。
    /// 适用于直接发送纯文本或预格式化字符串的场景。
    /// </summary>
    public bool RawString { get; set; }

    public bool EnableEncrypt { get; set; } = false;

    public SerializeType EncryptSerializeType { get; set; } = SerializeType.Json;

    public string? EncryptPropertyName { get; set; }
}
