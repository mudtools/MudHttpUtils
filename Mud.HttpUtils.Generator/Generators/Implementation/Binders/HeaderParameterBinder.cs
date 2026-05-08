using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Generators.Implementation;

internal class HeaderParameterBinder : IParameterBinder
{
    public bool CanBind(ParameterInfo parameter)
    {
        return parameter.Attributes.Any(attr =>
            attr.Name == HttpClientGeneratorConstants.HeaderAttribute);
    }

    public void GenerateBindingCode(StringBuilder codeBuilder, ParameterInfo parameter, MethodAnalysisResult methodInfo, string indent)
    {
        var headerAttr = parameter.Attributes.First(a => a.Name == HttpClientGeneratorConstants.HeaderAttribute);
        var headerName = headerAttr.Arguments.FirstOrDefault()?.ToString() ?? parameter.Name;
        var formatString = GetFormatString(headerAttr);
        var replace = headerAttr.NamedArguments.TryGetValue("Replace", out var replaceVal) && replaceVal is true;

        string? interfaceHeaderName = GetTokenHeaderName(methodInfo);
        var isTokenParam = parameter.Attributes.Any(attr =>
            HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.Name));

        if (isTokenParam && !string.IsNullOrEmpty(interfaceHeaderName))
        {
            var hasExplicitHeaderName = headerAttr.Arguments.Length > 0
                && !string.IsNullOrEmpty(headerAttr.Arguments[0]?.ToString());
            if (!hasExplicitHeaderName)
            {
                headerName = interfaceHeaderName;
            }
        }

        var headerMergeMode = methodInfo.HeaderMergeMode;
        var shouldReplace = replace || headerMergeMode == "Replace";
        var shouldIgnore = headerMergeMode == "Ignore";

        if (shouldIgnore)
            return;

        var isStringType = TypeDetectionHelper.IsStringType(parameter.Type);

        if (isStringType)
        {
            if (parameter.IsValidated)
            {
                if (shouldReplace)
                {
                    codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Remove(\"{headerName}\");");
                    codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{headerName}\", {parameter.Name});");
                }
                else
                {
                    codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{headerName}\", {parameter.Name});");
                }
            }
            else
            {
                codeBuilder.AppendLine($"{indent}if (!string.IsNullOrWhiteSpace({parameter.Name}))");
                if (shouldReplace)
                {
                    codeBuilder.AppendLine($"{indent}    __httpRequest.Headers.Remove(\"{headerName}\");");
                    codeBuilder.AppendLine($"{indent}    __httpRequest.Headers.Add(\"{headerName}\", {parameter.Name});");
                }
                else
                {
                    codeBuilder.AppendLine($"{indent}    __httpRequest.Headers.Add(\"{headerName}\", {parameter.Name});");
                }
            }
        }
        else
        {
            var formatExpression = !string.IsNullOrEmpty(formatString)
                ? $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0:{formatString}}}\", {parameter.Name})"
                : $"{parameter.Name}.ToString()";
            if (shouldReplace)
            {
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Remove(\"{headerName}\");");
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{headerName}\", {formatExpression});");
            }
            else
            {
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{headerName}\", {formatExpression});");
            }
        }
    }

    private static string? GetTokenHeaderName(MethodAnalysisResult methodInfo)
    {
        if (methodInfo.InterfaceTokenName == null)
            return null;

        return methodInfo.InterfaceTokenName switch
        {
            "Bearer" => "Authorization",
            "Basic" => "Authorization",
            _ => null
        };
    }

    private static string? GetFormatString(ParameterAttributeInfo attr)
    {
        return attr.NamedArguments.TryGetValue("Format", out var format) && format is string f ? f : null;
    }
}
