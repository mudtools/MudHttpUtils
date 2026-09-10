// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// AOT004 / AOT005 / AOT006 诊断分析器：检测 [HttpClientApi] 接口方法的请求/响应 DTO 与
/// [HttpJsonSerializable] 类型是否被任何已引用的 JsonSerializerContext 覆盖。
/// </summary>
/// <remarks>
/// <para>
/// 本分析器为<b>纯函数</b>：输入 <see cref="Compilation"/>，输出 <see cref="Diagnostic"/> 集合，
/// 不依赖 <c>SourceProductionContext</c>，因此可被单元测试直接调用（无需构造 GeneratorDriver）。
/// 生成器负责把返回的诊断上报。
/// </para>
/// <para>
/// 覆盖集合的收集支持 IDE（<c>CompilationReference</c>）与 CLI（<c>PortableExecutableReference</c>）
/// 两种引用形态，并递归命名空间，避免"DTO + Context 在依赖包"场景下的漏报/误报。
/// </para>
/// </remarks>
internal static class AotDtoCoverageAnalyzer
{
    private const string JsonSerializableAttributeFullName = "System.Text.Json.Serialization.JsonSerializableAttribute";
    private const string JsonSerializerContextFullName = "System.Text.Json.Serialization.JsonSerializerContext";
    private const string HttpClientApiAttributeFullName = "Mud.HttpUtils.Attributes.HttpClientApiAttribute";
    private const string HttpJsonSerializableAttributeFullName = "Mud.HttpUtils.Attributes.HttpJsonSerializableAttribute";
    private const string BodyAttributeFullName = "Mud.HttpUtils.Attributes.BodyAttribute";
    private const string QueryAttributeFullName = "Mud.HttpUtils.Attributes.QueryAttribute";
    private const string QueryMapAttributeFullName = "Mud.HttpUtils.Attributes.QueryMapAttribute";
    private const string SerializationMethodAttributeFullName = "Mud.HttpUtils.Attributes.SerializationMethodAttribute";

    /// <summary>
    /// 读取方法的 SerializationMethod（从 [SerializationMethod] 特性，方法级优先于接口级默认值）。
    /// 返回 "Json" / "Xml" / "FormUrlEncoded"。默认 "Json"。
    /// </summary>
    /// <remarks>
    /// 枚举名映射统一委托给 <see cref="MethodAnalyzer.ReadSerializationMethodName"/>（单一事实源），
    /// 避免"枚举数值字符串 vs 枚举名"再次分叉。
    /// </remarks>
    internal static string GetMethodSerializationMethod(IMethodSymbol method)
    {
        var methodAttr = method.GetAttributes().FirstOrDefault(IsSerializationMethodAttribute);
        var methodLevel = methodAttr != null ? MethodAnalyzer.ReadSerializationMethodName(methodAttr) : null;
        if (!string.IsNullOrEmpty(methodLevel))
            return methodLevel;

        var interfaceAttr = method.ContainingType.GetAttributes().FirstOrDefault(IsSerializationMethodAttribute);
        var interfaceLevel = interfaceAttr != null ? MethodAnalyzer.ReadSerializationMethodName(interfaceAttr) : null;

        return string.IsNullOrEmpty(interfaceLevel) ? "Json" : interfaceLevel;

        static bool IsSerializationMethodAttribute(AttributeData a)
            => a.AttributeClass?.Name == "SerializationMethodAttribute";
    }

    /// <summary>
    /// 构造用于 CodeFix 的诊断属性，携带待覆盖类型的完全限定名，
    /// 使 <c>Mud.HttpUtils.CodeFixes</c> 中的修复器能精确获知需要加入 JsonSerializerContext 的类型。
    /// </summary>
    private static ImmutableDictionary<string, string> TypeProps(INamedTypeSymbol type)
    {
        return ImmutableDictionary<string, string>.Empty
            .Add("TypeFullName", type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    /// <summary>
    /// 分析编译单元中所有 [HttpClientApi] 接口方法的 DTO 覆盖情况，返回 AOT004 / AOT005 诊断。
    /// </summary>
    /// <param name="compilation">编译单元。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>诊断集合（无问题或未配置 Context 时为空）。</returns>
    public static ImmutableArray<Diagnostic> Analyze(Compilation compilation, CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // 1. 收集所有已引用的 JsonSerializerContext 子类上的 [JsonSerializable] 类型集合
        var coveredTypes = CollectCoveredTypes(compilation, out var hasLocalContext);

        // 触发门控：仅当"本编译单元自身声明了 JsonSerializerContext"时才运行 AOT004/AOT005。
        // 原因：覆盖集合自 P1-4（ADR-03）起会同时扫描引用程序集，而 Mud.HttpUtils 各库内部
        // 都自带 internal Context（MudHttpJsonContext / OAuth2JsonContext / ProblemDetailsJsonContext …），
        // 因此"coveredTypes.Count == 0"不再是有效的门控——任何引用本库的工程都会命中。
        // 若不以"本地声明 Context"作为接入信号，所有未选择 AOT 源生成工作流的消费方
        // （本仓库的 HttpClientApiDemo / ResilienceDemo / HttpClientDemo 等）都会被大量噪音诊断淹没。
        // 注意：仍使用引用程序集解析出的覆盖集合做判定（跨程序集 DTO+Context 场景不误报，见 ADR-03）。
        if (!hasLocalContext || coveredTypes.Count == 0)
            return diagnostics.ToImmutable();

        // 2. 查找 HttpClientApiAttribute 符号
        var httpClientApiAttr = compilation.GetTypeByMetadataName(HttpClientApiAttributeFullName);
        if (httpClientApiAttr == null)
            return diagnostics.ToImmutable();

        // 3. 遍历所有标注 [HttpClientApi] 的接口
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (cancellationToken.IsCancellationRequested)
                return diagnostics.ToImmutable();

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);

            foreach (var interfaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            {
                var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDecl, cancellationToken);
                if (interfaceSymbol == null)
                    continue;

                var hasHttpClientApi = interfaceSymbol.GetAttributes()
                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpClientApiAttr));
                if (!hasHttpClientApi)
                    continue;

                // 4. 检查每个方法的 DTO 覆盖情况
                foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    if (cancellationToken.IsCancellationRequested)
                        return diagnostics.ToImmutable();

                    CheckMethodDtoCoverage(compilation, diagnostics, interfaceSymbol, method, coveredTypes);
                }
            }
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// AOT006 诊断：检测标注 [HttpJsonSerializable] 的类型是否未被任何 JsonSerializerContext 覆盖。
    /// </summary>
    /// <remarks>
    /// <para>与 AOT004 不同，本检查独立于 [HttpClientApi] 接口，凡是当前编译单元（即实体所在项目）
    /// 中标注了 [HttpJsonSerializable] 的类型都会被核验。覆盖缺失通常意味着 HttpJsonContextScaffolder
    /// 未运行或手写 Context 缺失——这正是“脚手架未接入构建”的编译期信号。</para>
    /// <para>仅核验当前编译单元内声明的类型（不含引用程序集中的类型），避免跨程序集误报：
    /// 实体项目应各自运行脚手架生成 internal Context 覆盖自身类型。</para>
    /// </remarks>
    public static ImmutableArray<Diagnostic> AnalyzeHttpJsonSerializableCoverage(Compilation compilation, CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var httpJsonSerializableAttr = compilation.GetTypeByMetadataName(HttpJsonSerializableAttributeFullName);
        if (httpJsonSerializableAttr == null)
            return diagnostics.ToImmutable();

        // 收集已覆盖类型（当前编译单元 + 引用程序集中的所有 Context）
        var coveredTypes = CollectCoveredTypes(compilation, out _);

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (cancellationToken.IsCancellationRequested)
                return diagnostics.ToImmutable();

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot(cancellationToken);

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (cancellationToken.IsCancellationRequested)
                    return diagnostics.ToImmutable();

                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken) as INamedTypeSymbol;
                if (typeSymbol == null)
                    continue;

                var hasHttpJsonSerializable = typeSymbol.GetAttributes()
                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, httpJsonSerializableAttr));
                if (!hasHttpJsonSerializable)
                    continue;

                // 已覆盖（含集合/Nullable 解包）或为基元/字符串/枚举 → 跳过
                if (IsCovered(typeSymbol, coveredTypes) || QuerySerializationClassifier.IsSimple(typeSymbol))
                    continue;

                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.AotJsonSerializableNotCovered,
                    typeSymbol.Locations.FirstOrDefault() ?? typeDecl.GetLocation(),
                    TypeProps(typeSymbol),
                    typeSymbol.ToDisplayString()));
            }
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// 收集编译单元引用的所有 JsonSerializerContext 子类上的 [JsonSerializable] 类型。
    /// </summary>
    /// <param name="compilation">编译单元。</param>
    /// <param name="hasLocalContext">
    /// 输出：当前编译单元是否<b>自身</b>声明了至少一个 <c>JsonSerializerContext</c> 子类。
    /// 用作 AOT004/AOT005 的触发门控（引用程序集中的 Context 只用于覆盖判定，不作为触发信号）。
    /// </param>
    /// <remarks>
    /// 同时支持 IDE（<see cref="CompilationReference"/>）与 CLI（<c>PortableExecutableReference</c>）
    /// 两种引用形态，并递归命名空间与嵌套类型。
    /// </remarks>
    private static HashSet<INamedTypeSymbol> CollectCoveredTypes(Compilation compilation, out bool hasLocalContext)
    {
        var result = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        hasLocalContext = false;

        var jsonSerializableAttr = compilation.GetTypeByMetadataName(JsonSerializableAttributeFullName);
        var jsonSerializerContext = compilation.GetTypeByMetadataName(JsonSerializerContextFullName);

        if (jsonSerializableAttr == null || jsonSerializerContext == null)
            return result;

        // 扫描当前编译单元中的所有类型
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (typeSymbol == null)
                    continue;

                if (InheritsFromJsonSerializerContext(typeSymbol, jsonSerializerContext))
                {
                    hasLocalContext = true;
                    CollectFromContextType(typeSymbol, jsonSerializableAttr, result);
                }
            }
        }

        // 扫描引用程序集中的 JsonSerializerContext 子类
        foreach (var reference in compilation.References)
        {
            // IDE: CompilationReference 直接可用；CLI: PortableExecutableReference 经符号解析
            var asm = reference is CompilationReference compRef
                ? compRef.Compilation.Assembly
                : compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
            if (asm is null)
                continue;

            CollectCoveredTypesFromNamespace(asm.GlobalNamespace, jsonSerializerContext, jsonSerializableAttr, result);
        }

        return result;
    }

    /// <summary>
    /// 从 Context 类型收集其 [JsonSerializable] 标注的类型。
    /// </summary>
    private static void CollectFromContextType(
        INamedTypeSymbol contextType,
        INamedTypeSymbol jsonSerializableAttr,
        HashSet<INamedTypeSymbol> result)
    {
        foreach (var attr in contextType.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, jsonSerializableAttr))
                continue;

            if (attr.ConstructorArguments.Length > 0 &&
                attr.ConstructorArguments[0].Value is INamedTypeSymbol coveredType)
            {
                result.Add(coveredType);
            }
        }
    }

    /// <summary>
    /// 递归扫描命名空间（含子命名空间与嵌套类型）中的 JsonSerializerContext 子类。
    /// </summary>
    private static void CollectCoveredTypesFromNamespace(
        INamespaceSymbol namespaceSymbol,
        INamedTypeSymbol jsonSerializerContext,
        INamedTypeSymbol jsonSerializableAttr,
        HashSet<INamedTypeSymbol> result)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            CollectCoveredTypesFromType(type, jsonSerializerContext, jsonSerializableAttr, result);
        }

        foreach (var child in namespaceSymbol.GetNamespaceMembers())
        {
            CollectCoveredTypesFromNamespace(child, jsonSerializerContext, jsonSerializableAttr, result);
        }
    }

    /// <summary>
    /// 递归处理类型及其嵌套类型。
    /// </summary>
    private static void CollectCoveredTypesFromType(
        INamedTypeSymbol type,
        INamedTypeSymbol jsonSerializerContext,
        INamedTypeSymbol jsonSerializableAttr,
        HashSet<INamedTypeSymbol> result)
    {
        if (InheritsFromJsonSerializerContext(type, jsonSerializerContext))
            CollectFromContextType(type, jsonSerializableAttr, result);

        foreach (var nested in type.GetTypeMembers())
        {
            CollectCoveredTypesFromType(nested, jsonSerializerContext, jsonSerializableAttr, result);
        }
    }

    /// <summary>
    /// 检查类型是否继承自 JsonSerializerContext。
    /// </summary>
    private static bool InheritsFromJsonSerializerContext(INamedTypeSymbol type, INamedTypeSymbol contextBase)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, contextBase))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// 检查单个方法的 DTO 覆盖情况（AOT004 + AOT005）。
    /// </summary>
    private static void CheckMethodDtoCoverage(
        Compilation compilation,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        INamedTypeSymbol interfaceSymbol,
        IMethodSymbol method,
        HashSet<INamedTypeSymbol> coveredTypes)
    {
        var bodyAttr = compilation.GetTypeByMetadataName(BodyAttributeFullName);
        var queryAttr = compilation.GetTypeByMetadataName(QueryAttributeFullName);
        var queryMapAttr = compilation.GetTypeByMetadataName(QueryMapAttributeFullName);

        foreach (var param in method.Parameters)
        {
            if (param.Type is not INamedTypeSymbol paramType)
                continue;

            var paramLocation = param.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                ?? method.Locations.FirstOrDefault();

            // 检查 [Body] 请求体 DTO — AOT004
            if (bodyAttr != null && param.GetAttributes()
                .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, bodyAttr)))
            {
                // FormUrlEncoded Body 不走 JSON 序列化，无需 JsonSerializerContext 覆盖，跳过 AOT004 检查。
                if (GetMethodSerializationMethod(method) == "FormUrlEncoded")
                    continue;

                if (!IsCovered(paramType, coveredTypes) && !QuerySerializationClassifier.IsSimple(paramType))
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.AotDtoNotCoveredByContext,
                        paramLocation,
                        TypeProps(paramType),
                        interfaceSymbol.Name,
                        method.Name,
                        paramType.ToDisplayString()));
                }
                continue; // [Body] 参数不会同时是 [Query]
            }

            // 检查 [Query] 复杂类型参数 — AOT005
            // 判定统一走 QuerySerializationClassifier（单一事实源），仅当"确实走 JSON 序列化"且未被覆盖时才报告。
            if (queryAttr != null && param.GetAttributes()
                .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, queryAttr)))
            {
                if (QuerySerializationClassifier.Classify(paramType) == QuerySerializationClassifier.Kind.JsonSerialized
                    && !IsCovered(paramType, coveredTypes))
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.AotQueryParameterNotInContext,
                        paramLocation,
                        TypeProps(paramType),
                        interfaceSymbol.Name,
                        method.Name,
                        param.Name,
                        paramType.ToDisplayString()));
                }
                continue;
            }

            // 检查 [QueryMap] + JSON 序列化参数 — AOT005
            if (queryMapAttr != null)
            {
                var queryMapAttribute = param.GetAttributes()
                    .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, queryMapAttr));

                if (queryMapAttribute != null)
                {
                    // 检查 SerializationMethod 是否为 Json (enum value != 0)
                    var serMethodArg = queryMapAttribute.NamedArguments
                        .FirstOrDefault(kvp => kvp.Key == "SerializationMethod");
                    var useJsonSerialization = serMethodArg.Value.Value is int enumVal && enumVal != 0;

                    // 仅当"JSON 序列化的 QueryMap 且类型确实走 JSON"且未被覆盖时才报告。
                    if (useJsonSerialization
                        && QuerySerializationClassifier.Classify(paramType) == QuerySerializationClassifier.Kind.JsonSerialized
                        && !IsCovered(paramType, coveredTypes))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            Diagnostics.AotQueryParameterNotInContext,
                            paramLocation,
                            TypeProps(paramType),
                            interfaceSymbol.Name,
                            method.Name,
                            param.Name,
                            paramType.ToDisplayString()));
                    }
                }
            }
        }

        // 检查响应 DTO — AOT004
        if (method.ReturnType is INamedTypeSymbol returnType)
        {
            var innerType = ExtractTaskInnerType(returnType);
            if (innerType != null && !IsCovered(innerType, coveredTypes) && !QuerySerializationClassifier.IsSimple(innerType))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.AotDtoNotCoveredByContext,
                    method.Locations.FirstOrDefault(),
                    TypeProps(innerType),
                    interfaceSymbol.Name,
                    method.Name,
                    innerType.ToDisplayString()));
            }
        }
    }

    /// <summary>
    /// 检查类型是否被 Context 覆盖（包括集合类型解包）。
    /// </summary>
    private static bool IsCovered(INamedTypeSymbol type, HashSet<INamedTypeSymbol> coveredTypes)
    {
        // 直接匹配
        if (coveredTypes.Contains(type))
            return true;

        // 解包 Nullable<T>
        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type.TypeArguments.Length > 0 &&
            type.TypeArguments[0] is INamedTypeSymbol inner)
        {
            return coveredTypes.Contains(inner);
        }

        // 解包集合类型：List<T>, IEnumerable<T>, etc.
        if (type.IsGenericType && type.TypeArguments.Length == 1 &&
            type.TypeArguments[0] is INamedTypeSymbol elementType)
        {
            if (coveredTypes.Contains(elementType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 解包 Task&lt;T&gt; / ValueTask&lt;T&gt; 的内部类型。
    /// </summary>
    /// <remarks>
    /// 注意：BCL 中泛型参数名为 <c>TResult</c>，因此 <c>ToDisplayString()</c> 是
    /// <c>System.Threading.Tasks.Task&lt;TResult&gt;</c>。早期实现按 <c>Task&lt;T&gt;</c> 做字符串比较，
    /// 永远不匹配 → 响应 DTO 的 AOT004 检查实际从未生效（已修复为按命名空间 + 名称 + 元数判定）。
    /// </remarks>
    private static INamedTypeSymbol? ExtractTaskInnerType(INamedTypeSymbol returnType)
    {
        if (!returnType.IsGenericType || returnType.TypeArguments.Length != 1)
            return null;

        var def = returnType.OriginalDefinition;
        var ns = def.ContainingNamespace?.ToDisplayString();
        if (ns != "System.Threading.Tasks")
            return null;

        if (def.Name is not ("Task" or "ValueTask"))
            return null;

        return returnType.TypeArguments[0] as INamedTypeSymbol;
    }
}
