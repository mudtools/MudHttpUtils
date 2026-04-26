namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class FormAttribute : Attribute
{
    public string? FieldName { get; set; }
}
