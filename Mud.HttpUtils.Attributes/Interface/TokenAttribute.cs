// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;


/// <summary>
/// 标记参数、接口或方法使用令牌（Token）认证。
/// </summary>
/// <remarks>
/// <para>
/// 应用于参数、接口或方法上，指示该请求需要使用令牌进行认证。支持多种注入模式（请求头、查询参数等）、
/// 令牌类型和作用域配置。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 接口级别使用令牌（默认 Header 模式）
/// [HttpClientApi]
/// [Token]
/// public interface ISecureApi
/// {
///     [Get("/api/secure")]
///     Task&lt;SecureData&gt; GetSecureDataAsync();
/// }
/// 
/// // 方法级别使用用户令牌和作用域
/// [Get("/api/user/profile")]
/// [Token("UserAccessToken", Scopes = "user:read")]
/// Task&lt;Profile&gt; GetProfileAsync();
/// 
/// // 自定义令牌名称和查询参数模式
/// [Get("/api/data")]
/// [Token("ApiKey", InjectionMode = TokenInjectionMode.Query, Name = "access_token")]
/// Task&lt;Data&gt; GetDataAsync();
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TokenAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="TokenAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="tokenType">令牌类型，默认为 "TenantAccessToken"。此值同时设置 TokenType 和 TokenManagerKey（向后兼容）。</param>
    public TokenAttribute(string tokenType = "TenantAccessToken")
    {
        TokenType = tokenType;
        TokenManagerKey = tokenType;
    }

    /// <summary>
    /// 获取或设置令牌类型。
    /// </summary>
    /// <value>默认为 "TenantAccessToken"。</value>
    public string TokenType { get; set; } = "TenantAccessToken";

    /// <summary>
    /// 获取或设置令牌注入模式（请求头、查询参数等）。
    /// </summary>
    /// <value>默认为 <see cref="TokenInjectionMode.Header"/>。</value>
    public TokenInjectionMode InjectionMode { get; set; } = TokenInjectionMode.Header;

    /// <summary>
    /// 获取或设置令牌参数的名称。在 Header 模式下默认为 "Authorization"，在 Query 模式下可自定义。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示是否替换已有的同名令牌。
    /// </summary>
    /// <value>默认为 true。</value>
    [Obsolete("Replace 属性暂未被框架使用，将在未来版本中启用。")]
    public bool Replace { get; set; } = true;

    /// <summary>
    /// 获取或设置令牌作用域（Scopes），多个作用域用逗号分隔。
    /// </summary>
    /// <example>
    /// [Token(TokenType = "UserAccessToken", Scopes = "user:read,user:write")]
    /// </example>
    [Obsolete("Scopes 当前未被生成器处理，将在未来版本中移除或实现。")]
    public string? Scopes { get; set; }

    /// <summary>
    /// 获取或设置令牌管理器的查找键。
    /// 当指定此值时，代码生成器将使用此键而非 TokenType 从 IMudAppContext 中查找令牌管理器。
    /// </summary>
    /// <remarks>
    /// 此属性用于解耦业务概念（TokenType）和技术查找键（TokenManagerKey）。
    /// 例如，多个不同的 TokenType 可以映射到同一个 TokenManager。
    /// 如果未指定，则使用 TokenType 作为查找键。
    /// </remarks>
    public string? TokenManagerKey { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示此令牌是否需要用户 ID。
    /// </summary>
    /// <remarks>
    /// 当设置为 true 时，生成的代码将通过 ICurrentUserContext 获取当前用户 ID，
    /// 并将其传递给 ITokenProvider 以获取用户级令牌。
    /// 如果未显式指定，则根据 TokenType 自动推断：
    /// TokenType 为 "UserAccessToken" 时默认为 true，否则默认为 false。
    /// </remarks>
    public bool RequiresUserId { get; set; }
}
