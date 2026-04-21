namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class PathAttribute : Attribute
{
    public PathAttribute()
    {
    }

    public PathAttribute(string? formatString) =>
        FormatString = formatString;

    public string? FormatString { get; set; }
}
