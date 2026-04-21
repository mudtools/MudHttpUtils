using Mud.HttpUtils;

namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class BodyAttribute : Attribute
{
    public BodyAttribute()
    {
    }

    public BodyAttribute(string contentType) => ContentType = contentType;

    public BodyAttribute(string contentType, string contentEncoding)
        : this(contentType) =>
        ContentEncoding = contentEncoding;

    public string? ContentType { get; set; }

    public string? ContentEncoding { get; set; }

    public bool UseStringContent { get; set; }

    public bool RawString { get; set; }

    public bool EnableEncrypt { get; set; } = false;

    public SerializeType EncryptSerializeType { get; set; } = SerializeType.Json;

    public string? EncryptPropertyName { get; set; }
}
