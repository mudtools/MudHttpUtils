namespace Mud.HttpUtils.Attributes;

/// <summary>
///     HTTP 声明式 PATCH 请求方式特性
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PatchAttribute : HttpMethodAttribute
{
    /// <summary>
    ///     <inheritdoc cref="PatchAttribute" />
    /// </summary>
    /// <param name="requestUri">请求地址</param>
    public PatchAttribute(string? requestUri = null)
        : base(new HttpMethod("Patch"), requestUri)
    {
    }
}
