namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
public sealed class IgnoreGeneratorAttribute : Attribute
{
}
