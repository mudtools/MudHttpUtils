// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.CodeGenerator;

/// <summary>
/// 字符串转义辅助工具
/// </summary>
internal static class StringEscapeHelper
{
    /// <summary>
    /// 转义字符串中的特殊字符，生成C#字符串字面量
    /// </summary>
    /// <param name="value">原始字符串</param>
    /// <returns>转义后的字符串</returns>
    public static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\0", "\\0")
                    .Replace("\a", "\\a")
                    .Replace("\b", "\\b")
                    .Replace("\f", "\\f")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t")
                    .Replace("\v", "\\v");
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
                if (!innerContent.Contains(quote))
                    return eventType; // 合法字面量
                else
                    // 包含未转义的引号，需要转义
                    eventType = innerContent.Replace(quote.ToString(), $"\\{quote}");
            }
        }

        // 转义特殊字符并包装成字符串字面量
        eventType = EscapeString(eventType);

        return $"\"{eventType}\"";
    }
}
