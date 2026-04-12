// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Validators;

/// <summary>
/// 参数验证辅助类，负责生成参数验证代码
/// </summary>
internal static class ParameterValidationHelper
{
    /// <summary>
    /// 为方法的所有参数生成验证代码
    /// </summary>
    /// <param name="codeBuilder">代码生成器</param>
    /// <param name="parameters">方法参数列表</param>
    public static void GenerateParameterValidation(StringBuilder codeBuilder, IReadOnlyList<ParameterInfo> parameters)
    {
        foreach (var param in parameters)
        {
            if (ShouldValidateParameter(param))
            {
                GenerateSingleParameterValidation(codeBuilder, param);
            }
        }
    }

    /// <summary>
    /// 判断参数是否需要验证
    /// </summary>
    private static bool ShouldValidateParameter(ParameterInfo param)
    {
        if (IsCancellationToken(param.Type))
            return false;

        if (param.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.BodyAttribute))
            return true;

        if (param.Type.EndsWith("?", StringComparison.Ordinal))
            return false;

        if (param.Type.EndsWith("string", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!TypeDetectionHelper.IsNullableType(param.Type) && !TypeDetectionHelper.IsSimpleType(param.Type))
            return true;

        return false;
    }

    private static bool IsCancellationToken(string typeName)
    {
        var name = typeName.TrimEnd('?');
        return string.Equals(name, "CancellationToken", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "System.Threading.CancellationToken", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 为单个参数生成验证代码
    /// </summary>
    private static void GenerateSingleParameterValidation(StringBuilder codeBuilder, ParameterInfo param)
    {
        var hasBodyAttr = param.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.BodyAttribute);

        if (hasBodyAttr)
        {
            codeBuilder.AppendLine($"            if ({param.Name} == null)");
            codeBuilder.AppendLine($"                throw new ArgumentNullException(nameof({param.Name}));");
        }
        else if (param.Type.EndsWith("string", StringComparison.OrdinalIgnoreCase))
        {
            var isPathOrQueryParam = IsPathOrQueryParam(param);
            codeBuilder.AppendLine($"            {param.Name} = {param.Name}.Trim();");
            codeBuilder.AppendLine($"            if (string.IsNullOrEmpty({param.Name}))");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                throw new ArgumentNullException(nameof({param.Name}));");
            codeBuilder.AppendLine($"            }}");
            if (isPathOrQueryParam)
            {
                codeBuilder.AppendLine($"            {param.Name} = Uri.EscapeDataString({param.Name});");
            }
        }
        else if (!TypeDetectionHelper.IsNullableType(param.Type) && !TypeDetectionHelper.IsSimpleType(param.Type))
        {
            codeBuilder.AppendLine($"            if ({param.Name} == null)");
            codeBuilder.AppendLine($"                throw new ArgumentNullException(nameof({param.Name}));");
        }
    }

    /// <summary>
    /// 判断参数是否为路径或查询参数（仅这两类需要 URL 编码）
    /// </summary>
    private static bool IsPathOrQueryParam(ParameterInfo param)
    {
        return param.Attributes.Any(attr =>
            HttpClientGeneratorConstants.PathAttributes.Contains(attr.Name) ||
            attr.Name == HttpClientGeneratorConstants.QueryAttribute ||
            attr.Name == HttpClientGeneratorConstants.ArrayQueryAttribute);
    }
}
