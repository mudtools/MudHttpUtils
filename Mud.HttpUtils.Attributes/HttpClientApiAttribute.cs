namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class HttpClientApiAttribute : Attribute
{
    public HttpClientApiAttribute()
    {
    }

    [Obsolete("此构造函数已被弃用，请使用 AddMudHttpClient(clientName, baseAddress) 配置基地址。", error: true)]
    public HttpClientApiAttribute(string baseAddress)
    {
        BaseAddress = baseAddress;
    }

    public string ContentType { get; set; } = "application/json";

    [Obsolete("此属性已被弃用，请使用 AddMudHttpClient(clientName, baseAddress) 配置基地址。", error: true)]
    public string? BaseAddress { get; }

    public int Timeout { get; set; } = 50;

    public string? RegistryGroupName { get; set; }

    public string? TokenManage { get; set; }

    public string? HttpClient { get; set; }

    public bool IsAbstract { get; set; }

    public string? InheritedFrom { get; set; }
}
