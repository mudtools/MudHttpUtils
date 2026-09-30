// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Helpers;

/// <summary>
/// URL 脱敏器（M1-#5）：剥离 userinfo 并掩码 query 中的敏感键值（如 access_token / api_key），
/// 用于 Span tag、日志、诊断事件与异常对象中的 URL 输出，防止凭据随遥测或异常泄漏。
/// </summary>
/// <remarks>
/// <para>
/// 默认只掩码<b>命中敏感词表的 value</b>，保留键名与 URL 结构（path、非敏感参数），
/// 兼顾排障可用性。
/// </para>
/// <para>
/// M6-HC-27：<c>scheme://user:pass@host</c> 形式的内嵌凭据（userinfo）在 query 处理之前先行剥离为
/// <c>scheme://***@host</c>，避免 Basic 凭据等随 URL 泄漏。
/// </para>
/// <para>
/// 无 query 且无 userinfo 的 URL 原样返回；未命中词表的参数原样保留。
/// </para>
/// <para>
/// 本类型位于 Abstractions（internal，经 <c>InternalsVisibleTo</c> 对 Client 可见），
/// 使异常工厂（<see cref="DefaultExceptionFactory"/>）与响应扩展（<see cref="ApiResponseExtensions"/>）
/// 也能在异常构造点统一脱敏，避免各层口径分裂。
/// </para>
/// </remarks>
internal static class SensitiveUrlRedactor
{
    private const string Mask = "***REDACTED***";

    /// <summary>
    /// 敏感字段名集合（单一事实源）。JSON 脱敏（<c>MessageSanitizer</c>）与 URL query 脱敏共用本词表，
    /// 不另行维护两份。约定：只读使用，任何代码不得修改本集合。
    /// </summary>
    internal static readonly HashSet<string> SensitiveFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "app_access_token", "appAccessToken", "token", "password", "secret",
        "access_token", "refresh_token", "auth_token", "session_token",
        "api_key", "apiKey", "private_key", "privateKey",
        "phone", "mobile", "tel", "telephone",
        "email", "mail",
        "id_card", "idcard", "id_number", "idNumber",
        "card_no", "card_number", "bank_card", "bankCard",
        "real_name", "realName",
        "住址",
        "passport", "driver_license",
        // M1-#5：补齐 URL query 中常见的敏感键（与 MessageSanitizer 共用本词表）
        "authorization", "client_secret", "signature", "sig",
        // P3（M6 阶段五）：补齐易漏的凭据类键名
        "pwd", "credential", "sessionid", "session_id", "bearer", "sign", "auth",
        // P3（M6 阶段五）：`code` / `nonce` / `address` 三个通用键名过于宽泛
        // （`code` 亦常为业务编码、`address` 亦常为网络地址），收窄为具体变体：
        "auth_code", "authorization_code", "verify_code", "sms_code", "captcha", "otp",
        "home_address", "detail_address", "billing_address", "shipping_address"
    };

    /// <summary>
    /// R-P1-05①：进程级"额外敏感键"登记表（Query 令牌注入模式的参数名为典型用法）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为何需要</b>：全局脱敏基于<b>词表匹配</b>，自定义 <see cref="TokenRecoveryContext.QueryParameterName"/>
    /// （如 <c>tk</c> / <c>_t</c>）不在词表中 ⇒ 令牌明文进入日志与遥测。
    /// </para>
    /// <para>
    /// 登记后的键<b>无论 <see cref="MudHttpObservabilityOptions.RedactUrlInTelemetry"/> 开关为何都强制掩码</b>
    /// —— 该开关只影响"词表外的未知参数"，不应成为已确认为凭据的键的逃生门。
    /// </para>
    /// <para>线程安全（<see cref="HashSet{T}"/> 的非并发写入由调用点的低频"登记一次"语义保证）；
    /// 重复登记同一键为幂等操作。</para>
    /// </remarks>
    private static readonly HashSet<string> ExtraSensitiveKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// R-P1-05①：登记一个进程级敏感键（幂等、线程安全、空值忽略）。
    /// </summary>
    /// <param name="key">参数名 / 字段名（大小写不敏感）。</param>
    public static void RegisterExtraSensitiveKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        lock (ExtraSensitiveKeys)
        {
            ExtraSensitiveKeys.Add(key);
        }
    }

    /// <summary>
    /// 脱敏 URL：剥离 userinfo，并掩码 query 中命中敏感词表的参数值。
    /// </summary>
    /// <param name="url">原始 URL（可为 null）。</param>
    /// <returns>脱敏后的 URL；无 query/userinfo 或未命中敏感键时与输入一致。</returns>
    public static string Redact(string? url) => Redact(url, null);

    /// <summary>
    /// R-P1-05②：脱敏 URL，并额外掩码调用点指定的敏感键（请求级精确，避免词表全局化的误伤）。
    /// </summary>
    /// <param name="url">原始 URL（可为 null）。</param>
    /// <param name="extraKeys">
    /// 本次调用必须掩码的参数名集合（可 null）。与 <see cref="ExtraSensitiveKeys"/> 同为强制项：
    /// <b>不受</b> <see cref="MudHttpObservabilityOptions.RedactUrlInTelemetry"/> 开关约束。
    /// </param>
    /// <returns>脱敏后的 URL。</returns>
    public static string Redact(string? url, IReadOnlyCollection<string>? extraKeys)
    {
        if (url is null || url.Length == 0)
            return string.Empty;

        // M1-#5.3：运维开关。关闭时保留完整 URL（仅供已自行治理日志下游的排障场景）。
        // 不影响 URL 安全校验（始终用原始 URL）。
        var globalRedaction = MudHttpObservabilityOptions.RedactUrlInTelemetry;
        var hasMandatoryKeys = ExtraSensitiveKeys.Count > 0 || extraKeys is { Count: > 0 };

        // 全局关闭且无强制登记键 ⇒ 与历史行为逐字节一致。
        if (!globalRedaction && !hasMandatoryKeys)
            return url;

        // userinfo 剥离仍只随全局开关生效（保持该开关的既有语义边界）。
        if (globalRedaction)
            url = RedactUserInfo(url);

        var qIndex = url.IndexOf('?');
        if (qIndex < 0 || qIndex == url.Length - 1)
            return url;                    // 无 query，无需处理

        var basePart = url.Substring(0, qIndex + 1);
        var query = url.Substring(qIndex + 1);

        // 按 & 切分（key=value 或裸 key），key 命中敏感词表则掩码 value
        var redacted = string.Join("&", query.Split('&').Select(pair => RedactOne(pair, extraKeys, globalRedaction)));
        return basePart + redacted;
    }

    /// <summary>
    /// R-P1-05②：按<b>请求上下文</b>脱敏 —— 从请求的 <see cref="TokenRecoveryContext"/> 推导
    /// Query 注入模式的参数名，作为本次调用的强制掩码键（请求级精确，无全局副作用）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为何复用恢复上下文而非新增请求属性</b>：生成器已把 <c>QueryParameterName</c> 写入
    /// <see cref="TokenRecoveryContext"/>（<c>MethodGenerator</c> Query 模式分支），是同一信息的既有唯一来源。
    /// 再引入一个 <c>__mud_sensitive_query_keys</c> 属性通道在当前无任何生产者 ⇒ 属冗余代码，
    /// 故评审后<b>不采纳</b>原方案 ② 的"新增请求属性"部分（见修复方案 §0.3.2 修订 10）。
    /// </para>
    /// <para>
    /// <b>刻意不做进程级自动登记（修订 10 补记）</b>：进程级登记是<b>全局可变状态</b>，若由本方法自动触发，
    /// 则掩码行为将取决于"是否已有某个 Query 模式请求被处理过" ⇒ 行为随执行顺序变化、并会误伤同进程内
    /// 其它组件的 URL 日志（实测已导致 <c>SensitiveUrlRedactorTests.Redact_SwitchOff_PreservesFullUrl</c> 失败）。
    /// 进程级登记因此收敛为<b>显式 API</b>（<see cref="RegisterExtraSensitiveKey"/>），由生成器 / 宿主在启动期调用。
    /// </para>
    /// </remarks>
    /// <param name="request">当前请求。</param>
    /// <param name="url">待脱敏 URL。</param>
    /// <returns>脱敏后的 URL。</returns>
    internal static string RedactForRequest(HttpRequestMessage request, string? url)
    {
        var keys = CollectRequestSensitiveKeys(request);
        return Redact(url, keys);
    }

    /// <summary>
    /// R-P1-05②：收集请求级强制掩码键（当前来源为 Query 注入模式的 <c>QueryParameterName</c>）。
    /// </summary>
    private static List<string>? CollectRequestSensitiveKeys(HttpRequestMessage request)
    {
        var context = TokenRecoveryContext.FromRequest(request);
        if (context is not { InjectionMode: TokenInjectionMode.Query } ||
            string.IsNullOrEmpty(context.QueryParameterName))
        {
            return null;
        }

        return new List<string>(1) { context.QueryParameterName! };
    }

    /// <summary>
    /// R-P1-05：判断键是否为强制掩码项（进程级登记键 或 请求级登记键）。
    /// </summary>
    private static bool IsMandatorySensitiveKey(string key, IReadOnlyCollection<string>? extraKeys)
    {
        lock (ExtraSensitiveKeys)
        {
            if (ExtraSensitiveKeys.Contains(key))
                return true;
        }

        if (extraKeys is { Count: > 0 })
        {
            foreach (var candidate in extraKeys)
            {
                if (!string.IsNullOrEmpty(candidate) &&
                    string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// M6-HC-27：剥离 URL 中的 userinfo（<c>scheme://user:pass@host</c> → <c>scheme://***@host</c>）。
    /// 仅处理 scheme 之后、首个 path/query/fragment 分隔符之前的 authority 段，避免误伤 path 中的 '@'。
    /// </summary>
    private static string RedactUserInfo(string url)
    {
        var schemeIndex = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex < 0)
            return url;

        var authorityStart = schemeIndex + 3;
        if (authorityStart >= url.Length)
            return url;

        // authority 段终止于首个 '/'、'?' 或 '#'
        var authorityEnd = url.Length;
        for (var i = authorityStart; i < url.Length; i++)
        {
            var c = url[i];
            if (c == '/' || c == '?' || c == '#')
            {
                authorityEnd = i;
                break;
            }
        }

        var userInfoEnd = url.IndexOf('@', authorityStart);
        if (userInfoEnd < 0 || userInfoEnd >= authorityEnd)
            return url;                    // authority 段内无 userinfo

        return url.Substring(0, authorityStart) + "***@" + url.Substring(userInfoEnd + 1);
    }

    private static string RedactOne(string pair, IReadOnlyCollection<string>? extraKeys, bool globalRedaction)
    {
        if (pair.Length == 0)
            return pair;

        var eqIndex = pair.IndexOf('=');
        if (eqIndex < 0)
            return pair;                  // 裸 key 无值

        var key = pair.Substring(0, eqIndex);
        var value = pair.Substring(eqIndex + 1);

        // 值为空或已掩码的参数直接返回
        if (value.Length == 0 || value == Mask)
            return pair;

        // R-P1-05：强制掩码项（进程级 / 请求级登记键）不受全局开关约束。
        if (IsMandatorySensitiveKey(key, extraKeys))
            return key + "=" + Mask;

        if (!globalRedaction)
            return pair;                   // 全局脱敏关闭：词表外参数保留原文

        if (!IsSensitiveKey(key))
            return pair;                   // 非敏感键，保留原文（排障可用性）

        return key + "=" + Mask;
    }

    private static bool IsSensitiveKey(string key)
    {
        foreach (var field in SensitiveFieldNames)
        {
            if (string.Equals(key, field, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}