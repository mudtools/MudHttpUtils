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
            GenerateSimpleQueryParameter(codeBuilder, param, paramName, formatString, indent);
        }
        else
        {
            GenerateComplexQueryParameter(codeBuilder, param, indent);
        }
    }

    private static void GenerateSimpleQueryParameter(StringBuilder codeBuilder, ParameterInfo param, string paramName, string? formatString, string indent)
    {
        if (TypeDetectionHelper.IsArrayType(param.Type))
        {
            codeBuilder.AppendLine($"{indent}if ({param.Name} != null && {param.Name}.Any())");
            codeBuilder.AppendLine($"{indent}{{");
            codeBuilder.AppendLine($"{indent}    var __joinedValues = string.Join(\";\", {param.Name}.Where(__item => __item != null).Select(__item => __item.ToString()));");
            codeBuilder.AppendLine($"{indent}    __queryParams.Add(\"{paramName}\", __joinedValues);");
            codeBuilder.AppendLine($"{indent}}}");
        }
        else if (TypeDetectionHelper.IsStringType(param.Type))
        {
            codeBuilder.AppendLine($"{indent}if (!string.IsNullOrWhiteSpace({param.Name}))");
            codeBuilder.AppendLine($"{indent}{{");
            codeBuilder.AppendLine($"{indent}    __queryParams.Add(\"{paramName}\", {param.Name});");
            codeBuilder.AppendLine($"{indent}}}");
        }
        else
        {
            if (TypeDetectionHelper.IsNullableType(param.Type))
            {
                codeBuilder.AppendLine($"{indent}if ({param.Name}.HasValue)");
                codeBuilder.AppendLine($"{indent}{{");
                var formatExpression = !string.IsNullOrEmpty(formatString)
                    ? $".Value.ToString(\"{formatString}\")"
                    : ".Value.ToString()";
                codeBuilder.AppendLine($"{indent}    __queryParams.Add(\"{paramName}\", {param.Name}{formatExpression});");
                codeBuilder.AppendLine($"{indent}}}");
            }
            else
            {
                var formatExpression = !string.IsNullOrEmpty(formatString)
                    ? $".ToString(\"{formatString}\")"
                    : ".ToString()";
                codeBuilder.AppendLine($"{indent}__queryParams.Add(\"{paramName}\", {param.Name}{formatExpression});");
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
        var separator = attr.NamedArguments.TryGetValue("Separator", out var sep) && sep is string s ? s : ",";

        codeBuilder.AppendLine($"{indent}if ({param.Name} != null && {param.Name}.Any())");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    var __joinedValues = string.Join(\"{separator}\", {param.Name}.Where(__item => __item != null).Select(__item => __item.ToString()));");
        codeBuilder.AppendLine($"{indent}    __queryParams.Add(\"{paramName}\", __joinedValues);");
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
        codeBuilder.AppendLine($"{indent}    FlattenObjectToQueryParams({param.Name}, string.Empty, \"{separator}\", __queryParams, {includeNull.ToString().ToLowerInvariant()}, {useJson.ToString().ToLowerInvariant()}, {urlEncode.ToString().ToLowerInvariant()}, __rawQueryPairs);");
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
