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
/// 诊断严重级别。
/// </summary>
public enum ScaffolderDiagnosticSeverity
{
    /// <summary>信息。</summary>
    Info,
    /// <summary>警告。</summary>
    Warning,
    /// <summary>错误。</summary>
    Error
}

/// <summary>
/// Scaffolder 诊断信息（对应 AOT001-AOT005 诊断约定）。
/// </summary>
/// <param name="Id">诊断 ID（如 AOT001）。</param>
/// <param name="Severity">严重级别。</param>
/// <param name="Message">诊断消息。</param>
/// <param name="Location">相关位置（类型全名，可选）。</param>
public record ScaffolderDiagnostic(string Id, ScaffolderDiagnosticSeverity Severity, string Message, string? Location = null);

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
    private const string JsonDerivedTypeAttributeFullName = "System.Text.Json.Serialization.JsonDerivedTypeAttribute";
    private const string HttpClientApiAttributeFullName = "Mud.HttpUtils.Attributes.HttpClientApiAttribute";
    private const string BodyAttributeFullName = "Mud.HttpUtils.Attributes.BodyAttribute";

    /// <summary>
    /// 本次生成过程中产生的诊断信息。
    /// </summary>
    public List<ScaffolderDiagnostic> Diagnostics { get; } = [];

    /// <summary>
    /// 生成 Context 源文件。
    /// </summary>
    /// <param name="compilation">Roslyn 编译单元。</param>
    /// <param name="defaultNamespace">默认命名空间（当类型无命名空间时使用）。</param>
    /// <param name="autoDerivedTypes">是否自动检测同程序集内的派生类并生成 [JsonDerivedType]。</param>
    /// <param name="scanHttpClientApi">是否扫描 [HttpClientApi] 接口，自动发现返回类型和 [Body] 参数类型中的闭合泛型。</param>
    public List<JsonContextFile> Generate(
        Compilation compilation,
        string defaultNamespace = "Generated",
        bool autoDerivedTypes = false,
        bool scanHttpClientApi = true)
    {
        Diagnostics.Clear();

        var attributeSymbol = compilation.GetTypeByMetadataName(AttributeFullName);

        // 1. 扫描 [HttpJsonSerializable] 标注类型
        var annotatedTypes = attributeSymbol != null
            ? ScanAnnotatedTypes(compilation, attributeSymbol)
            : new List<AnnotatedType>();

        // 2. 扫描 [HttpClientApi] 接口返回类型和 [Body] 参数类型
        List<INamedTypeSymbol> discoveredTypes = [];
        if (scanHttpClientApi)
        {
            var annotatedSet = new HashSet<INamedTypeSymbol>(
                annotatedTypes.Select(a => a.Symbol),
                SymbolEqualityComparer.Default);
            discoveredTypes = ScanHttpClientApiTypes(compilation, annotatedSet);
        }

        // 3. 无标注类型且无发现类型时返回空
        if (annotatedTypes.Count == 0 && discoveredTypes.Count == 0)
            return [];

        // 4. 诊断检查（AOT001-AOT003，仅针对标注类型）
        if (annotatedTypes.Count > 0)
            CheckDiagnostics(annotatedTypes, compilation, autoDerivedTypes);

        // 5. 分组并生成
        var files = new List<JsonContextFile>();

        // 5a. 标注类型分组
        if (annotatedTypes.Count > 0)
        {
            var groups = GroupTypes(annotatedTypes, defaultNamespace);
            foreach (var group in groups)
            {
                var file = GenerateContextFile(group, compilation, autoDerivedTypes);
                files.Add(file);
            }
        }

        // 5b. [HttpClientApi] 发现类型分组（闭合泛型等）
        if (discoveredTypes.Count > 0)
        {
            var apiGroup = CreateDiscoveredTypeGroup(discoveredTypes, compilation, defaultNamespace);
            var file = GenerateContextFile(apiGroup, compilation, autoDerivedTypes);
            files.Add(file);

            Diagnostics.Add(new ScaffolderDiagnostic(
                "AOT004",
                ScaffolderDiagnosticSeverity.Info,
                $"[HttpClientApi] 接口扫描发现 {discoveredTypes.Count} 个类型（含闭合泛型），已自动纳入 {apiGroup.SerializerClassName}JsonContext。",
                null));
        }

        return files;
    }

    /// <summary>
    /// 运行 AOT 诊断检查。
    /// </summary>
    private void CheckDiagnostics(List<AnnotatedType> annotatedTypes, Compilation compilation, bool autoDerivedTypes)
    {
        // AOT001：同一 SerializerClassName 出现冲突的 NamingPolicy
        CheckDuplicateSerializerClassNameConflicts(annotatedTypes);

        // AOT002：开放泛型类型在低版本 TFM 上不可用
        CheckOpenGenericOnLegacyTfm(annotatedTypes);

        // AOT003：多态类型缺少 [JsonDerivedType]
        if (!autoDerivedTypes)
            CheckPolymorphismWithoutJsonDerivedType(annotatedTypes, compilation);
    }

    /// <summary>
    /// AOT001：检测同一 SerializerClassName 下存在冲突的 NamingPolicy 配置。
    /// </summary>
    private void CheckDuplicateSerializerClassNameConflicts(List<AnnotatedType> annotatedTypes)
    {
        var conflictGroups = annotatedTypes
            .Where(t => !string.IsNullOrEmpty(t.SerializerClassName))
            .GroupBy(t => t.SerializerClassName!)
            .Where(g => g.Select(t => t.NamingPolicy).Distinct().Count() > 1
                        && g.Any(t => t.NamingPolicy != JsonNamingPolicyHint.Default));
        ;

        foreach (var group in conflictGroups)
        {
            var policies = string.Join(", ", group.Select(t => $"{t.Symbol.ToDisplayString()}={t.NamingPolicy}"));
            Diagnostics.Add(new ScaffolderDiagnostic(
                "AOT001",
                ScaffolderDiagnosticSeverity.Warning,
                $"SerializerClassName '{group.Key}' 存在冲突的 NamingPolicy 配置：{policies}。同一 Context 内只能使用一个命名策略，当前采用第一个非 Default 值。建议统一配置或拆分为不同分组。",
                group.Key));
        }
    }

    /// <summary>
    /// AOT002：开放泛型类型在 net8.0 以下不支持源生成。
    /// </summary>
    private void CheckOpenGenericOnLegacyTfm(List<AnnotatedType> annotatedTypes)
    {
        foreach (var type in annotatedTypes)
        {
            if (type.Symbol.IsGenericType && type.Symbol.TypeParameters.Length > 0)
            {
                Diagnostics.Add(new ScaffolderDiagnostic(
                    "AOT002",
                    ScaffolderDiagnosticSeverity.Warning,
                    $"类型 '{type.Symbol.ToDisplayString()}' 是开放泛型，在 net8.0 以下不支持源生成开放泛型。生成的 Context 以 #if NET8_0_OR_GREATER 包裹，低版本将走反射兜底，AOT 下不可用。",
                    type.Symbol.ToDisplayString()));
            }
        }
    }

    /// <summary>
    /// AOT003：类型存在基类（多态）但未标注 [JsonDerivedType]。
    /// </summary>
    private void CheckPolymorphismWithoutJsonDerivedType(List<AnnotatedType> annotatedTypes, Compilation compilation)
    {
        var jsonDerivedTypeAttr = compilation.GetTypeByMetadataName(JsonDerivedTypeAttributeFullName);

        foreach (var type in annotatedTypes)
        {
            // 跳过值类型和没有基类（仅 object）的类型
            if (type.Symbol.BaseType == null ||
                type.Symbol.BaseType.SpecialType == SpecialType.System_Object)
                continue;

            // 检查是否有 [JsonDerivedType] 标注
            var hasJsonDerivedType = jsonDerivedTypeAttr != null &&
                type.Symbol.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, jsonDerivedTypeAttr));

            if (!hasJsonDerivedType)
            {
                Diagnostics.Add(new ScaffolderDiagnostic(
                    "AOT003",
                    ScaffolderDiagnosticSeverity.Warning,
                    $"类型 '{type.Symbol.ToDisplayString()}' 存在基类 '{type.Symbol.BaseType.ToDisplayString()}'（多态序列化），但未标注 [JsonDerivedType]。以基类反序列化派生类时源生成不含派生类型，可能丢字段。建议在同程序集内补充 [JsonDerivedType] 或使用 --auto-derived-types 选项自动补全。",
                    type.Symbol.ToDisplayString()));
            }
        }
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
    /// 扫描编译单元中所有标注 <c>[HttpClientApi]</c> 的接口，提取方法返回类型和 <c>[Body]</c> 参数类型。
    /// </summary>
    /// <remarks>
    /// 自动发现闭合泛型（如 <c>FeishuApiResult&lt;T&gt;</c>）和非标注的自定义类型，
    /// 将其纳入 JSON 源生成上下文，确保 AOT 下类型元数据完整。
    /// </remarks>
    /// <param name="compilation">Roslyn 编译单元。</param>
    /// <param name="annotatedSet">已通过 [HttpJsonSerializable] 标注的类型集合（用于去重）。</param>
    /// <returns>发现的需注册类型列表。</returns>
    private List<INamedTypeSymbol> ScanHttpClientApiTypes(
        Compilation compilation,
        HashSet<INamedTypeSymbol> annotatedSet)
    {
        var result = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        var httpClientApiAttr = compilation.GetTypeByMetadataName(HttpClientApiAttributeFullName);
        if (httpClientApiAttr == null)
            return [];

        var bodyAttr = compilation.GetTypeByMetadataName(BodyAttributeFullName);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                if (semanticModel.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol)
                    continue;

                if (typeSymbol.TypeKind != TypeKind.Interface)
                    continue;

                var hasHttpClientApi = typeSymbol.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpClientApiAttr));
                if (!hasHttpClientApi)
                    continue;

                // 扫描接口自身声明的方法（含继承链上的方法）
                var methods = typeSymbol.GetMembers().OfType<IMethodSymbol>();
                foreach (var method in methods)
                {
                    // 跳过属性访问器和事件访问器
                    if (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet)
                        continue;

                    // 返回类型
                    var returnType = UnwrapTaskType(method.ReturnType);
                    if (returnType is INamedTypeSymbol namedReturn)
                        CollectSerializableTypes(namedReturn, result, annotatedSet, compilation.Assembly);

                    // [Body] 参数类型
                    foreach (var param in method.Parameters)
                    {
                        var hasBody = bodyAttr != null && param.GetAttributes().Any(a =>
                            SymbolEqualityComparer.Default.Equals(a.AttributeClass, bodyAttr));
                        if (!hasBody)
                            continue;

                        if (param.Type is INamedTypeSymbol namedParam)
                            CollectSerializableTypes(namedParam, result, annotatedSet, compilation.Assembly);
                    }
                }
            }
        }

        return result.ToList();
    }

    /// <summary>
    /// 解包 <see cref="Task{T}"/> / <see cref="ValueTask{T}"/> 的类型参数。
    /// </summary>
    /// <param name="type">方法返回类型符号。</param>
    /// <returns>解包后的类型参数；若为无返回值的 Task 则返回 null；若非 Task 类型则原样返回。</returns>
    private static ITypeSymbol? UnwrapTaskType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
            return null;

        // Task<T> / ValueTask<T>
        if (named.IsGenericType && named.TypeArguments.Length == 1)
        {
            var fullName = named.OriginalDefinition.ToDisplayString();
            if (fullName is "System.Threading.Tasks.Task<T>" or "System.Threading.Tasks.ValueTask<T>")
                return named.TypeArguments[0];
        }

        // Task / ValueTask（无返回值）
        var nonGenericName = named.OriginalDefinition?.ToDisplayString();
        if (nonGenericName is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
            return null;

        // 非 Task 类型，原样返回
        return type;
    }

    /// <summary>
    /// 递归收集需要纳入 JSON 源生成的类型。
    /// </summary>
    /// <remarks>
    /// 处理规则：
    /// <list type="bullet">
    ///   <item>框架类型（System.* 命名空间、基元类型）跳过自身，但递归处理其类型参数</item>
    ///   <item>闭合泛型（如 <c>FeishuApiResult&lt;X&gt;</c>）始终注册，并递归处理类型参数</item>
    ///   <item>开放泛型定义跳过（无法直接注册）</item>
    ///   <item>非泛型类型：仅当来自当前程序集且未标注 <c>[HttpJsonSerializable]</c> 时注册</item>
    ///   <item><see cref="Nullable{T}"/> 自动解包内层类型</item>
    /// </list>
    /// </remarks>
    /// <param name="type">待收集的类型符号。</param>
    /// <param name="result">收集结果集合。</param>
    /// <param name="annotatedSet">已通过 [HttpJsonSerializable] 标注的类型集合（用于去重）。</param>
    /// <param name="currentAssembly">当前编译的程序集符号（用于判断类型来源）。</param>
    private static void CollectSerializableTypes(
        INamedTypeSymbol type,
        HashSet<INamedTypeSymbol> result,
        HashSet<INamedTypeSymbol> annotatedSet,
        IAssemblySymbol currentAssembly)
    {
        // 解包 Nullable<T>
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            if (type.TypeArguments.FirstOrDefault() is INamedTypeSymbol inner)
                CollectSerializableTypes(inner, result, annotatedSet, currentAssembly);
            return;
        }

        // 框架类型：跳过自身，但递归处理类型参数
        if (IsFrameworkType(type))
        {
            if (type.IsGenericType)
            {
                foreach (var arg in type.TypeArguments)
                {
                    if (arg is INamedTypeSymbol namedArg)
                        CollectSerializableTypes(namedArg, result, annotatedSet, currentAssembly);
                }
            }
            return;
        }

        // 跳过开放泛型定义
        if (type.IsGenericType && type.IsDefinition)
            return;

        // 闭合泛型：始终注册（无论来自哪个程序集），并递归处理类型参数
        if (type.IsGenericType && !type.IsDefinition)
        {
            result.Add(type);
            foreach (var arg in type.TypeArguments)
            {
                if (arg is INamedTypeSymbol namedArg)
                    CollectSerializableTypes(namedArg, result, annotatedSet, currentAssembly);
            }
            return;
        }

        // 非泛型类型：仅当来自当前程序集且未标注时注册
        // 来自引用程序集的类型应已由其自身的 [HttpJsonSerializable] Context 覆盖
        if (!SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, currentAssembly))
            return;
        if (annotatedSet.Contains(type))
            return;

        result.Add(type);
    }

    /// <summary>
    /// 判断类型是否为框架类型（基元类型、System.* 命名空间下的类型）。
    /// </summary>
    private static bool IsFrameworkType(INamedTypeSymbol type)
    {
        // 基元类型（int, string, bool, DateTime, etc.）
        if (type.OriginalDefinition.SpecialType != SpecialType.None)
            return true;

        var ns = type.ContainingNamespace?.ToDisplayString();
        if (ns is null or "System" || ns.StartsWith("System."))
            return true;

        return false;
    }

    /// <summary>
    /// 为 [HttpClientApi] 发现的类型创建分组。
    /// </summary>
    private static TypeGroup CreateDiscoveredTypeGroup(
        List<INamedTypeSymbol> types,
        Compilation compilation,
        string defaultNamespace)
    {
        var assemblyName = compilation.AssemblyName ?? "App";
        var shortName = assemblyName.Split('.').Last();
        var serializerClassName = $"{shortName}HttpClientApi";

        return new TypeGroup
        {
            SerializerClassName = serializerClassName,
            NamingPolicy = JsonNamingPolicyHint.Default, // 自动推导
            TargetNamespace = defaultNamespace,
            Types = types
        };
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
    /// 在同程序集内递归查找继承自指定基类的所有派生类型（用于 --auto-derived-types 自动 [JsonDerivedType]）。
    /// 采用广度优先遍历完整继承链，覆盖多层派生（如 Base → Mid → Leaf）。
    /// </summary>
    private static List<INamedTypeSymbol> FindDerivedTypes(Compilation compilation, INamedTypeSymbol baseType)
    {
        // 收集编译单元内声明的所有具名类型（仅遍历一次语法树）
        var allTypes = new List<INamedTypeSymbol>();
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            foreach (var node in syntaxTree.GetRoot().DescendantNodes())
            {
                if (semanticModel.GetDeclaredSymbol(node) is INamedTypeSymbol candidate)
                    allTypes.Add(candidate);
            }
        }

        // 从基类出发，逐层找出所有直接/间接派生类
        var result = new List<INamedTypeSymbol>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<INamedTypeSymbol>();
        queue.Enqueue(baseType);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var candidate in allTypes)
            {
                if (candidate.BaseType != null &&
                    SymbolEqualityComparer.Default.Equals(candidate.BaseType, current) &&
                    visited.Add(candidate))
                {
                    result.Add(candidate);
                    queue.Enqueue(candidate);
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

        // 开放泛型定义：将 <T>, <TKey, TValue> 等改写为 <>
        // 闭合泛型（如 FeishuApiResult<X>）保持原样，STJ 支持闭合泛型的 [JsonSerializable]
        if (type.IsGenericType && type.IsDefinition)
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
