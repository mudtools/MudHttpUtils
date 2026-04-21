namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Parameter, AllowMultiple = true)]
public sealed class QueryAttribute : Attribute
{
    public QueryAttribute()
    {
    }

    public QueryAttribute(string name)
    {
        Name = name;
    }

    public QueryAttribute(string name, string? formatString)
        : this(name) =>
        FormatString = formatString;

    public string? Name { get; set; }

    public string? FormatString { get; set; }

    public string? AliasAs { get; set; }
}
