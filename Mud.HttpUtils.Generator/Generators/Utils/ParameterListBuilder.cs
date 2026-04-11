// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Models;

namespace Mud.HttpUtils.Generators.Utils;

/// <summary>
/// 参数列表构建器，统一处理参数列表生成相关逻辑
/// </summary>
internal static class ParameterListBuilder
{
    /// <summary>
    /// 检查参数是否具有指定的特性
    /// </summary>
    public static bool HasAttribute(ParameterInfo parameter, params string[] attributeNames)
    {
        if (parameter == null)
            return false;
        if (parameter.Attributes == null || parameter.Attributes.Count == 0)
            return false;
        return parameter.Attributes
            .Any(attr => attributeNames.Contains(attr.Name));
    }

    /// <summary>
    /// 根据特性名称过滤参数
    /// </summary>
    public static IReadOnlyList<ParameterInfo> FilterParametersByAttribute(
        IReadOnlyList<ParameterInfo> parameters, 
        string[] attributeNames, 
        bool exclude = false)
    {
        return exclude
            ? parameters.Where(p => !HasAttribute(p, attributeNames)).ToList()
            : parameters.Where(p => HasAttribute(p, attributeNames)).ToList();
    }

    /// <summary>
    /// 生成方法参数列表字符串
    /// </summary>
    public static string GenerateParameterList(IReadOnlyList<ParameterInfo> parameters)
    {
        if (parameters == null || !parameters.Any())
            return string.Empty;

        var parameterStrings = parameters.Select(parameter =>
        {
            var parameterStr = $"{parameter.Type} {parameter.Name}";

            // 处理可选参数
            if (parameter.HasDefaultValue && !string.IsNullOrEmpty(parameter.DefaultValueLiteral))
            {
                parameterStr += $" = {parameter.DefaultValueLiteral}";
            }

            return parameterStr;
        });

        return string.Join(", ", parameterStrings);
    }

    /// <summary>
    /// 生成正确的参数调用列表，确保token参数替换掉原来标记了[Token]特性的参数位置
    /// </summary>
    public static IReadOnlyList<string> GenerateCorrectParameterCallList(
        IReadOnlyList<ParameterInfo> originalParameters, 
        IReadOnlyList<ParameterInfo> filteredParameters, 
        string tokenParameterName)
    {
        var callParameters = new List<string>();

        foreach (var originalParam in originalParameters)
        {
            // 检查当前参数是否是Token参数
            if (HasAttribute(originalParam, HttpClientGeneratorConstants.TokenAttributeNames))
            {
                // 如果是Token参数，用token参数替换
                callParameters.Add(tokenParameterName);
            }
            else
            {
                // 如果不是Token参数，检查是否在过滤后的参数列表中
                var matchingFilteredParam = filteredParameters.FirstOrDefault(p => p.Name == originalParam.Name);
                if (matchingFilteredParam != null)
                {
                    callParameters.Add(matchingFilteredParam.Name);
                }
            }
        }

        return callParameters;
    }
}
