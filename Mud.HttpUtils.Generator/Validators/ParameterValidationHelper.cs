// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
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
                param.IsValidated = true;
            }
        }
    }

    /// <summary>
    /// 判断参数是否需要验证
    /// </summary>
    private static bool ShouldValidateParameter(ParameterInfo param)
    {
        if (TypeDetectionHelper.IsCancellationToken(param.Type))
            return false;

        var hasBodyAttr = param.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.BodyAttribute);

        // 非可空值类型（int、long、Guid、DateTime 等）永远不会为 null，无需验证
        if (TypeDetectionHelper.IsValueType(param.Type) && !TypeDetectionHelper.IsNullableType(param.Type))
            return false;

        // [Body] 参数：引用类型和可空值类型需要验证（非可空值类型已在上方跳过）
        if (hasBodyAttr)
            return true;

        // 可空类型（string?、int?、MyClass? 等）：无需验证
        if (TypeDetectionHelper.IsNullableType(param.Type))
            return false;

        // 非可空 string：需要验证
        if (TypeDetectionHelper.IsStringType(param.Type))
        {
            if (param.HasDefaultValue && param.DefaultValue == null)
                return false;
            return true;
        }

        // 非可空、非简单类型的引用类型（如 MyClass）：需要验证
        if (!TypeDetectionHelper.IsSimpleType(param.Type))
        {
            if (param.HasDefaultValue && param.DefaultValue == null)
                return false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 为单个参数生成验证代码
    /// </summary>
    private static void GenerateSingleParameterValidation(StringBuilder codeBuilder, ParameterInfo param)
    {
        // string / string?：使用 IsNullOrWhiteSpace 检查
        if (TypeDetectionHelper.IsStringType(param.Type))
        {
            codeBuilder.AppendLine($"            if (string.IsNullOrWhiteSpace({param.Name}))");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                throw new ArgumentNullException(nameof({param.Name}));");
            codeBuilder.AppendLine($"            }}");
            return;
        }

        // 非可空值类型（int、long、Guid 等）：永远不会为 null，无需生成验证代码
        if (TypeDetectionHelper.IsValueType(param.Type) && !TypeDetectionHelper.IsNullableType(param.Type))
            return;

        // 可空值类型（int?、Guid? 等）和引用类型（MyClass 等）：使用 == null 检查
        codeBuilder.AppendLine($"            if ({param.Name} == null)");
        codeBuilder.AppendLine($"                throw new ArgumentNullException(nameof({param.Name}));");
    }


}
