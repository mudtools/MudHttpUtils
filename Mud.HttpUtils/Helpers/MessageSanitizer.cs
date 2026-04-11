// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mud.HttpUtils;

/// <summary>
/// 消息脱敏工具
/// </summary>
public static class MessageSanitizer
{
    // 添加更多常见敏感字段
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "app_access_token", "appAccessToken", "token", "password", "secret",
        "access_token", "refresh_token", "auth_token", "session_token",
        "api_key", "apiKey", "private_key", "privateKey",
        "phone", "mobile", "tel", "telephone",
        "email", "mail",
        "id_card", "idcard", "id_number", "idNumber",
        "card_no", "card_number", "bank_card", "bankCard",
        "real_name", "realName", "name",
        "address", "住址",
        "passport", "driver_license"
    };

    // 添加敏感字段值模式匹配（正则表达式）
    private static readonly Regex TokenPattern = new Regex(
        @"^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$|" +  // Base64格式
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$|" +  // UUID
        @"^[A-Za-z0-9_\-]{20,}$",  // 长token格式
        RegexOptions.Compiled);

    private static readonly Regex PhonePattern = new Regex(@"^1[3-9]\d{9}$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex IdCardPattern = new Regex(@"^\d{17}[\dXx]$", RegexOptions.Compiled);

    /// <summary>
    /// 脱敏消息内容（改进版）
    /// </summary>
    public static string Sanitize(string message, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        try
        {
            using var json = JsonDocument.Parse(message, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var sanitized = SanitizeJsonElement(json.RootElement);
            var result = sanitized.ToString();

            // 脱敏后再截断
            return result.Length > maxLength ?
                result.Substring(0, Math.Min(result.Length, maxLength)) + "..." :
                result;
        }
        catch (JsonException)
        {
            // 不是JSON格式，进行文本脱敏
            return SanitizePlainText(message, maxLength);
        }
    }

    /// <summary>
    /// 递归脱敏JSON元素（优化性能版）
    /// </summary>
    private static JsonElement SanitizeJsonElement(JsonElement element, int depth = 0)
    {
        // 防止深度递归导致栈溢出
        if (depth > 32) return JsonSerializer.Deserialize<JsonElement>("\"***RECURSION_DEPTH_EXCEEDED***\"");

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, JsonElement>();
                foreach (var property in element.EnumerateObject())
                {
                    if (SensitiveFields.Contains(property.Name))
                    {
                        // 根据字段名选择不同的脱敏策略
                        dict[property.Name] = GetMaskedValue(property.Name, property.Value);
                    }
                    else
                    {
                        dict[property.Name] = SanitizeJsonElement(property.Value, depth + 1);
                    }
                }
                return JsonSerializer.SerializeToElement(dict);

            case JsonValueKind.Array:
                var list = new List<JsonElement>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(SanitizeJsonElement(item, depth + 1));
                }
                return JsonSerializer.SerializeToElement(list);

            case JsonValueKind.String:
                var strValue = element.GetString();
                if (IsSensitiveString(strValue))
                {
                    return JsonSerializer.Deserialize<JsonElement>("\"***\"");
                }
                return element;

            default:
                return element;
        }
    }

    /// <summary>
    /// 根据字段名获取脱敏值
    /// </summary>
    private static JsonElement GetMaskedValue(string fieldName, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return JsonSerializer.Deserialize<JsonElement>("\"***\"");

        var str = value.GetString();
        if (string.IsNullOrEmpty(str))
            return value;

        // 根据不同字段类型采用不同的脱敏策略
        if (fieldName.IndexOf("phone", StringComparison.OrdinalIgnoreCase) >= 0 ||
            fieldName.IndexOf("mobile", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // 手机号保留前3后4
            return JsonSerializer.Deserialize<JsonElement>($"\"{MaskPhone(str)}\"");
        }
        else if (fieldName.IndexOf("email", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fieldName.IndexOf("mail", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // 邮箱保留前1位和域名
            return JsonSerializer.Deserialize<JsonElement>($"\"{MaskEmail(str)}\"");
        }
        else if (fieldName.IndexOf("name", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // 姓名保留第一个字
            return JsonSerializer.Deserialize<JsonElement>($"\"{MaskName(str)}\"");
        }
        else if (str.Length <= 8)
        {
            // 短值完全脱敏
            return JsonSerializer.Deserialize<JsonElement>("\"***\"");
        }
        else
        {
            // 长token保留前4后4
            return JsonSerializer.Deserialize<JsonElement>($"\"{str.Substring(0, 4)}***{str.Substring(str.Length - 4)}\"");
        }
    }

    /// <summary>
    /// 判断字符串是否为敏感信息（改进版）
    /// </summary>
    private static bool IsSensitiveString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        // 检查常见敏感数据格式
        return TokenPattern.IsMatch(value) ||
               PhonePattern.IsMatch(value) ||
               EmailPattern.IsMatch(value) ||
               IdCardPattern.IsMatch(value);
    }

    /// <summary>
    /// 纯文本脱敏（用于非JSON格式）
    /// </summary>
    private static string SanitizePlainText(string text, int maxLength)
    {
        // 简单的正则替换
        var patterns = new Dictionary<Regex, string>
        {
            [new Regex(@"(?i)(token|password|secret|key)\s*[:=]\s*['\""]?([^'\""\s]{6,})['\""]?")] = "$1: ***",
            [PhonePattern] = "***",
            [EmailPattern] = "***",
            [IdCardPattern] = "***"
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