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

        var isTokenParam = parameter.Attributes.Any(attr =>
            HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.Name));

        // G8-20：令牌参数（[Token] + [Header]，且未显式给头名）的默认头名 = **令牌注入头名**
        //（与注入路径共享 TokenMethodHelper.GetTokenHeaderName —— 有效级 Name，仅 Header/ApiKey 模式消费；
        // 未声明时回退 Authorization）。
        // 修复前：本分支读接口级 InterfaceTokenName 并把 Name 与 Scheme 字面量（"Bearer"/"Basic"）比对 ⇒
        // 分支不可达，头名落回**参数名**（如 token），服务端按 Name 指定的头取值必然 401。
        // 显式头名（[Header(Name/AliasAs)]）仍然最优先（GEN-05 契约不变）。
        if (isTokenParam && string.IsNullOrEmpty(explicitHeaderName))
        {
            var tokenMode = methodInfo.EffectiveTokenInjectionMode;
            var isHeaderCarryingMode =
                tokenMode == HttpClientGeneratorConstants.TokenInjectionModeHeader ||
                tokenMode == HttpClientGeneratorConstants.TokenInjectionModeApiKey;

            // 非 Header/ApiKey 令牌模式（Query/Cookie/BasicAuth/Path/HmacSignature）下，
            // [Header] 无名参数保持 GEN-05 的「参数名」回退，不扩大改动面。
            if (isHeaderCarryingMode)
                headerName = TokenMethodHelper.GetTokenHeaderName(methodInfo) ?? "Authorization";
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

    // G8-20：原私有 GetTokenHeaderName（读接口级 Name 并与 Scheme 字面量 "Bearer"/"Basic" 比对）已删除，
    // 统一改用 TokenMethodHelper.GetTokenHeaderName —— 单一事实源，与令牌注入路径同口径。

    private static string? GetFormatString(ParameterAttributeInfo attr)
    {
        if (attr.NamedArguments.TryGetValue("FormatString", out var fs) && fs is string f)
            return f;
        return attr.NamedArguments.TryGetValue("Format", out var format) && format is string s ? s : null;
    }
}
