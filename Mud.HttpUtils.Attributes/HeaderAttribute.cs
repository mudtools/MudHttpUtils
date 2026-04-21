namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class HeaderAttribute : Attribute
{
    public HeaderAttribute()
    {
    }

    public HeaderAttribute(string name)
    {
        Name = name;
    }

    public HeaderAttribute(string name, object? value)
        : this(name) =>
        Value = value;

    public string? Name { get; set; }

    private object? _field;

    public object? Value
    {
        get => _field;
        set
        {
            _field = value;
            HasSetValue = true;
        }
    }

    public string? AliasAs { get; set; }

    public bool Escape { get; set; }

    public bool Replace { get; set; }

    internal bool HasSetValue { get; private set; }
}
