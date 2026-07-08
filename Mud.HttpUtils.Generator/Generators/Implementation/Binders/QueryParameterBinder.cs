using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Generators.Implementation;

internal class QueryParameterBinder : IParameterBinder
{
    public bool CanBind(ParameterInfo parameter)
    {
        return parameter.Attributes.Any(attr =>
            attr.Name == HttpClientGeneratorConstants.QueryAttribute ||
            attr.Name == HttpClientGeneratorConstants.ArrayQueryAttribute ||
            attr.Name == HttpClientGeneratorConstants.QueryMapAttribute ||
            attr.Name == HttpClientGeneratorConstants.RawQueryStringAttribute);
    }

    public void GenerateBindingCode(StringBuilder codeBuilder, ParameterInfo parameter, MethodAnalysisResult methodInfo, string indent)
    {
        var queryAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.QueryAttribute);
        if (queryAttr != null)
        {
            GenerateQueryParameter(codeBuilder, parameter, queryAttr, indent);
            return;
        }

        var arrayQueryAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.ArrayQueryAttribute);
        if (arrayQueryAttr != null)
        {
            GenerateArrayQueryParameter(codeBuilder, parameter, arrayQueryAttr, indent);
            return;
        }

        var queryMapAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.QueryMapAttribute);
        if (queryMapAttr != null)
        {
            GenerateQueryMapParameter(codeBuilder, parameter, queryMapAttr, indent);
            return;
        }

        var rawQueryAttr = parameter.Attributes.FirstOrDefault(a => a.Name == HttpClientGeneratorConstants.RawQueryStringAttribute);
        if (rawQueryAttr != null)
        {
            GenerateRawQueryStringParameter(codeBuilder, parameter, rawQueryAttr, indent);
        }
    }

    private static void GenerateQueryParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var paramName = GetQueryParameterName(attr, param.Name);
        var formatString = GetFormatString(attr);

        if (TypeDetectionHelper.IsSimpleType(param.Type))
        {
            GenerateSimpleQueryParameter(codeBuilder, param, paramName, formatString, attr, indent);
        }
        else
        {
            GenerateComplexQueryParameter(codeBuilder, param, indent);
        }
    }

    private static void GenerateSimpleQueryParameter(StringBuilder codeBuilder, ParameterInfo param, string paramName, string? formatString, ParameterAttributeInfo attr, string indent)
    {
        if (TypeDetectionHelper.IsArrayType(param.Type))
        {
            // [Query] 数组默认使用重复参数模式（与 Separator = null 行为一致）
            GenerateArrayQueryUrl(codeBuilder, param, paramName, attr, indent, defaultSeparator: null);
        }
        else if (TypeDetectionHelper.IsStringType(param.Type))
        {
            // Add() 内部已跳过 null/空白值，无需外部检查
            codeBuilder.AppendLine($"{indent}__queryParams.Add(\"{StringEscapeHelper.EscapeString(paramName)}\", {param.Name});");
        }
        else
        {
            if (TypeDetectionHelper.IsNullableType(param.Type))
            {
                // 使用 ?. 运算符，Add() 会跳过 null 值
                var formatExpression = !string.IsNullOrEmpty(formatString)
                    ? $"?.ToString(\"{formatString}\")"
                    : "?.ToString()";
                codeBuilder.AppendLine($"{indent}__queryParams.Add(\"{StringEscapeHelper.EscapeString(paramName)}\", {param.Name}{formatExpression});");
            }
            else
            {
                var formatExpression = !string.IsNullOrEmpty(formatString)
                    ? $".ToString(\"{formatString}\")"
                    : ".ToString()";
                codeBuilder.AppendLine($"{indent}__queryParams.Add(\"{StringEscapeHelper.EscapeString(paramName)}\", {param.Name}{formatExpression});");
            }
        }
    }

    private static void GenerateComplexQueryParameter(StringBuilder codeBuilder, ParameterInfo param, string indent)
    {
        codeBuilder.AppendLine($"{indent}if ({param.Name} != null)");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    FlattenObjectToQueryParams({param.Name}, string.Empty, \",\", __queryParams, false, true, true, __rawQueryPairs);");
        codeBuilder.AppendLine($"{indent}}}");
    }

    private static void GenerateArrayQueryParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var paramName = attr.Arguments.FirstOrDefault()?.ToString() ?? param.Name;
        GenerateArrayQueryUrl(codeBuilder, param, paramName, attr, indent, defaultSeparator: ",");
    }

    /// <summary>
    /// 生成数组查询参数的 URL 代码，支持分隔符模式和重复参数模式。
    /// </summary>
    /// <param name="codeBuilder">代码构建器。</param>
    /// <param name="param">参数信息。</param>
    /// <param name="paramName">查询参数名称。</param>
    /// <param name="attr">参数特性信息。</param>
    /// <param name="indent">缩进字符串。</param>
    /// <param name="defaultSeparator">当 Separator 未显式设置时的默认分隔符。null 表示默认使用重复参数模式（[Query] 的默认行为），非 null 表示使用分隔符模式（[ArrayQuery] 默认 ","）。</param>
    private static void GenerateArrayQueryUrl(StringBuilder codeBuilder, ParameterInfo param, string paramName, ParameterAttributeInfo attr, string indent, string? defaultSeparator)
    {
        // 从构造函数参数或命名参数中解析 Separator
        // ArrayQueryAttribute 构造函数: (string name, string? separator)
        // QueryAttribute 通过命名参数 Separator 设置
        string? separator = null;
        bool separatorExplicitlySet = false;

        // 检查构造函数参数（ArrayQueryAttribute 的第二个参数）
        if (attr.Arguments.Length > 1)
        {
            separator = attr.Arguments[1]?.ToString();
            separatorExplicitlySet = true;
        }

        // 检查命名参数（两种特性都支持）
        if (!separatorExplicitlySet && attr.NamedArguments.TryGetValue("Separator", out var sepVal))
        {
            separator = sepVal as string;
            separatorExplicitlySet = true;
        }

        // 计算最终生效的分隔符：显式设置则使用设置的值，未设置则使用默认值
        var effectiveSeparator = separatorExplicitlySet ? separator : defaultSeparator;

        codeBuilder.AppendLine($"{indent}if ({param.Name} != null && {param.Name}.Any())");
        codeBuilder.AppendLine($"{indent}{{");

        if (effectiveSeparator == null)
        {
            // 重复参数模式: query1=val1&query1=val2&query1=val3
            codeBuilder.AppendLine($"{indent}    foreach (var __item in {param.Name}.Where(__item => __item != null))");
            codeBuilder.AppendLine($"{indent}    {{");
            codeBuilder.AppendLine($"{indent}        __queryParams.Add(\"{StringEscapeHelper.EscapeString(paramName)}\", __item.ToString());");
            codeBuilder.AppendLine($"{indent}    }}");
        }
        else
        {
            // 分隔符模式: query1=val1;val2;val3
            codeBuilder.AppendLine($"{indent}    var __joinedValues = string.Join(\"{StringEscapeHelper.EscapeString(effectiveSeparator)}\", {param.Name}.Where(__item => __item != null).Select(__item => __item.ToString()));");
            codeBuilder.AppendLine($"{indent}    __queryParams.Add(\"{StringEscapeHelper.EscapeString(paramName)}\", __joinedValues);");
        }

        codeBuilder.AppendLine($"{indent}}}");
    }

    private static void GenerateQueryMapParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var isDictionaryType = IsDictionaryType(param.Type);

        if (isDictionaryType)
        {
            GenerateDictionaryQueryMapParameter(codeBuilder, param, attr, indent);
        }
        else
        {
            GenerateObjectQueryMapParameter(codeBuilder, param, attr, indent);
        }
    }

    private static void GenerateDictionaryQueryMapParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var urlEncode = !(attr.NamedArguments.TryGetValue("UrlEncode", out var urlEnc) && urlEnc is false);

        codeBuilder.AppendLine($"{indent}if ({param.Name} != null)");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    foreach (var __kvp in {param.Name})");
        codeBuilder.AppendLine($"{indent}    {{");
        if (urlEncode)
        {
            codeBuilder.AppendLine($"{indent}        __queryParams.Add(__kvp.Key, __kvp.Value?.ToString());");
        }
        else
        {
            codeBuilder.AppendLine($"{indent}        __rawQueryPairs.Add(__kvp.Key + \"=\" + __kvp.Value?.ToString());");
        }
        codeBuilder.AppendLine($"{indent}    }}");
        codeBuilder.AppendLine($"{indent}}}");
    }

    private static void GenerateObjectQueryMapParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var separator = attr.NamedArguments.TryGetValue("PropertySeparator", out var sep) && sep is string s && !string.IsNullOrEmpty(s) ? s : "_";
        var includeNull = attr.NamedArguments.TryGetValue("IncludeNullValues", out var incNull) && incNull is true;
        var useJson = attr.NamedArguments.TryGetValue("SerializationMethod", out var serMethod)
            && serMethod is int enumVal && enumVal != 0;
        var urlEncode = !(attr.NamedArguments.TryGetValue("UrlEncode", out var urlEnc) && urlEnc is false);

        codeBuilder.AppendLine($"{indent}if ({param.Name} != null)");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    FlattenObjectToQueryParams({param.Name}, string.Empty, \"{StringEscapeHelper.EscapeString(separator)}\", __queryParams, {includeNull.ToString().ToLowerInvariant()}, {useJson.ToString().ToLowerInvariant()}, {urlEncode.ToString().ToLowerInvariant()}, __rawQueryPairs);");
        codeBuilder.AppendLine($"{indent}}}");
    }

    private static bool IsDictionaryType(string type)
    {
        return type.StartsWith("System.Collections.Generic.IDictionary", StringComparison.Ordinal) ||
               type.StartsWith("System.Collections.Generic.Dictionary", StringComparison.Ordinal) ||
               type.StartsWith("IDictionary<", StringComparison.Ordinal) ||
               type.StartsWith("Dictionary<", StringComparison.Ordinal);
    }

    private static void GenerateRawQueryStringParameter(StringBuilder codeBuilder, ParameterInfo param, ParameterAttributeInfo attr, string indent)
    {
        var rawQsVar = $"__rawQS_{param.Name}";
        if (param.IsValidated)
        {
            codeBuilder.AppendLine($"{indent}var {rawQsVar} = {param.Name}.TrimStart('?', '&').TrimEnd('&');");
            codeBuilder.AppendLine($"{indent}if (!string.IsNullOrWhiteSpace({rawQsVar}))");
            codeBuilder.AppendLine($"{indent}{{");
            codeBuilder.AppendLine($"{indent}    var __separator = __url.Contains('?') ? \"&\" : \"?\";");
            codeBuilder.AppendLine($"{indent}    __url += __separator + {rawQsVar};");
            codeBuilder.AppendLine($"{indent}}}");
        }
        else
        {
            codeBuilder.AppendLine($"{indent}if (!string.IsNullOrWhiteSpace({param.Name}))");
            codeBuilder.AppendLine($"{indent}{{");
            codeBuilder.AppendLine($"{indent}    var {rawQsVar} = {param.Name}.TrimStart('?', '&').TrimEnd('&');");
            codeBuilder.AppendLine($"{indent}    if (!string.IsNullOrWhiteSpace({rawQsVar}))");
            codeBuilder.AppendLine($"{indent}    {{");
            codeBuilder.AppendLine($"{indent}        var __separator = __url.Contains('?') ? \"&\" : \"?\";");
            codeBuilder.AppendLine($"{indent}        __url += __separator + {rawQsVar};");
            codeBuilder.AppendLine($"{indent}    }}");
            codeBuilder.AppendLine($"{indent}}}");
        }
    }

    private static string GetQueryParameterName(ParameterAttributeInfo attr, string defaultName)
    {
        return attr.Arguments.FirstOrDefault()?.ToString() ?? defaultName;
    }

    private static string? GetFormatString(ParameterAttributeInfo attr)
    {
        return attr.NamedArguments.TryGetValue("Format", out var format) && format is string f ? f : null;
    }
}
