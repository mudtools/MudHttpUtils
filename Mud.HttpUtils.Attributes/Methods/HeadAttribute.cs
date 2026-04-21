namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class HeadAttribute : HttpMethodAttribute
{
    public HeadAttribute(string? requestUri = null)
        : base(HttpMethod.Head, requestUri)
    {
    }
}
