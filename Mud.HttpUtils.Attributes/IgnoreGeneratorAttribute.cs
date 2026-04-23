namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class IgnoreGeneratorAttribute : Attribute
{
}
