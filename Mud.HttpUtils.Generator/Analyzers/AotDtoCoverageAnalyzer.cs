// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
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
    // [Phase4 修复 5.1] 已删除未使用常量 SerializationMethodAttributeFullName（CA1823）：
    // 序列化方式改为按 AttributeClass.Name 匹配（见 GetMethodSerializationMethod），不再需要完全限定名。

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
    private static ImmutableDictionary<string, string?> TypeProps(INamedTypeSymbol type)
    {
        // [Phase4 修复 5.1] 值类型改为 string?（CS8620）：Diagnostic.Create 的 properties 形参为
        // ImmutableDictionary<string, string?>，用 <string, string> 传入会被判定为可空性不匹配。
        return ImmutableDictionary<string, string?>.Empty
            .Add("TypeFullName", type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    /// <summary>
    /// 分析编译单元中所有 [HttpClientApi] 接口方法的 DTO 覆盖情况，返回 AOT004 / AOT005 诊断。
    /// </summary>
    /// <param name="compilation">编译单元。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="forceRun">[T6] 强制运行覆盖分析（opt-in）：当 DTO + Context 均在引用程序集（共享包）
    /// 而本编译单元不声明 Context 时，默认不检查；设为 true 时跳过 hasLocalContext 门控。</param>
    /// <returns>诊断集合（无问题或未配置 Context 时为空）。</returns>
    public static ImmutableArray<Diagnostic> Analyze(Compilation compilation, CancellationToken cancellationToken, bool forceRun = false)
    {
        // [Phase2 修复 2.2] 异常护栏：分析器宁少报不可抛，避免 AD0001 整轮禁用。
        try
        {
            return AnalyzeCore(compilation, cancellationToken, forceRun);
        }
        catch (Exception ex)
        {
            GeneratorDebugLogger.LogError(nameof(Analyze), ex);
            return ImmutableArray<Diagnostic>.Empty;
        }
    }

    private static ImmutableArray<Diagnostic> AnalyzeCore(Compilation compilation, CancellationToken cancellationToken, bool forceRun = false)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // [T7 修复] 门控前移：先做轻量探测（仅遍历本编译语法树，不触引用程序集），
        // 避免无本地 Context 的 JIT 消费方每次编译都付出全量引用程序集扫描成本。
        // 探测结果按 Compilation 缓存，后续覆盖集合计算直接复用（本编译语法树只遍历一次）。
        // forceRun（T6）为 true 时跳过 hasLocalContext 门控，但仍需覆盖集合。
        if (!forceRun && !HasLocalJsonSerializerContext(compilation, out _))
            return diagnostics.ToImmutable();

        // 实际需要覆盖集合时才执行全量扫描（含引用程序集）。
        var coveredTypes = CollectCoveredTypes(compilation);

        // 触发门控：仅当"本编译单元自身声明了 JsonSerializerContext"时才运行 AOT004/AOT005。
        // 原因：覆盖集合自 P1-4（ADR-03）起会同时扫描引用程序集，而 Mud.HttpUtils 各库内部
        // 都自带 internal Context（MudHttpJsonContext / OAuth2JsonContext / ProblemDetailsJsonContext …），
        // 因此"coveredTypes.Count == 0"不再是有效的门控——任何引用本库的工程都会命中。
        // 若不以"本地声明 Context"作为接入信号，所有未选择 AOT 源生成工作流的消费方
        // （本仓库的 HttpClientApiDemo / ResilienceDemo / HttpClientDemo 等）都会被大量噪音诊断淹没。
        // 注意：仍使用引用程序集解析出的覆盖集合做判定（跨程序集 DTO+Context 场景不误报，见 ADR-03）。
        // [T6 修复] forceRun=true 时跳过 hasLocalContext 门控，用于 DTO+Context 均在引用程序集的共享包场景。
        if (!forceRun && coveredTypes.Count == 0)
            return diagnostics.ToImmutable();

        // 2. 查找 HttpClientApiAttribute 符号
        var httpClientApiAttr = compilation.GetTypeByMetadataName(HttpClientApiAttributeFullName);
        if (httpClientApiAttr == null)
            return diagnostics.ToImmutable();

        // [T8 修复] 方法级特性符号一次性解析，避免在 CheckMethodDtoCoverage 中逐方法重复 GetTypeByMetadataName
        var bodyAttr = compilation.GetTypeByMetadataName(BodyAttributeFullName);
        var queryAttr = compilation.GetTypeByMetadataName(QueryAttributeFullName);
        var queryMapAttr = compilation.GetTypeByMetadataName(QueryMapAttributeFullName);
        var analysisContext = new DtoCoverageAnalysisContext(bodyAttr, queryAttr, queryMapAttr);

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

                // [E-5] 接口级 [IgnoreGenerator]：AOT004/AOT005 对"用户明确不管"的接口无意义，跳过。
                if (GeneratorAttributeFilters.HasIgnoreGenerator(interfaceSymbol))
                    continue;

                // 4. 检查每个方法的 DTO 覆盖情况
                foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    if (cancellationToken.IsCancellationRequested)
                        return diagnostics.ToImmutable();

                    CheckMethodDtoCoverage(compilation, diagnostics, interfaceSymbol, method, coveredTypes, analysisContext);
                }
            }
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// 方法级特性符号的预解析上下文，避免逐方法重复 GetTypeByMetadataName。
    /// </summary>
    /// <remarks>
    /// 刻意不用 <c>record</c>/<c>init</c>：本工程目标框架为 <c>netstandard2.0</c>，
    /// 缺少 <c>System.Runtime.CompilerServices.IsExternalInit</c>（<c>InjectIsExternalInitOnLegacy</c>
    /// 在本仓库未生效），使用 record/init 会直接 CS0518 编译失败。
    /// </remarks>
    private sealed class DtoCoverageAnalysisContext
    {
        public DtoCoverageAnalysisContext(
            INamedTypeSymbol? bodyAttribute,
            INamedTypeSymbol? queryAttribute,
            INamedTypeSymbol? queryMapAttribute)
        {
            BodyAttribute = bodyAttribute;
            QueryAttribute = queryAttribute;
            QueryMapAttribute = queryMapAttribute;
        }

        public INamedTypeSymbol? BodyAttribute { get; }
        public INamedTypeSymbol? QueryAttribute { get; }
        public INamedTypeSymbol? QueryMapAttribute { get; }
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
        // [Phase2 修复 2.2] 异常护栏：分析器宁少报不可抛，避免 AD0001 整轮禁用。
        try
        {
            return AnalyzeHttpJsonSerializableCoverageCore(compilation, cancellationToken);
        }
        catch (Exception ex)
        {
            GeneratorDebugLogger.LogError(nameof(AnalyzeHttpJsonSerializableCoverage), ex);
            return ImmutableArray<Diagnostic>.Empty;
        }
    }

    private static ImmutableArray<Diagnostic> AnalyzeHttpJsonSerializableCoverageCore(Compilation compilation, CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var httpJsonSerializableAttr = compilation.GetTypeByMetadataName(HttpJsonSerializableAttributeFullName);
        if (httpJsonSerializableAttr == null)
            return diagnostics.ToImmutable();

        // [T7 修复] 覆盖集合内部会复用按 Compilation 缓存的本地 Context 探测结果，
        // 本编译语法树不会因 AOT006 路径被二次遍历。
        var coveredTypes = CollectCoveredTypes(compilation);

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
    /// [T7 修复] 轻量探测门控：仅遍历本编译语法树，检查是否声明了 JsonSerializerContext 子类。
    /// 不触引用程序集，O(语法树) 而非 O(语法树 + 引用程序集)。
    /// </summary>
    /// <param name="compilation">编译单元。</param>
    /// <param name="localContexts">输出：本编译单元中所有 JsonSerializerContext 子类符号。</param>
    /// <returns>是否存在至少一个本地 Context。</returns>
    private static bool HasLocalJsonSerializerContext(Compilation compilation, out List<INamedTypeSymbol> localContexts)
    {
        localContexts = GetLocalContexts(compilation);
        return localContexts.Count > 0;
    }

    /// <summary>
    /// 本编译单元 Context 探测结果的编译级缓存。
    /// </summary>
    /// <remarks>
    /// [T7 修复] 探测（遍历本编译语法树）与覆盖集合计算（本编译 + 全部引用程序集）都会用到
    /// "本编译声明的 Context 列表"。若各自扫描，本编译语法树会被遍历两次——而 T7 的目标正是
    /// "复用探测结果，避免二次遍历"。故按 <see cref="Compilation"/> 缓存探测结果：
    /// AOT004/005（<c>AotDtoCoverageDiagnosticAnalyzer</c>）、AOT006（<c>HttpJsonSerializableCoverageAnalyzer</c>）
    /// 与覆盖集合计算共享同一份列表。
    /// </remarks>
    private static readonly ConditionalWeakTable<Compilation, List<INamedTypeSymbol>> _localContextsCache = new();

    /// <summary>
    /// 获取本编译单元声明的所有 JsonSerializerContext 子类（按 <see cref="Compilation"/> 缓存）。
    /// </summary>
    private static List<INamedTypeSymbol> GetLocalContexts(Compilation compilation)
        => _localContextsCache.GetValue(compilation, static c => ComputeLocalContexts(c));

    /// <summary>
    /// 遍历本编译语法树，收集其中声明的 JsonSerializerContext 子类。
    /// </summary>
    private static List<INamedTypeSymbol> ComputeLocalContexts(Compilation compilation)
    {
        var localContexts = new List<INamedTypeSymbol>();
        var jsonSerializerContext = compilation.GetTypeByMetadataName(JsonSerializerContextFullName);
        if (jsonSerializerContext == null)
            return localContexts;

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol typeSymbol &&
                    InheritsFromJsonSerializerContext(typeSymbol, jsonSerializerContext))
                {
                    localContexts.Add(typeSymbol);
                }
            }
        }

        return localContexts;
    }

    /// <summary>
    /// 收集编译单元引用的所有 JsonSerializerContext 子类上的 [JsonSerializable] 类型（按 <see cref="Compilation"/> 缓存）。
    /// </summary>
    /// <param name="compilation">编译单元。</param>
    /// <remarks>
    /// <para>
    /// 同时支持 IDE（<see cref="CompilationReference"/>）与 CLI（<c>PortableExecutableReference</c>）
    /// 两种引用形态，并递归命名空间与嵌套类型。
    /// </para>
    /// <para>
    /// [Phase5 修复 3.1] 结果按 <see cref="Compilation"/> 缓存（ConditionalWeakTable）：同一次编译内
    /// AOT004/005 与 AOT006 两个分析器共享同一份覆盖集合，省掉第二遍全引用程序集扫描。
    /// 缓存值只读（调用方不得修改返回的集合），故可安全共享。
    /// </para>
    /// <para>
    /// [T7 修复] 本编译单元部分的扫描复用 <see cref="_localContextsCache"/> 的探测结果，
    /// 不再重复遍历本编译语法树；只有"引用程序集扫描"会在缓存未命中时执行。
    /// 注意：引用程序集中的 Context 只用于覆盖判定，不作为 AOT004/005 的触发信号
    /// （触发门控见 <see cref="HasLocalJsonSerializerContext"/>）。
    /// </para>
    /// </remarks>
    private static HashSet<INamedTypeSymbol> CollectCoveredTypes(Compilation compilation)
        => _coveredTypesCache.GetValue(compilation, static c => ComputeCoveredTypes(c));

    /// <summary>
    /// 编译级覆盖集合缓存：<see cref="Compilation"/> 生命周期结束即自动失效。
    /// </summary>
    private static readonly ConditionalWeakTable<Compilation, HashSet<INamedTypeSymbol>> _coveredTypesCache = new();

    /// <summary>
    /// 计算覆盖集合（缓存未命中时的实际扫描，O(本编译 Context 数 + 引用程序集)）。
    /// </summary>
    private static HashSet<INamedTypeSymbol> ComputeCoveredTypes(Compilation compilation)
    {
        var result = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        var jsonSerializableAttr = compilation.GetTypeByMetadataName(JsonSerializableAttributeFullName);
        var jsonSerializerContext = compilation.GetTypeByMetadataName(JsonSerializerContextFullName);

        if (jsonSerializableAttr == null || jsonSerializerContext == null)
            return result;

        // 本编译单元：直接复用探测缓存（避免二次遍历本编译语法树）
        foreach (var localContext in GetLocalContexts(compilation))
            CollectFromContextType(localContext, jsonSerializableAttr, result);

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
        HashSet<INamedTypeSymbol> coveredTypes,
        DtoCoverageAnalysisContext analysisContext)
    {
        var bodyAttr = analysisContext.BodyAttribute;
        var queryAttr = analysisContext.QueryAttribute;
        var queryMapAttr = analysisContext.QueryMapAttribute;

        foreach (var param in method.Parameters)
        {
            // [T1 修复] 数组参数解包——[Body] 数组的元素类型需要 Context 覆盖。
            // 裸数组（如 UserDto[]）是 IArrayTypeSymbol 而非 INamedTypeSymbol，
            // 原 `is not INamedTypeSymbol` 过滤使其被整体跳过，导致漏报。
            if (param.Type is IArrayTypeSymbol arrayType)
            {
                var paramLocation = param.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                    ?? method.Locations.FirstOrDefault();

                // 仅对 [Body] 数组做 AOT004 检查（[Query] 数组走逐元素 ToString，不走 JSON 序列化）
                if (bodyAttr != null && param.GetAttributes()
                    .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, bodyAttr)))
                {
                    var ser = GetMethodSerializationMethod(method);
                    if (ser is "FormUrlEncoded" or "Xml")
                        continue;

                    if (arrayType.ElementType is INamedTypeSymbol arrayElem &&
                        !IsCovered(arrayElem, coveredTypes) &&
                        !QuerySerializationClassifier.IsSimple(arrayElem))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            Diagnostics.AotDtoNotCoveredByContext,
                            paramLocation,
                            TypeProps(arrayElem),
                            interfaceSymbol.Name,
                            method.Name,
                            arrayElem.ToDisplayString()));
                    }
                }
                continue;
            }

            if (param.Type is not INamedTypeSymbol paramType)
                continue;

            var paramLocation2 = param.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                ?? method.Locations.FirstOrDefault();

            // 检查 [Body] 请求体 DTO — AOT004
            if (bodyAttr != null && param.GetAttributes()
                .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, bodyAttr)))
            {
                // FormUrlEncoded / Xml Body 不走 JSON 序列化，无需 JsonSerializerContext 覆盖，跳过 AOT004 检查。
                // [Phase2 修复 2.5] 增加 Xml 豁免，防止 XML 方法被 AOT004 误报。
                var ser = GetMethodSerializationMethod(method);
                if (ser is "FormUrlEncoded" or "Xml")
                    continue;

                if (!IsCovered(paramType, coveredTypes) && !QuerySerializationClassifier.IsSimple(paramType))
                {
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.AotDtoNotCoveredByContext,
                        paramLocation2,
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
                        paramLocation2,
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
                            paramLocation2,
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
            // [F12] 响应端判定：与 MethodGenerator 的分支保持同源。
            // 先解包 Task<T>/ValueTask<T>（非泛型 Task 无响应 DTO，保持原行为跳过）。
            var innerType = ExtractTaskInnerType(returnType);
            if (innerType == null)
                return;

            // [T1 修复] 数组响应类型（如 Task<UserDto[]>）：解包元素类型做覆盖判定。
            if (innerType is IArrayTypeSymbol responseArrayType)
            {
                var responseArrayElem = responseArrayType.ElementType;
                if (responseArrayElem is INamedTypeSymbol responseElemNamed)
                {
                    // 非 JSON 契约跳过
                    if (IsHttpResponseMessage(responseElemNamed) || IsStream(responseElemNamed) ||
                        QuerySerializationClassifier.IsSimple(responseElemNamed))
                        return;

                    if (GetMethodSerializationMethod(method) is "Xml")
                        return;

                    if (!IsCovered(responseElemNamed, coveredTypes))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            Diagnostics.AotDtoNotCoveredByContext,
                            method.Locations.FirstOrDefault(),
                            TypeProps(responseElemNamed),
                            interfaceSymbol.Name,
                            method.Name,
                            responseElemNamed.ToDisplayString()));
                    }
                }
                return;
            }

            if (innerType is not INamedTypeSymbol responseType)
                return;

            // 1) Response<T> → 取内部 T（生成器端 MethodGenerator.IsResponseType 分支）。
            if (IsResponseWrapper(responseType))
            {
                responseType = GetResponseInnerType(responseType);
                if (responseType == null)
                    return;
            }

            // 2) 非 JSON 契约的 Task<T> 返回（生成器不走反序列化）→ 跳过：
            //    HttpResponseMessage（SendRawAsync 直达）、Stream/byte[]（下载分支）、简单类型。
            //    [Phase2 修复 2.5] XML 序列化的响应不走 JSON 反序列化，也跳过，防止 AOT004 误报。
            if (IsHttpResponseMessage(responseType) || IsStream(responseType) ||
                IsByteArray(responseType) || QuerySerializationClassifier.IsSimple(responseType))
                return;

            // XML 序列化方法豁免：响应端用 XML 反序列化，不需要 JsonSerializerContext 覆盖。
            if (GetMethodSerializationMethod(method) is "Xml")
                return;

            // 3) 其余才做覆盖判定
            if (!IsCovered(responseType, coveredTypes) && !QuerySerializationClassifier.IsSimple(responseType))
            {
                diagnostics.Add(Diagnostic.Create(
                    Diagnostics.AotDtoNotCoveredByContext,
                    method.Locations.FirstOrDefault(),
                    TypeProps(responseType),
                    interfaceSymbol.Name,
                    method.Name,
                    responseType.ToDisplayString()));
            }
        }
    }

    /// <summary>是否 Response&lt;T&gt; 包装类型（Mud.HttpUtils.Response&lt;T&gt;）。</summary>
    private static bool IsResponseWrapper(ITypeSymbol type)
    {
        var def = (type as INamedTypeSymbol)?.OriginalDefinition;
        if (def is null || !def.IsGenericType || def.TypeParameters.Length != 1)
            return false;
        return def.ContainingNamespace?.ToDisplayString() == "Mud.HttpUtils" && def.Name == "Response";
    }

    /// <summary>解包 Response&lt;T&gt; 的内部 T。</summary>
    private static INamedTypeSymbol? GetResponseInnerType(ITypeSymbol type)
        => (type as INamedTypeSymbol)?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;

    private static bool IsHttpResponseMessage(ITypeSymbol type)
        => type is { } t
           && t.ContainingNamespace?.ToDisplayString() == "System.Net.Http"
           && t.Name == "HttpResponseMessage";

    private static bool IsStream(ITypeSymbol type)
        => type is { } t
           && t.ContainingNamespace?.ToDisplayString() == "System.IO"
           && t.Name == "Stream";

    private static bool IsByteArray(ITypeSymbol type)
        => type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte };

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
    /// 检查 ITypeSymbol（含数组）是否被 Context 覆盖。
    /// 数组类型的元素类型已被覆盖时视为覆盖。
    /// </summary>
    private static bool IsCovered(ITypeSymbol type, HashSet<INamedTypeSymbol> coveredTypes)
    {
        if (type is INamedTypeSymbol named)
            return IsCovered(named, coveredTypes);

        // 数组：元素类型被覆盖即可
        if (type is IArrayTypeSymbol array &&
            array.ElementType is INamedTypeSymbol elem)
        {
            return coveredTypes.Contains(elem);
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
    /// [T1 修复] 返回类型从 INamedTypeSymbol? 放宽为 ITypeSymbol?，
    /// 使 Task&lt;UserDto[]&gt; 中的数组类型能原样上抛，由调用方统一处理。
    /// </remarks>
    private static ITypeSymbol? ExtractTaskInnerType(INamedTypeSymbol returnType)
    {
        if (!returnType.IsGenericType || returnType.TypeArguments.Length != 1)
            return null;

        var def = returnType.OriginalDefinition;
        var ns = def.ContainingNamespace?.ToDisplayString();
        if (ns != "System.Threading.Tasks")
            return null;

        if (def.Name is not ("Task" or "ValueTask"))
            return null;

        return returnType.TypeArguments[0];
    }
}
