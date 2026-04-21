namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PutAttribute : HttpMethodAttribute
{
    public PutAttribute(string? requestUri = null)
        : base(HttpMethod.Put, requestUri)
    {
    }
}
