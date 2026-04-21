namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false)]
public sealed class FilePathAttribute : Attribute
{
    public int BufferSize { get; set; } = 81920;
}
