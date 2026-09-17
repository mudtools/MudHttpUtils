using Mud.HttpUtils;
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
        // GEN-05（B-1）：优先级 Name（命名参数或位置 0）> AliasAs > 参数名。
        // 此前只读 Arguments[0]，导致 [Header(Name = "X-Tenant")] / [Header(AliasAs = "...")] 落回参数名。
        var explicitHeaderName = AttributeArgumentReader.GetString(headerAttr, AttributeArgumentReader.ResolveNamePosition(headerAttr.Name), "Name")
            ?? AttributeArgumentReader.GetString(headerAttr, null, "AliasAs");
        var headerName = explicitHeaderName ?? parameter.Name;
        var formatString = GetFormatString(headerAttr);
        var replace = headerAttr.NamedArguments.TryGetValue("Replace", out var replaceVal) && replaceVal is true;

        string? interfaceHeaderName = GetTokenHeaderName(methodInfo);
        var isTokenParam = parameter.Attributes.Any(attr =>
            HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.Name));

        if (isTokenParam && !string.IsNullOrEmpty(interfaceHeaderName) && string.IsNullOrEmpty(explicitHeaderName))
        {
            headerName = interfaceHeaderName;
        }

        var headerMergeMode = methodInfo.HeaderMergeMode;
        var shouldReplace = replace || headerMergeMode == "Replace";
        var shouldIgnore = headerMergeMode == "Ignore";

        if (shouldIgnore)
            return;

        var escapedHeaderName = StringEscapeHelper.EscapeString(headerName);
        var isStringType = TypeDetectionHelper.IsStringType(parameter.Type);

        if (isStringType)
        {
            // [GEN-18][§8.6] 运行期守卫：net4x/netstandard2.0 编译的 HttpClient 不校验头值 CR/LF，
            // 这里显式拒绝含 CR/LF 的头值以阻止头部注入（与 .NET Core/5+ 的 HttpRequestHeaders.Add 行为对齐）。
            codeBuilder.AppendLine($"{indent}if (!global::Mud.HttpUtils.HttpHeaderValueValidator.IsValid({parameter.Name}))");
            codeBuilder.AppendLine($"{indent}    throw new global::System.ArgumentException(\"HTTP 头值包含非法字符（CR/LF）。\", nameof({parameter.Name}));");
            if (parameter.IsValidated)
            {
                if (shouldReplace)
                {
                    codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Remove(\"{escapedHeaderName}\");");
                    codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{escapedHeaderName}\", {parameter.Name});");
                }
                else
                {
                    codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{escapedHeaderName}\", {parameter.Name});");
                }
            }
            else
            {
                codeBuilder.AppendLine($"{indent}if (!string.IsNullOrWhiteSpace({parameter.Name}))");
                if (shouldReplace)
                {
                    codeBuilder.AppendLine($"{indent}    __httpRequest.Headers.Remove(\"{escapedHeaderName}\");");
                    codeBuilder.AppendLine($"{indent}    __httpRequest.Headers.Add(\"{escapedHeaderName}\", {parameter.Name});");
                }
                else
                {
                    codeBuilder.AppendLine($"{indent}    __httpRequest.Headers.Add(\"{escapedHeaderName}\", {parameter.Name});");
                }
            }
        }
        else
        {
            // FIX-05: formatString 必须转义，否则含 " 或 \ 的格式串会导致生成代码语法错误
            var escapedFormat = StringEscapeHelper.EscapeString(formatString);
            var formatExpression = !string.IsNullOrEmpty(formatString)
                ? $"string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{{0:{escapedFormat}}}\", {parameter.Name})"
                : $"{parameter.Name}.ToString()";
            if (shouldReplace)
            {
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Remove(\"{escapedHeaderName}\");");
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{escapedHeaderName}\", {formatExpression});");
            }
            else
            {
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{escapedHeaderName}\", {formatExpression});");
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
        if (attr.NamedArguments.TryGetValue("FormatString", out var fs) && fs is string f)
            return f;
        return attr.NamedArguments.TryGetValue("Format", out var format) && format is string s ? s : null;
    }
}
