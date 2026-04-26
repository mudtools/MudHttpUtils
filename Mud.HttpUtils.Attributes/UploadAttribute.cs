namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class UploadAttribute : Attribute
{
    public string? FieldName { get; set; }

    public string? FileName { get; set; }

    public string? ContentType { get; set; }
}
