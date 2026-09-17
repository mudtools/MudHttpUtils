using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 参数绑定器：处理 [HeaderCollection] 标记的字典参数，将字典键值对作为 HTTP 请求头批量添加。
/// </summary>
internal class HeaderCollectionParameterBinder : IParameterBinder
{
    public bool CanBind(ParameterInfo parameter)
    {
        return parameter.Attributes.Any(attr =>
            attr.Name == HttpClientGeneratorConstants.HeaderCollectionAttribute);
    }

    public void GenerateBindingCode(StringBuilder codeBuilder, ParameterInfo parameter, MethodAnalysisResult methodInfo, string indent)
    {
        // 遍历字典，将每个键值对添加为 HTTP 请求头
        // 支持 IDictionary<string, string?> 和 IDictionary<string, object?>
        codeBuilder.AppendLine($"{indent}if ({parameter.Name} != null)");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    foreach (var __headerKvp in {parameter.Name})");
        codeBuilder.AppendLine($"{indent}    {{");
        codeBuilder.AppendLine($"{indent}        if (!string.IsNullOrWhiteSpace(__headerKvp.Key))");
        codeBuilder.AppendLine($"{indent}        {{");
        codeBuilder.AppendLine($"{indent}            var __headerValue = __headerKvp.Value?.ToString();");
        codeBuilder.AppendLine($"{indent}            if (!string.IsNullOrWhiteSpace(__headerValue))");
        codeBuilder.AppendLine($"{indent}            {{");
        codeBuilder.AppendLine($"{indent}                __httpRequest.Headers.Remove(__headerKvp.Key);");
        codeBuilder.AppendLine($"{indent}                __httpRequest.Headers.Add(__headerKvp.Key, __headerValue);");
        codeBuilder.AppendLine($"{indent}            }}");
        codeBuilder.AppendLine($"{indent}        }}");
        codeBuilder.AppendLine($"{indent}    }}");
        codeBuilder.AppendLine($"{indent}}}");
    }
}
