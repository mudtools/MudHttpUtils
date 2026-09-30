// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌恢复上下文，携带令牌注入信息以支持 <see cref="System.Net.Http.DelegatingHandler"/> 在 401 恢复时正确重新注入令牌。
/// <para>
/// 生成代码在构建 HTTP 请求时将此上下文附加到 <see cref="System.Net.Http.HttpRequestMessage.Properties"/> 中，
/// 键为 <see cref="PropertyKey"/>。恢复处理器读取此上下文以确定令牌的注入模式和位置。
/// </para>
/// </summary>
public sealed class TokenRecoveryContext
{
    /// <summary>
    /// 附加到 <see cref="System.Net.Http.HttpRequestMessage.Properties"/> 时使用的属性键。
    /// </summary>
    public const string PropertyKey = "__Mud_HttpUtils_TokenRecoveryContext";

    /// <summary>
    /// 获取或设置令牌注入模式。
    /// </summary>
    public TokenInjectionMode InjectionMode { get; set; } = TokenInjectionMode.Header;

    /// <summary>
    /// 获取或设置令牌注入的 Header 名称。
    /// <para>Header 模式默认为 "Authorization"；ApiKey 模式为自定义 Header 名称。</para>
    /// </summary>
    public string HeaderName { get; set; } = "Authorization";

    /// <summary>
    /// 获取或设置令牌的认证方案（如 "Bearer"），仅 Header 模式使用。
    /// </summary>
    public string TokenScheme { get; set; } = "Bearer";

    /// <summary>
    /// 获取或设置 Cookie 名称，仅 Cookie 模式使用。
    /// </summary>
    public string? CookieName { get; set; }

    /// <summary>
    /// 获取或设置查询参数名称，仅 Query 模式使用。
    /// </summary>
    public string? QueryParameterName { get; set; }

    /// <summary>
    /// 获取或设置用户 ID，用于用户级令牌恢复。
    /// <para>当令牌需要用户上下文时，生成代码将当前用户 ID 附加到此属性。</para>
    /// <para><b>SR-M7（P2.5）：必须来自服务端受信上下文</b>——恢复执行器将校验本值与
    /// <see cref="ICurrentUserContext"/> 主体身份的一致性，不一致即拒绝恢复（返回 401）。
    /// 此校验是纵深防御层，不是完整授权模型。</para>
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 获取或设置令牌管理器查找键，用于在恢复时定位正确的令牌管理器。
    /// <para>SR-M6（P2.4，D9）：配置 <see cref="ITokenManagerRegistry"/> 后，恢复执行器按本键
    /// 经注册表路由失效/刷新/重试全链路；未注册表或解析失败时回退构造注入实例（记 Warning）。</para>
    /// </summary>
    public string? TokenManagerKey { get; set; }

    /// <summary>
    /// 获取或设置令牌作用域集合，用于 401 恢复时按正确的作用域失效和刷新令牌。
    /// <para>TMR-04（D2 修订）：与取令牌路径同源，缺失时按默认作用域恢复。</para>
    /// <para>生成代码写入 <c>methodInfo.EffectiveTokenScopes</c> 的解析结果（G8-21：方法级 Scopes 优先于接口级，
    /// 与取令牌路径共用模型访问器，不再两处内联同一表达式）。</para>
    /// </summary>
    public string[]? Scopes { get; set; }

    /// <summary>
    /// R-P1-04：契约级显式放行"该请求可安全重放（幂等）"。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 默认 <c>false</c>。401 恢复对非幂等方法（POST/PATCH）默认不重放，以避免重复下单 / 重复扣款等副作用；
    /// 生成器可按接口 / 方法级声明（如具备幂等 upsert 语义的 POST）把本值写入请求属性，从而精确开洞。
    /// </para>
    /// <para>本标志优先级高于全局 <see cref="TokenRecoveryOptions.AllowNonIdempotentRecovery"/> 的<b>关闭</b>语义
    /// （即：全局关闭时，逐契约放行的请求仍可重放）。</para>
    /// </remarks>
    public bool IsRetryAllowedExplicitly { get; set; }

    /// <summary>
    /// 从请求中读取恢复上下文（netstandard2.0 走 <c>Properties</c>；其余 TFM 走 <c>Options</c> 并兼容回读 <c>Properties</c>）。
    /// </summary>
    /// <remarks>
    /// R-P1-05②：作为<b>单一实现点</b>供恢复执行器与 URL 脱敏共用，避免两处读取口径分裂。
    /// </remarks>
    /// <param name="request">HTTP 请求。</param>
    /// <returns>恢复上下文；未附加时返回 null。</returns>
    internal static TokenRecoveryContext? FromRequest(HttpRequestMessage request)
    {
        if (request is null)
            return null;

#if NETSTANDARD2_0
        return request.Properties.TryGetValue(PropertyKey, out var value)
            ? value as TokenRecoveryContext
            : null;
#else
        if (request.Options.TryGetValue(new HttpRequestOptionsKey<TokenRecoveryContext>(PropertyKey), out var value))
            return value;

        // 兼容历史写入路径：旧代码可能把上下文写在已过时的 Properties 上，此处刻意保留回读。
#pragma warning disable CS0618 // HttpRequestMessage.Properties 已过时
        if (request.Properties.TryGetValue(PropertyKey, out var legacyValue))
            return legacyValue as TokenRecoveryContext;
#pragma warning restore CS0618 // HttpRequestMessage.Properties 已过时
        return null;
#endif
    }
}
