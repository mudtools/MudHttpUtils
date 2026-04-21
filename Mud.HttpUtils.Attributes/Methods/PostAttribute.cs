namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PostAttribute : HttpMethodAttribute
{
    public PostAttribute(string? requestUri = null)
        : base(HttpMethod.Post, requestUri)
    {
    }
}
