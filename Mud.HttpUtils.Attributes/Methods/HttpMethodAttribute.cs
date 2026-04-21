namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class HttpMethodAttribute : Attribute
{
    public HttpMethodAttribute(HttpMethod httpMethod, string? requestUri = null)
    {
        HttpMethod = httpMethod;
        RequestUri = requestUri;
    }

    public HttpMethod HttpMethod { get; set; }

    public string? RequestUri { get; set; }

    public string? ContentType { get; set; }

    public string? ResponseContentType { get; set; }

    public bool ResponseEnableDecrypt { get; set; }
}
