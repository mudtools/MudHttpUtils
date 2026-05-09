// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 单个 HttpClient 实例的配置选项
/// </summary>
/// <remarks>
/// <para>配置单个命名 HttpClient 的详细选项，包括基地址、超时、默认请求头等。</para>
/// <para>配置示例：</para>
/// <code>
/// "Clients": {
///   "MyApi": {
///     "BaseAddress": "https://api.example.com",
///     "TimeoutSeconds": 30,
///     "DefaultHeaders": {
///       "X-Api-Key": "my-api-key",
///       "X-Client-Version": "1.0.0"
///     },
///     "TokenManagerKey": "MyTokenManager",
///     "TokenInjectionMode": "Header",
///     "AllowAnyStatusCode": false
///   }
/// }
/// </code>
/// </remarks>
public class MudHttpClientOptions
{
    /// <summary>
    /// 基础地址
    /// </summary>
    /// <remarks>
    /// HttpClient 请求的基础 URL，例如：https://api.example.com
    /// </remarks>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    /// <remarks>
    /// 请求的超时时间，单位为秒。如果未设置，使用系统默认值（通常为 100 秒）。
    /// </remarks>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// 默认请求头
    /// </summary>
    /// <remarks>
    /// 每个请求都会自动添加的默认 HTTP 头。
    /// 常用于设置 API 密钥、客户端版本等信息。
    /// </remarks>
    public Dictionary<string, string>? DefaultHeaders { get; set; }

    /// <summary>
    /// Token 管理器键名
    /// </summary>
    /// <remarks>
    /// 用于获取访问令牌的 Token 管理器标识。
    /// 与 <see cref="TokenInjectionMode"/> 配合使用。
    /// </remarks>
    public string? TokenManagerKey { get; set; }

    /// <summary>
    /// Token 注入模式
    /// </summary>
    /// <remarks>
    /// 指定如何将 Token 注入到请求中：
    /// <list type="bullet">
    ///   <item>Header - 通过 Authorization 请求头注入</item>
    ///   <item>Query - 通过查询参数注入</item>
    ///   <item>Cookie - 通过 Cookie 注入</item>
    /// </list>
    /// </remarks>
    public string? TokenInjectionMode { get; set; }

    /// <summary>
    /// Token 作用域
    /// </summary>
    /// <remarks>
    /// 请求访问令牌时使用的 OAuth 作用域，多个作用域用空格分隔。
    /// 例如："read write admin"
    /// </remarks>
    public string? TokenScopes { get; set; }

    /// <summary>
    /// 允许任意 HTTP 状态码
    /// </summary>
    /// <remarks>
    /// 如果设置为 true，HttpClient 不会因非 2xx 状态码抛出异常。
    /// 适用于需要手动处理错误响应的场景。
    /// 默认值为 false。
    /// </remarks>
    public bool? AllowAnyStatusCode { get; set; }

    /// <summary>
    /// 是否允许自定义基础 URL
    /// </summary>
    /// <remarks>
    /// 如果设置为 true，允许请求白名单域名之外的 URL（仍会检查私有 IP 和内网域名以防范 SSRF）。
    /// 如果设置为 false（默认），则只允许访问白名单中的域名。
    /// <para>注意：启用此选项会放宽 URL 验证策略，请确保在受信任的环境中使用。</para>
    /// </remarks>
    public bool AllowCustomBaseUrls { get; set; }

    /// <summary>
    /// 序列化方法
    /// </summary>
    /// <remarks>
    /// 指定请求/响应内容的序列化方式：
    /// <list type="bullet">
    ///   <item>Json - JSON 序列化（默认）</item>
    ///   <item>Xml - XML 序列化</item>
    ///   <item>FormUrlEncoded - URL 编码表单序列化</item>
    /// </list>
    /// </remarks>
    public string? SerializationMethod { get; set; }

    /// <summary>
    /// 弹性策略配置
    /// </summary>
    /// <remarks>
    /// 配置重试、熔断和超时等弹性策略。
    /// 详见 <see cref="MudHttpClientResilienceOptions"/>。
    /// </remarks>
    public MudHttpClientResilienceOptions? Resilience { get; set; }
}
