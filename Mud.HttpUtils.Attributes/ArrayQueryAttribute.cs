namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class ArrayQueryAttribute : Attribute
{
    public ArrayQueryAttribute()
    {
    }

    public ArrayQueryAttribute(string name)
    {
        Name = name;
    }

    public ArrayQueryAttribute(string name, string? separator)
        : this(name) =>
        Separator = separator;

    public string? Name { get; set; }

    public string? Separator { get; set; }
}
