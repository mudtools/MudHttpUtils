namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class DeleteAttribute : HttpMethodAttribute
{
    public DeleteAttribute(string? requestUri = null)
        : base(HttpMethod.Delete, requestUri)
    {
    }
}
