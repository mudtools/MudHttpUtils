// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mud.HttpUtils;

/// <summary>
/// 信息脱敏工具类，用于在日志记录或调试过程中隐藏敏感信息，如令牌、密码、个人身份信息等。
/// </summary>
#if NET7_0_OR_GREATER
public static partial class MessageSanitizer
#else
public static class MessageSanitizer
#endif
{
    /// <summary>
    /// 敏感字段名集合（单一事实源位于 Abstractions 的 <c>SensitiveUrlRedactor</c>，URL query 脱敏共用同一词表）。
    /// </summary>
    private static HashSet<string> SensitiveFields => Helpers.SensitiveUrlRedactor.SensitiveFieldNames;

    /// <summary>
    /// M2-#18：统一的日志脱敏入口 —— 优先使用可插拔掩码器（<paramref name="masker"/>），
    /// 未注册时回退内置 <see cref="Sanitize(string, int)"/>。内置方法路径（EnhancedHttpClient）与
    /// 生成代码路径（DefaultHttpRequestExecutor）共用本方法，避免两条路径行为漂移。
    /// </summary>
    /// <param name="content">原始日志内容。</param>
    /// <param name="maxLength">输出最大长度（超长截断并追加 "...")。</param>
    /// <param name="masker">可插拔敏感数据掩码器（可为 null）。</param>
    /// <returns>脱敏后的内容。</returns>
    internal static string SanitizeWith(ISensitiveDataMasker? masker, string content, int maxLength)
    {
        if (masker != null)
        {
            var masked = masker.Mask(content);
            return masked.Length > maxLength ? masked.Substring(0, maxLength) + "..." : masked;
        }

        return Sanitize(content, maxLength: maxLength);
    }

    /// <summary>
    /// P3（M6 阶段五）：标识符脱敏 —— 令牌缓存键由 <c>userId + 分隔符 + scope</c> 组成（含 PII），
    /// 日志中不得明文输出。仅保留前 4 个字符（不足以还原原值，但保留跨日志行的关联能力），
    /// 其余以 <c>***</c> 替代并附原长度，便于判断是否同一键。
    /// </summary>
    /// <param name="value">待脱敏的标识符（可为 null / 空）。</param>
    /// <returns>脱敏后的标识符；null / 空输入返回空串。</returns>
    internal static string MaskIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Length <= 4)
            return "***";

        return value.Substring(0, 4) + "***(len=" + value.Length + ")";
    }

    private static readonly HashSet<string> NameSensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "real_name", "realName", "name"
    };

#if NET7_0_OR_GREATER
    [GeneratedRegex(@"^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$|^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$|^[A-Za-z0-9_\-]{20,}$", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?i)(token|password|secret|key)\s*[:=]\s*['""]?([^'""\s]{6,})['""]?", RegexOptions.Compiled)]
    private static partial Regex SensitiveKeyValuePattern();

    // M6-HC-06：PII 模式由 ^...$ 锚点（仅整串匹配才掩码）改为查找式 + 边界断言，
    // 嵌入在任意文本中的 PII（"手机号13812345678已注册"、"a@b.com 主体"）同样被掩码。
    // 各边界均为单字符类的前/后行断言，无嵌套量词，维持无 ReDoS 结论。
    [GeneratedRegex(@"(?<![0-9])1[3-9]\d{9}(?![0-9])")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"(?<![\w.+-])[\w.+-]+@[\w-]+\.[\w.-]+(?<!\.)")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<![0-9Xx])\d{17}[\dXx](?![0-9Xx])")]
    private static partial Regex IdCardPattern();
#else
    private static readonly Regex TokenPatternField = new Regex(
        @"^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$|" +
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$|" +
        @"^[A-Za-z0-9_\-]{20,}$",
        RegexOptions.Compiled);

    private static readonly Regex SensitiveKeyValuePatternField = new Regex(
        @"(?i)(token|password|secret|key)\s*[:=]\s*['""]?([^'""\s]{6,})['""]?",
        RegexOptions.Compiled);

    // M6-HC-06：与 NET7+ GeneratedRegex 分支同步（查找式 + 边界断言）。
    private static readonly Regex PhonePatternField = new Regex(@"(?<![0-9])1[3-9]\d{9}(?![0-9])", RegexOptions.Compiled);
    private static readonly Regex EmailPatternField = new Regex(@"(?<![\w.+-])[\w.+-]+@[\w-]+\.[\w.-]+(?<!\.)", RegexOptions.Compiled);
    private static readonly Regex IdCardPatternField = new Regex(@"(?<![0-9Xx])\d{17}[\dXx](?![0-9Xx])", RegexOptions.Compiled);

    private static Regex TokenPattern() => TokenPatternField;
    private static Regex SensitiveKeyValuePattern() => SensitiveKeyValuePatternField;
    private static Regex PhonePattern() => PhonePatternField;
    private static Regex EmailPattern() => EmailPatternField;
    private static Regex IdCardPattern() => IdCardPatternField;
#endif

    /// <summary>
    /// 信息脱敏方法，接受一个字符串输入，尝试将其解析为 JSON 对象，并对其中的敏感字段进行脱敏处理。如果解析失败，则对整个字符串进行正则表达式替换以隐藏敏感信息。最终返回脱敏后的字符串，长度超过指定最大值时会被截断并添加省略号。
    /// </summary>
    /// <param name="message">要脱敏的消息字符串。</param>
    /// <param name="maxLength">脱敏后字符串的最大长度，超过该长度将被截断并添加省略号。</param>
    /// <returns>脱敏后的字符串。</returns>
    public static string Sanitize(string message, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        try
        {
            var jsonNode = JsonNode.Parse(message, new JsonNodeOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (jsonNode == null)
                return SanitizePlainText(message, maxLength);

            var sanitized = SanitizeJsonNode(jsonNode, 0);
            var result = sanitized?.ToJsonString() ?? "{}";

            return result.Length > maxLength
                ? result.Substring(0, Math.Min(result.Length, maxLength)) + "..."
                : result;
        }
        catch (JsonException)
        {
            return SanitizePlainText(message, maxLength);
        }
    }

    private static JsonNode? SanitizeJsonNode(JsonNode? node, int depth)
    {
        if (node == null || depth > 32)
            return "***RECURSION_DEPTH_EXCEEDED***";

        switch (node)
        {
            case JsonObject obj:
                var newObj = new JsonObject();
                foreach (var property in obj)
                {
                    if (SensitiveFields.Contains(property.Key) || NameSensitiveFields.Contains(property.Key))
                    {
                        newObj[property.Key] = GetMaskedJsonValue(property.Key, property.Value);
                    }
                    else
                    {
                        newObj[property.Key] = SanitizeJsonNode(property.Value, depth + 1);
                    }
                }
                return newObj;

            case JsonArray arr:
                var newArr = new JsonArray();
                foreach (var item in arr)
                {
                    newArr.Add(SanitizeJsonNode(item, depth + 1));
                }
                return newArr;

            case JsonValue val when val.TryGetValue(out string? str):
                if (IsSensitiveString(str))
                    return "***";
                return JsonValue.Create(str);

            default:
                return node.DeepClone();
        }
    }

    private static JsonNode? GetMaskedJsonValue(string fieldName, JsonNode? value)
    {
        if (value is not JsonValue val || !val.TryGetValue(out string? str))
            return "***";

        if (string.IsNullOrEmpty(str))
            return value;

        if (IsPhoneField(fieldName))
            return MaskPhone(str);

        if (IsEmailField(fieldName))
            return MaskEmail(str);

        if (NameSensitiveFields.Contains(fieldName))
            return MaskName(str);

        if (str.Length <= 8)
            return "***";

        return $"{str.Substring(0, 4)}***{str.Substring(str.Length - 4)}";
    }

    private static bool IsSensitiveString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        return TokenPattern().IsMatch(value) ||
               PhonePattern().IsMatch(value) ||
               EmailPattern().IsMatch(value) ||
               IdCardPattern().IsMatch(value);
    }

    private static string SanitizePlainText(string text, int maxLength)
    {
        var patterns = new Dictionary<Regex, string>
        {
            [SensitiveKeyValuePattern()] = "$1: ***",
            [PhonePattern()] = "***",
            [EmailPattern()] = "***",
            [IdCardPattern()] = "***"
        };

        foreach (var pattern in patterns)
        {
            text = pattern.Key.Replace(text, pattern.Value);
        }

        return text.Length > maxLength ? text.Substring(0, Math.Min(text.Length, maxLength)) + "..." : text;
    }

    private static bool IsPhoneField(string fieldName)
    {
        return fieldName.Equals("phone", StringComparison.OrdinalIgnoreCase) ||
               fieldName.Equals("mobile", StringComparison.OrdinalIgnoreCase) ||
               fieldName.Equals("tel", StringComparison.OrdinalIgnoreCase) ||
               fieldName.Equals("telephone", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEmailField(string fieldName)
    {
        return fieldName.Equals("email", StringComparison.OrdinalIgnoreCase) ||
               fieldName.Equals("mail", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaskPhone(string phone)
    {
        if (phone.Length < 7) return "***";
        return $"{phone.Substring(0, 3)}****{phone.Substring(phone.Length - 4)}";
    }

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2) return "***";
        var name = parts[0];
        return $"{(name.Length > 0 ? name[0] : "*")}***@{parts[1]}";
    }

    private static string MaskName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "**";
        if (name.Length == 1) return "*";
        return $"{name[0]}*";
    }
}
