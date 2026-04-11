// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// 参数分析器，负责分析方法参数的特性和元数据
/// </summary>
internal static class ParameterAnalyzer
{
    /// <summary>
    /// 分析方法的所有参数
    /// </summary>
    public static List<ParameterInfo> AnalyzeParameters(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Parameters.Select(AnalyzeParameter).ToList();
    }

    /// <summary>
    /// 分析单个参数
    /// </summary>
    public static ParameterInfo AnalyzeParameter(IParameterSymbol parameter)
    {
        var parameterInfo = new ParameterInfo
        {
            Name = parameter.Name,
            Type = TypeSymbolHelper.GetTypeFullName(parameter.Type),
            Attributes = parameter.GetAttributes().Select(attr => new ParameterAttributeInfo
            {
                Name = attr.AttributeClass?.Name ?? "",
                Arguments = attr.ConstructorArguments.Select(arg => arg.Value).ToArray(),
                NamedArguments = attr.NamedArguments.ToDictionary(na => na.Key, na => na.Value.Value)
            }).ToList(),
            HasDefaultValue = parameter.HasExplicitDefaultValue,
            TokenType = GetTokenType(parameter)
        };

        if (parameter.HasExplicitDefaultValue)
        {
            parameterInfo.DefaultValue = parameter.ExplicitDefaultValue;
            parameterInfo.DefaultValueLiteral = TypeConverter.GetDefaultValueLiteral(parameter.Type, parameter.ExplicitDefaultValue);
        }

        return parameterInfo;
    }

    /// <summary>
    /// 获取参数的 Token 类型
    /// </summary>
    private static string GetTokenType(IParameterSymbol parameter)
    {
        var tokenAttribute = parameter.GetAttributes()
            .FirstOrDefault(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.AttributeClass?.Name));

        return TokenHelper.GetTokenTypeFromAttribute(tokenAttribute) ?? TokenHelper.GetDefaultTokenType();
    }
}
