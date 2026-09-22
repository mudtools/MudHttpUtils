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
        //
        // M6-HC-30：Key/Value 逐项经 HttpHeaderValueValidator.IsValid 校验（CR/LF），
        // 收敛 ns2.0 不校验头值的平台差异，阻断头部注入。不合格项跳过（不发射、不 Remove），
        // Debug 输出头名（不输出值，防敏感信息落日志）。与 [Header] string 分支的
        // GEN-18 同源：此处数据来自运行期字典（批量），采用跳过而非抛出，单个脏项不致整体请求失败。
        codeBuilder.AppendLine($"{indent}if ({parameter.Name} != null)");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    foreach (var __headerKvp in {parameter.Name})");
        codeBuilder.AppendLine($"{indent}    {{");
        codeBuilder.AppendLine($"{indent}        if (!string.IsNullOrWhiteSpace(__headerKvp.Key))");
        codeBuilder.AppendLine($"{indent}        {{");
        codeBuilder.AppendLine($"{indent}            var __headerValue = __headerKvp.Value?.ToString();");
        codeBuilder.AppendLine($"{indent}            if (!string.IsNullOrWhiteSpace(__headerValue))");
        codeBuilder.AppendLine($"{indent}            {{");
        codeBuilder.AppendLine($"{indent}                if (!global::Mud.HttpUtils.HttpHeaderValueValidator.IsValid(__headerKvp.Key) || !global::Mud.HttpUtils.HttpHeaderValueValidator.IsValid(__headerValue))");
        codeBuilder.AppendLine($"{indent}                {{");
        codeBuilder.AppendLine($"{indent}                    global::System.Diagnostics.Debug.WriteLine(\"[MudHttpUtils] HeaderCollection 项包含非法字符（CR/LF），已跳过: \" + __headerKvp.Key);");
        codeBuilder.AppendLine($"{indent}                }}");
        codeBuilder.AppendLine($"{indent}                else");
        codeBuilder.AppendLine($"{indent}                {{");
        codeBuilder.AppendLine($"{indent}                    __httpRequest.Headers.Remove(__headerKvp.Key);");
        codeBuilder.AppendLine($"{indent}                    __httpRequest.Headers.Add(__headerKvp.Key, __headerValue);");
        codeBuilder.AppendLine($"{indent}                }}");
        codeBuilder.AppendLine($"{indent}            }}");
        codeBuilder.AppendLine($"{indent}        }}");
        codeBuilder.AppendLine($"{indent}    }}");
        codeBuilder.AppendLine($"{indent}}}");
    }
}
