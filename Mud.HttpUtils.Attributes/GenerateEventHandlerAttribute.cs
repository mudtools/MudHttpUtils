namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class GenerateEventHandlerAttribute : Attribute
{
    public GenerateEventHandlerAttribute()
    {
    }

    public GenerateEventHandlerAttribute(string? eventType)
    {
        EventType = eventType;
    }

    public string? HandlerClassName { get; set; }

    public string? HandlerNamespace { get; set; }

    public string? InheritedFrom { get; set; }

    public string? EventType { get; set; }

    public string? ConstructorParameters { get; set; }

    public string? ConstructorBaseCall { get; set; }

    public string? HeaderType { get; set; }
}
