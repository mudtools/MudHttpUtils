namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class OptionsAttribute : HttpMethodAttribute
{
    public OptionsAttribute(string? requestUri = null)
        : base(HttpMethod.Options, requestUri)
    {
    }
}
