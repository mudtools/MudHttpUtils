using System.Reflection;
using System.Text.Json;

namespace Mud.HttpUtils;

public class DefaultSensitiveDataMasker : ISensitiveDataMasker
{
    private const string MaskString = "***";
    private const string SensitiveDataAttributeName = "SensitiveDataAttribute";

    public string Mask(string value, SensitiveDataMaskMode mode = SensitiveDataMaskMode.Mask, int prefixLength = 2, int suffixLength = 2)
    {
        if (string.IsNullOrEmpty(value))
            return MaskString;

        switch (mode)
        {
            case SensitiveDataMaskMode.Hide:
                return MaskString;

            case SensitiveDataMaskMode.Mask:
                if (value.Length <= prefixLength + suffixLength)
                    return MaskString;

                var prefix = value.Substring(0, prefixLength);
                var suffix = value.Substring(value.Length - suffixLength);
                return $"{prefix}{MaskString}{suffix}";

            case SensitiveDataMaskMode.TypeOnly:
                return $"[String, Length={value.Length}]";

            default:
                return MaskString;
        }
    }

    public string MaskObject(object obj)
    {
        if (obj == null)
            return "null";

        var type = obj.GetType();

        if (type.IsPrimitive || type == typeof(string) || type == typeof(DateTime) || type == typeof(decimal))
        {
            return obj.ToString() ?? "null";
        }

        var maskedObject = new Dictionary<string, object?>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead)
                continue;

            object? value;
            try
            {
                value = property.GetValue(obj);
            }
            catch
            {
                continue;
            }

            var sensitiveAttr = property.GetCustomAttributesData()
                .FirstOrDefault(a => a.AttributeType.Name == SensitiveDataAttributeName);

            if (sensitiveAttr != null && value is string stringValue)
            {
                var mode = GetNamedArgument(sensitiveAttr, "MaskMode", SensitiveDataMaskMode.Mask);
                var prefixLen = GetNamedArgument(sensitiveAttr, "PrefixLength", 2);
                var suffixLen = GetNamedArgument(sensitiveAttr, "SuffixLength", 2);
                maskedObject[property.Name] = Mask(stringValue, mode, prefixLen, suffixLen);
            }
            else if (value != null && !property.PropertyType.IsPrimitive && property.PropertyType != typeof(string))
            {
                maskedObject[property.Name] = MaskObject(value);
            }
            else
            {
                maskedObject[property.Name] = value;
            }
        }

        return JsonSerializer.Serialize(maskedObject);
    }

    private static T GetNamedArgument<T>(CustomAttributeData attr, string name, T defaultValue)
    {
        var arg = attr.NamedArguments.FirstOrDefault(a => a.MemberName == name);
        if (arg.TypedValue.Value != null)
        {
            return (T)arg.TypedValue.Value;
        }
        return defaultValue;
    }
}
