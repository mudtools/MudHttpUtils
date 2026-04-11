namespace Mud.HttpUtils.Attributes;

/// <summary>
///     HTTP 声明式请求方式特性
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class HttpMethodAttribute : Attribute
{
    /// <summary>
    ///     <inheritdoc cref="HttpMethodAttribute" />
    /// </summary>
    /// <param name="httpMethod">请求方式</param>
    /// <param name="requestUri">请求地址</param>
    public HttpMethodAttribute(HttpMethod httpMethod, string? requestUri = null)
    {
        HttpMethod = httpMethod;
        RequestUri = requestUri;
    }

    /// <summary>
    ///     请求方式
    /// </summary>
    public HttpMethod HttpMethod { get; set; }

    /// <summary>
    ///     请求地址
    /// </summary>
    public string? RequestUri { get; set; }

    /// <summary>
    /// 请求内容类型（用于序列化请求体）
    /// <para>默认: 从接口或全局配置继承</para>
    /// <para>示例: "application/json", "application/xml", "text/plain"</para>
    /// <para>优先级: Body参数ContentType > 方法级ContentType > 接口级ContentType</para>
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// 响应内容类型（用于反序列化）
    /// <para>默认: 从接口或全局配置继承</para>
    /// <para>示例: "application/json", "application/xml", "text/plain"</para>
    /// </summary>
    public string? ResponseContentType { get; set; }

    /// <summary>
    /// 响应是否启用解密
    /// <para>当API返回加密数据时设置为true</para>
    /// </summary>
    public bool ResponseEnableDecrypt { get; set; }
}