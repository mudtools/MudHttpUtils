// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Helpers;

/// <summary>
/// URL 脱敏器（M1-#5）：掩码 query 中的敏感键值（如 access_token / api_key），
/// 用于 Span tag、日志与诊断事件中的 URL 输出，防止令牌随遥测泄漏。
/// </summary>
/// <remarks>
/// <para>
/// 默认只掩码<b>命中敏感词表的 value</b>，保留键名与 URL 结构（path、非敏感参数），
/// 兼顾排障可用性。敏感词表复用 <see cref="MessageSanitizer"/> 的字段词表，不另行维护。
/// </para>
/// <para>
/// 无 query 的 URL 原样返回；未命中词表的参数原样保留。
/// </para>
/// </remarks>
internal static class SensitiveUrlRedactor
{
    private const string Mask = "***REDACTED***";

    /// <summary>
    /// 脱敏 URL：掩码 query 中命中敏感词表的参数值。
    /// </summary>
    /// <param name="url">原始 URL（可为 null）。</param>
    /// <returns>脱敏后的 URL；无 query 或未命中敏感键时与输入一致。</returns>
    public static string Redact(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return url ?? string.Empty;

        var qIndex = url.IndexOf('?');
        if (qIndex < 0 || qIndex == url.Length - 1)
            return url;                    // 无 query，无需处理

        var basePart = url.Substring(0, qIndex + 1);
        var query = url.Substring(qIndex + 1);

        // 按 & 切分（key=value 或裸 key），key 命中敏感词表则掩码 value
        var redacted = string.Join("&", query.Split('&').Select(RedactOne));
        return basePart + redacted;
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
        foreach (var field in MessageSanitizer.SensitiveFieldNames)
        {
            if (string.Equals(key, field, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
