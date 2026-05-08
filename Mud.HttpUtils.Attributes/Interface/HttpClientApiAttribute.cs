// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记接口为 HTTP API 客户端，由源代码生成器自动生成实现代码。
/// </summary>
/// <remarks>
/// <para>
/// 此特性是 Mud.HttpUtils 的核心特性，应用于接口定义上，指示源代码生成器为该接口生成 HTTP 客户端实现。
/// 配合其他特性（如 <see cref="GetAttribute"/>、<see cref="PostAttribute"/>、<see cref="QueryAttribute"/> 等）
/// 可以声明式地定义 HTTP API 接口。
/// </para>
/// <para>
/// 基地址应通过依赖注入配置时指定，使用 <c>AddMudHttpClient(clientName, baseAddress)</c> 方法。
/// </para>
/// </remarks>
/// <example>
/// 定义 HTTP API 接口：
/// <code>
/// [HttpClientApi]
/// public interface IUserApi
/// {
///     [Get("/api/users/{id}")]
///     Task&lt;User&gt; GetUserAsync(int id);
///     
///     [Post("/api/users")]
///     Task&lt;User&gt; CreateUserAsync([Body] User user);
/// }
/// </code>
/// 
/// 注册和使用：
/// <code>
/// // 注册
/// builder.Services.AddMudHttpClient&lt;IUserApi&gt;("UserApi", "https://api.example.com");
/// 
/// // 使用
/// var userApi = serviceProvider.GetRequiredService&lt;IUserApi&gt;();
/// var user = await userApi.GetUserAsync(123);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public sealed class HttpClientApiAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="HttpClientApiAttribute"/> 类的新实例。
    /// </summary>
    public HttpClientApiAttribute()
    {
    }

    [Obsolete("此构造函数已被弃用，请使用 AddMudHttpClient(clientName, baseAddress) 配置基地址。", error: true)]
    public HttpClientApiAttribute(string baseAddress)
    {
        BaseAddress = baseAddress;
    }

    /// <summary>
    /// 获取或设置请求的默认内容类型。
    /// </summary>
    /// <value>默认为 "application/json"。</value>
    public string ContentType { get; set; } = "application/json";

    [Obsolete("此属性已被弃用，请使用 AddMudHttpClient(clientName, baseAddress) 配置基地址。", error: true)]
    public string? BaseAddress { get; }

    /// <summary>
    /// 获取或设置请求超时时间（秒）。
    /// </summary>
    /// <value>默认为 50 秒。</value>
    public int Timeout { get; set; } = 50;

    /// <summary>
    /// 获取或设置服务注册组名称，用于将客户端分组管理。
    /// </summary>
    public string? RegistryGroupName { get; set; }

    /// <summary>
    /// 获取或设置令牌管理器名称，指定用于该客户端的令牌管理策略。
    /// </summary>
    public string? TokenManage { get; set; }

    /// <summary>
    /// 获取或设置 HttpClient 实例名称，用于引用已注册的 HttpClient。
    /// </summary>
    public string? HttpClient { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示此接口生成的类是否为抽象类。
    /// </summary>
    /// <remarks>
    /// 抽象类不会生成完整的实现代码，通常用作基类供其他接口继承。
    /// </remarks>
    public bool IsAbstract { get; set; }

    /// <summary>
    /// 获取或设置继承来源，用于标识此接口继承自哪个接口。
    /// </summary>
    public string? InheritedFrom { get; set; }
}
