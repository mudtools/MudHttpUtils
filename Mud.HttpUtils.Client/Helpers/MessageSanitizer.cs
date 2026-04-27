// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mud.HttpUtils;

#if NET7_0_OR_GREATER
public static partial class MessageSanitizer
#else
public static class MessageSanitizer
#endif
{
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "app_access_token", "appAccessToken", "token", "password", "secret",
        "access_token", "refresh_token", "auth_token", "session_token",
        "api_key", "apiKey", "private_key", "privateKey",
        "phone", "mobile", "tel", "telephone",
        "email", "mail",
        "id_card", "idcard", "id_number", "idNumber",
        "card_no", "card_number", "bank_card", "bankCard",
        "real_name", "realName",
        "address", "住址",
        "passport", "driver_license"
    };

    private static readonly HashSet<string> NameSensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "real_name", "realName", "name"
    };

#if NET7_0_OR_GREATER
    [GeneratedRegex(@"^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$|^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$|^[A-Za-z0-9_\-]{20,}$", RegexOptions.IgnoreCase)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"(?i)(token|password|secret|key)\s*[:=]\s*['""]?([^'""\s]{6,})['""]?", RegexOptions.Compiled)]
    private static partial Regex SensitiveKeyValuePattern();

    [GeneratedRegex(@"^1[3-9]\d{9}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\d{17}[\dXx]$")]
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

    private static readonly Regex PhonePatternField = new Regex(@"^1[3-9]\d{9}$", RegexOptions.Compiled);
    private static readonly Regex EmailPatternField = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex IdCardPatternField = new Regex(@"^\d{17}[\dXx]$", RegexOptions.Compiled);

    private static Regex TokenPattern() => TokenPatternField;
    private static Regex SensitiveKeyValuePattern() => SensitiveKeyValuePatternField;
    private static Regex PhonePattern() => PhonePatternField;
    private static Regex EmailPattern() => EmailPatternField;
    private static Regex IdCardPattern() => IdCardPatternField;
#endif

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
                return node;

            default:
                return node;
        }
    }

    private static JsonNode? GetMaskedJsonValue(string fieldName, JsonNode? value)
    {
        if (value is not JsonValue val || !val.TryGetValue(out string? str))
            return "***";

        if (string.IsNullOrEmpty(str))
            return value;

        if (fieldName.IndexOf("phone", StringComparison.OrdinalIgnoreCase) >= 0 ||
            fieldName.IndexOf("mobile", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return MaskPhone(str);
        }

        if (fieldName.IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0 ||
            fieldName.IndexOf("mail", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return MaskEmail(str);
        }

        if (fieldName.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return MaskName(str);
        }

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
