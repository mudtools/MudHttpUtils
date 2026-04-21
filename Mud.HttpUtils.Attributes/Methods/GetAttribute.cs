namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class GetAttribute : HttpMethodAttribute
{
    public GetAttribute(string? requestUri = null)
        : base(HttpMethod.Get, requestUri)
    {
    }
}
