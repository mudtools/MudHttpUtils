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
    /// 脱敏 URL：剥离 userinfo，并掩码 query 中命中敏感词表的参数值。
    /// </summary>
    /// <param name="url">原始 URL（可为 null）。</param>
    /// <returns>脱敏后的 URL；无 query/userinfo 或未命中敏感键时与输入一致。</returns>
    public static string Redact(string? url)
    {
        if (url is null || url.Length == 0)
            return string.Empty;

        // M1-#5.3：运维开关。关闭时保留完整 URL（仅供已自行治理日志下游的排障场景）。
        // 不影响 URL 安全校验（始终用原始 URL）。
        if (!MudHttpObservabilityOptions.RedactUrlInTelemetry)
            return url;

        url = RedactUserInfo(url);

        var qIndex = url.IndexOf('?');
        if (qIndex < 0 || qIndex == url.Length - 1)
            return url;                    // 无 query，无需处理

        var basePart = url.Substring(0, qIndex + 1);
        var query = url.Substring(qIndex + 1);

        // 按 & 切分（key=value 或裸 key），key 命中敏感词表则掩码 value
        var redacted = string.Join("&", query.Split('&').Select(RedactOne));
        return basePart + redacted;
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

    private static string RedactOne(string pair)
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