// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
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
    /// 所有已知的 HTTP 参数特性名称集合。
    /// 参数若未标注其中任何一个，且不属于特殊类型，则根据类型自动推断默认特性：
    /// 简单类型使用 [Query]，复杂类型使用 [Body]。
    /// </summary>
    private static readonly HashSet<string> KnownHttpParameterAttributes = new(StringComparer.Ordinal)
    {
        // Path / Route
        "PathAttribute", "Path", "RouteAttribute", "Route",
        // Query 系列
        "QueryAttribute", "Query",
        "ArrayQueryAttribute", "ArrayQuery",
        "QueryMapAttribute", "QueryMap",
        "RawQueryStringAttribute", "RawQueryString",
        // Header
        "HeaderAttribute", "Header",
        // Body 系列
        "BodyAttribute", "Body",
        "FormContentAttribute", "FormContent",
        "MultipartFormAttribute", "MultipartForm",
        "UploadAttribute", "Upload",
        "FormAttribute", "Form",
        "FilePathAttribute", "FilePath",
        // Token
        "TokenAttribute", "Token"
    };

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
            TypeSymbol = parameter.Type,
            Attributes = parameter.GetAttributes().Select(attr => new ParameterAttributeInfo
            {
                Name = attr.AttributeClass?.Name ?? "",
                Arguments = attr.ConstructorArguments.Select(arg => arg.Value).ToArray(),
                NamedArguments = attr.NamedArguments.ToDictionary(na => na.Key, na => na.Value.Value)
            }).ToList(),
            HasDefaultValue = parameter.HasExplicitDefaultValue
        };

        if (parameter.HasExplicitDefaultValue)
        {
            parameterInfo.DefaultValue = parameter.ExplicitDefaultValue;
            parameterInfo.DefaultValueLiteral = TypeConverter.GetDefaultValueLiteral(parameter.Type, parameter.ExplicitDefaultValue);
        }

        // 未标注任何 HTTP 参数特性的参数，根据类型自动推断默认特性
        ApplyDefaultAttributeIfNeeded(parameterInfo);

        return parameterInfo;
    }

    /// <summary>
    /// 对未标注任何 HTTP 参数特性的非特殊类型参数，根据参数类型添加合成的默认特性：
    /// <list type="bullet">
    /// <item>简单类型（string、int、Guid 等及其数组）：添加 [Query("参数名")]，作为查询参数处理。</item>
    /// <item>复杂类型（自定义对象、List 等）：添加 [Body]，作为请求体处理。</item>
    /// </list>
    /// <para>特殊类型（CancellationToken、IProgress&lt;T&gt; 等）不适用此默认行为。</para>
    /// </summary>
    private static void ApplyDefaultAttributeIfNeeded(ParameterInfo parameterInfo)
    {
        // 已标注 HTTP 参数特性的参数不处理
        if (parameterInfo.Attributes.Any(attr => KnownHttpParameterAttributes.Contains(attr.Name)))
            return;

        // CancellationToken 为特殊参数，不处理
        if (TypeDetectionHelper.IsCancellationToken(parameterInfo.Type))
            return;

        // IProgress<T> 为下载进度报告参数，不处理
        if (TypeDetectionHelper.IsIProgressType(parameterInfo.Type, out _))
            return;

        var attributes = parameterInfo.Attributes.ToList();

        if (TypeDetectionHelper.IsSimpleType(parameterInfo.Type))
        {
            // 简单类型：添加合成的 [Query] 特性，使用参数名作为查询参数名
            attributes.Add(new ParameterAttributeInfo
            {
                Name = HttpClientGeneratorConstants.QueryAttribute,
                Arguments = new object?[] { parameterInfo.Name },
                NamedArguments = new Dictionary<string, object?>()
            });
        }
        else
        {
            // 复杂类型：添加合成的 [Body] 特性，作为请求体处理（默认 JSON 序列化）
            attributes.Add(new ParameterAttributeInfo
            {
                Name = HttpClientGeneratorConstants.BodyAttribute,
                Arguments = Array.Empty<object?>(),
                NamedArguments = new Dictionary<string, object?>()
            });
        }

        parameterInfo.Attributes = attributes;
    }
}
