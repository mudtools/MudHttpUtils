// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 字符串转义辅助工具
/// </summary>
internal static class StringEscapeHelper
{
    /// <summary>
    /// 转义字符串中的特殊字符，生成C#字符串字面量
    /// </summary>
    /// <param name="value">原始字符串（允许为 null，按空串处理）。</param>
    /// <returns>转义后的字符串；<paramref name="value"/> 为 null 时返回空串（绝不返回 null）。</returns>
    /// <remarks>
    /// [Phase4 修复 5.1] 两处修正：
    /// <list type="number">
    ///   <item>参数与返回语义一致化：原实现在 <paramref name="value"/> 为 null 时
    ///   <c>return value;</c> 会<strong>返回 null</strong>（违反非空返回契约，调用方产出的字面量会静默变空）；
    ///   现明确返回 <see cref="string.Empty"/>。</item>
    ///   <item>参数放宽为 <c>string?</c>：调用点普遍传入「可能为 null 的令牌键/内容类型」，
    ///   原签名迫使调用方在无实际校验意义处添加 <c>!</c>，或产生 CS8604 噪声。</item>
    /// </list>
    /// </remarks>
    public static string EscapeString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // 检查是否包含需要转义的字符，避免无转义需求时的 StringBuilder 分配
        bool needsEscape = false;
        for (int i = 0; i < value.Length; i++)
        {
            if (NeedsEscape(value[i]))
            {
                needsEscape = true;
                break;
            }
        }

        if (!needsEscape)
            return value;

        // 单次遍历完成所有转义，避免链式 Replace 产生的多次中间字符串分配
        var sb = new StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\"': sb.Append("\\\""); break;
                case '\0': sb.Append("\\0"); break;
                case '\a': sb.Append("\\a"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\v': sb.Append("\\v"); break;
                case '\u0085': sb.Append("\\u0085"); break;
                case '\u2028': sb.Append("\\u2028"); break;
                case '\u2029': sb.Append("\\u2029"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static bool NeedsEscape(char c)
    {
        return c == '\\' || c == '\"' || c == '\0' || c == '\a' ||
               c == '\b' || c == '\f' || c == '\n' || c == '\r' ||
               c == '\t' || c == '\v' ||
               c == '\u0085' || c == '\u2028' || c == '\u2029';
    }

    /// <summary>
    /// 转义字符中的特殊字符，生成C#字符字面量
    /// </summary>
    /// <param name="value">原始字符</param>
    /// <returns>转义后的字符字符串</returns>
    public static string EscapeChar(char value)
    {
        return value switch
        {
            '\\' => "\\\\",
            '\'' => "\\'",
            '\0' => "\\0",
            '\a' => "\\a",
            '\b' => "\\b",
            '\f' => "\\f",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\v' => "\\v",
            _ => value.ToString()
        };
    }

    /// <summary>
    /// 规范化事件类型字符串，确保输出为合法的字符串字面量
    /// </summary>
    /// <param name="eventType">原始事件类型值</param>
    /// <returns>规范化后的字符串字面量</returns>
    public static string NormalizeEventType(string eventType)
    {
        eventType = eventType?.Trim() ?? string.Empty;

        // 空字符串返回空字符串字面量
        if (string.IsNullOrEmpty(eventType))
            return "\"\"";

        // 检测是否已经是合法字符串字面量
        if (eventType.Length >= 2)
        {
            char quote = eventType[0];
            if ((quote == '"' || quote == '\'') && eventType[0] == eventType[eventType.Length - 1])
            {
                // 验证内部是否包含相同的未转义引号
                var innerContent = eventType.Substring(1, eventType.Length - 2);
                if (!ContainsUnescapedQuote(innerContent, quote))
                    return eventType; // 合法字面量，直接返回

                // 内部包含未转义引号，需要转义后重新包装
                var escapedInner = EscapeString(innerContent);
                // 使用双引号包装，内部的双引号已被 EscapeString 转义
                return $"\"{escapedInner}\"";
            }
        }

        // 转义特殊字符并包装成字符串字面量
        eventType = EscapeString(eventType);
        return $"\"{eventType}\"";
    }

    /// <summary>
    /// 检查字符串中是否包含未转义的指定引号字符
    /// </summary>
    /// <param name="content">字符串内容</param>
    /// <param name="quote">引号字符</param>
    /// <returns>如果存在未转义的引号返回 true，否则返回 false</returns>
    private static bool ContainsUnescapedQuote(string content, char quote)
    {
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] != quote)
                continue;

            // 统计前面的连续反斜杠数量
            int backslashCount = 0;
            for (int j = i - 1; j >= 0 && content[j] == '\\'; j--)
                backslashCount++;

            // 偶数个反斜杠表示引号未转义
            if (backslashCount % 2 == 0)
                return true;
        }
        return false;
    }
}
