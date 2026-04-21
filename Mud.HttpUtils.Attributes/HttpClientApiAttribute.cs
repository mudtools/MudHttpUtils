namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class HttpClientApiAttribute : Attribute
{
    public HttpClientApiAttribute()
    {
    }

    public HttpClientApiAttribute(string baseAddress)
    {
        BaseAddress = baseAddress;
    }

    public string ContentType { get; set; } = "application/json";

    [Obsolete("此属性已被弃用，请使用其他方式配置API端点")]
    public string? BaseAddress { get; private set; }

    public int Timeout { get; set; } = 50;

    public string? RegistryGroupName { get; set; }

    public string? TokenManage { get; set; }

    public string? HttpClient { get; set; }

    public bool IsAbstract { get; set; }

    public string? InheritedFrom { get; set; }
}
