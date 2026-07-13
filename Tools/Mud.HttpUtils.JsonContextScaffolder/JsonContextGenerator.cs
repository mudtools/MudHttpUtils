// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.JsonContextScaffolder;

/// <summary>
/// JSON Context 生成结果。
/// </summary>
/// <param name="FileName">输出文件名（不含路径）。</param>
/// <param name="SourceCode">生成的 C# 源代码。</param>
/// <param name="ContextClassName">Context 类名。</param>
/// <param name="TypeCount">包含的类型数量。</param>
public record JsonContextFile(string FileName, string SourceCode, string ContextClassName, int TypeCount);

/// <summary>
/// 类型分组信息。
/// </summary>
internal record TypeGroup
{
    public required string SerializerClassName { get; init; }
    public required JsonNamingPolicyHint NamingPolicy { get; init; }
    public required string TargetNamespace { get; init; }
    public required List<INamedTypeSymbol> Types { get; init; }
}

/// <summary>
/// JSON Context 源文件生成器核心逻辑。
/// </summary>
/// <remarks>
/// 此类仅依赖 Roslyn <see cref="Compilation"/>，不依赖 MSBuild Workspace，便于单元测试。
/// 调用方负责加载项目/解决方案获取 <see cref="Compilation"/>，然后调用 <see cref="Generate"/>。
/// </remarks>
public class JsonContextGenerator
{
    private const string AttributeFullName = "Mud.HttpUtils.Attributes.HttpJsonSerializableAttribute";

    /// <summary>
    /// 生成 Context 源文件。
    /// </summary>
    /// <param name="compilation">Roslyn 编译单元。</param>
    /// <param name="defaultNamespace">默认命名空间（当类型无命名空间时使用）。</param>
    /// <param name="autoDerivedTypes">是否自动检测同程序集内的派生类并生成 [JsonDerivedType]。</param>
    public List<JsonContextFile> Generate(Compilation compilation, string defaultNamespace = "Generated", bool autoDerivedTypes = false)
    {
        var attributeSymbol = compilation.GetTypeByMetadataName(AttributeFullName);
        if (attributeSymbol == null)
            return [];

        // 1. 扫描所有标注类型
        var annotatedTypes = ScanAnnotatedTypes(compilation, attributeSymbol);
        if (annotatedTypes.Count == 0)
            return [];

        // 2. 检查 SerializerClassName 重复（AOT001）
        var duplicates = annotatedTypes
            .Where(t => !string.IsNullOrEmpty(t.SerializerClassName))
            .GroupBy(t => t.SerializerClassName!)
            .Where(g => g.Count() > 1)
            .ToList();

        // 3. 分组
        var groups = GroupTypes(annotatedTypes, defaultNamespace);

        // 4. 为每组生成源文件
        var files = new List<JsonContextFile>();
        foreach (var group in groups)
        {
            var file = GenerateContextFile(group, compilation, autoDerivedTypes);
            files.Add(file);
        }

        return files;
    }

    /// <summary>
    /// 扫描编译单元中所有标注 <c>[HttpJsonSerializable]</c> 的类型。
    /// </summary>
    private List<AnnotatedType> ScanAnnotatedTypes(Compilation compilation, INamedTypeSymbol attributeSymbol)
    {
        var result = new List<AnnotatedType>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            // 遍历所有类型声明节点
            foreach (var node in root.DescendantNodes())
            {
                var symbol = semanticModel.GetDeclaredSymbol(node) as INamedTypeSymbol;
                if (symbol == null)
                    continue;

                var attr = symbol.GetAttributes().FirstOrDefault(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeSymbol));

                if (attr == null)
                    continue;

                string? serializerClassName = null;
                var namingPolicy = JsonNamingPolicyHint.Default;

                foreach (var arg in attr.NamedArguments)
                {
                    if (arg.Key == "SerializerClassName" && arg.Value.Value is string s)
                        serializerClassName = s;
                    else if (arg.Key == "NamingPolicy" && arg.Value.Value != null)
                    {
                        var val = arg.Value.Value;
                        if (val is JsonNamingPolicyHint hint)
                            namingPolicy = hint;
                        else
                            namingPolicy = (JsonNamingPolicyHint)System.Convert.ToInt32(val);
                    }
                }

                result.Add(new AnnotatedType
                {
                    Symbol = symbol,
                    SerializerClassName = string.IsNullOrWhiteSpace(serializerClassName) ? null : serializerClassName,
                    NamingPolicy = namingPolicy
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 按 SerializerClassName 分组。留空的自动派生名称。
    /// </summary>
    private List<TypeGroup> GroupTypes(List<AnnotatedType> types, string defaultNamespace)
    {
        var groups = new Dictionary<string, TypeGroup>();

        foreach (var type in types)
        {
            var groupName = type.SerializerClassName ?? DeriveSerializerClassName(type.Symbol);

            if (!groups.TryGetValue(groupName, out var group))
            {
                var ns = type.Symbol.ContainingNamespace?.IsGlobalNamespace == true
                    ? defaultNamespace
                    : type.Symbol.ContainingNamespace?.ToDisplayString() ?? defaultNamespace;

                group = new TypeGroup
                {
                    SerializerClassName = groupName,
                    NamingPolicy = type.NamingPolicy,
                    TargetNamespace = ns,
                    Types = []
                };
                groups[groupName] = group;
            }
            else
            {
            // 已有分组：如果已有组为 Default 且当前类型显式指定了策略，则采用当前类型的
            if (group.NamingPolicy == JsonNamingPolicyHint.Default && type.NamingPolicy != JsonNamingPolicyHint.Default)
            {
                group = group with { NamingPolicy = type.NamingPolicy };
                groups[groupName] = group;
            }
            }

            group.Types.Add(type.Symbol);
        }

        return groups.Values.ToList();
    }

    /// <summary>
    /// 自动派生 SerializerClassName：{程序集简称}{顶层命名空间}。
    /// </summary>
    private static string DeriveSerializerClassName(INamedTypeSymbol symbol)
    {
        var assemblyName = symbol.ContainingAssembly?.Name ?? "App";
        // 取程序集名中第一个点前的部分作为简称
        var shortName = assemblyName.Split('.').Last();

        var ns = symbol.ContainingNamespace;
        var topNs = ns?.IsGlobalNamespace == true ? "" : ns?.Name ?? "";

        return string.IsNullOrEmpty(topNs) ? shortName : $"{shortName}{topNs}";
    }

    /// <summary>
    /// 为一个分组生成 JsonSerializerContext 源文件。
    /// </summary>
    private JsonContextFile GenerateContextFile(TypeGroup group, Compilation compilation, bool autoDerivedTypes)
    {
        var className = group.SerializerClassName + "JsonContext";
        var fileName = className + ".g.cs";

        var namingPolicy = group.NamingPolicy == JsonNamingPolicyHint.Default
            ? AutoDeriveNamingPolicy(group.Types)
            : group.NamingPolicy;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated> 由 Mud.HttpUtils.JsonContextScaffolder 生成。请勿手动修改。");
        sb.AppendLine("// 生成时间请通过 git blame 查看。重新生成：dotnet mud-jsonctx --project <path> </auto-generated>");
        sb.AppendLine("#if NET8_0_OR_GREATER");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine($"namespace {group.TargetNamespace};");
        sb.AppendLine();
        sb.AppendLine("[JsonSourceGenerationOptions(");
        sb.AppendLine("    PropertyNameCaseInsensitive = true,");
        sb.AppendLine($"    PropertyNamingPolicy = {GetNamingPolicyString(namingPolicy)},");
        sb.AppendLine("    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,");
        sb.AppendLine("    WriteIndented = false)]");

        foreach (var type in group.Types)
        {
            var typeofExpr = GetTypeOfExpression(type);
            sb.AppendLine($"[JsonSerializable(typeof({typeofExpr}))]");

            // P2.2: 自动检测同程序集内的派生类并生成 [JsonDerivedType]
            if (autoDerivedTypes)
            {
                var derivedTypes = FindDerivedTypes(compilation, type);
                foreach (var derived in derivedTypes)
                {
                    var derivedExpr = GetTypeOfExpression(derived);
                    sb.AppendLine($"[JsonSerializable(typeof({derivedExpr}))]");
                }
            }
        }

        sb.AppendLine($"internal partial class {className} : JsonSerializerContext");
        sb.AppendLine("{");
        sb.AppendLine("}");
        sb.AppendLine("#endif");

        return new JsonContextFile(fileName, sb.ToString(), className, group.Types.Count);
    }

    /// <summary>
    /// 在同程序集内查找直接继承自指定基类的派生类型（用于 P2.2 自动 [JsonDerivedType]）。
    /// </summary>
    private static List<INamedTypeSymbol> FindDerivedTypes(Compilation compilation, INamedTypeSymbol baseType)
    {
        var result = new List<INamedTypeSymbol>();

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                if (semanticModel.GetDeclaredSymbol(node) is not INamedTypeSymbol candidate)
                    continue;

                // 跳过自身
                if (SymbolEqualityComparer.Default.Equals(candidate, baseType))
                    continue;

                // 检查直接基类是否匹配
                if (candidate.BaseType != null &&
                    SymbolEqualityComparer.Default.Equals(candidate.BaseType, baseType))
                {
                    result.Add(candidate);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 自动推导命名策略：检测实体上的 [JsonPropertyName] 模式。
    /// 超过 50% 的实体使用 snake_case_lower → 返回 SnakeCaseLower，否则 CamelCase。
    /// </summary>
    private static JsonNamingPolicyHint AutoDeriveNamingPolicy(List<INamedTypeSymbol> types)
    {
        var snakeCaseRegex = new Regex(@"^[a-z][a-z0-9]*(_[a-z0-9]+)+$", RegexOptions.Compiled);
        int totalProps = 0;
        int snakeCaseProps = 0;

        foreach (var type in types)
        {
            foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
            {
                // 检查是否有 [JsonPropertyName] 特性
                var jsonPropNameAttr = prop.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonPropertyNameAttribute");

                if (jsonPropNameAttr != null)
                {
                    var name = jsonPropNameAttr.ConstructorArguments.FirstOrDefault().Value as string;
                    if (!string.IsNullOrEmpty(name))
                    {
                        totalProps++;
                        if (snakeCaseRegex.IsMatch(name))
                            snakeCaseProps++;
                    }
                }
            }
        }

        if (totalProps > 0 && (double)snakeCaseProps / totalProps > 0.5)
            return JsonNamingPolicyHint.SnakeCaseLower;

        return JsonNamingPolicyHint.CamelCase;
    }

    /// <summary>
    /// 获取类型的 typeof() 表达式，处理开放泛型（&lt;T&gt; → &lt;&gt;）。
    /// </summary>
    private static string GetTypeOfExpression(INamedTypeSymbol type)
    {
        var displayString = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

        // 开放泛型：将 <T>, <TKey, TValue> 等改写为 <>
        if (type.IsGenericType && type.TypeParameters.Length > 0)
        {
            // 匹配 <...> 中的类型参数名并替换为空
            var genericMatch = Regex.Match(displayString, @"<[^>]+>");
            if (genericMatch.Success)
                displayString = displayString.Replace(genericMatch.Value, "<>");
        }

        return displayString;
    }

    /// <summary>
    /// 将 <see cref="JsonNamingPolicyHint"/> 转为 STJ 的 <c>JsonKnownNamingPolicy</c> 字符串。
    /// </summary>
    private static string GetNamingPolicyString(JsonNamingPolicyHint hint) => hint switch
    {
        JsonNamingPolicyHint.CamelCase => "JsonKnownNamingPolicy.CamelCase",
        JsonNamingPolicyHint.SnakeCaseLower => "JsonKnownNamingPolicy.SnakeCaseLower",
        JsonNamingPolicyHint.SnakeCaseUpper => "JsonKnownNamingPolicy.SnakeCaseUpper",
        JsonNamingPolicyHint.KebabCaseLower => "JsonKnownNamingPolicy.KebabCaseLower",
        JsonNamingPolicyHint.KebabCaseUpper => "JsonKnownNamingPolicy.KebabCaseUpper",
        _ => "JsonKnownNamingPolicy.CamelCase"
    };

    /// <summary>
    /// 标注类型信息。
    /// </summary>
    private record AnnotatedType
    {
        public required INamedTypeSymbol Symbol { get; init; }
        public required string? SerializerClassName { get; init; }
        public required JsonNamingPolicyHint NamingPolicy { get; init; }
    }
}
