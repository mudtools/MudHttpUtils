namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PatchAttribute : HttpMethodAttribute
{
    public PatchAttribute(string? requestUri = null)
        : base(new HttpMethod("Patch"), requestUri)
    {
    }
}
