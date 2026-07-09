// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generators.Context;

/// <summary>
/// 生成上下文
/// </summary>
internal class GeneratorContext
{
    public Compilation Compilation { get; }

    public INamedTypeSymbol InterfaceSymbol { get; }

    public InterfaceDeclarationSyntax InterfaceDeclaration { get; }

    public SemanticModel SemanticModel { get; }

    public SourceProductionContext ProductionContext { get; }

    public GenerationConfiguration Configuration { get; }

    public string ClassName { get; }

    public string NamespaceName { get; }

    public bool HasTokenManager => !string.IsNullOrEmpty(Configuration.TokenManager);

    public bool HasHttpClient => !string.IsNullOrEmpty(Configuration.HttpClient);

    public bool HasInheritedFrom => !string.IsNullOrEmpty(Configuration.InheritedFrom);

    public bool HasTokenType => !string.IsNullOrEmpty(Configuration.TokenType);

    public bool HasCache { get; set; }

    public bool HasCacheVaryByUser { get; set; }

    public bool HasResilience { get; set; }

    public bool HasQueryMap { get; set; }

    /// <summary>
    /// 接口中是否有方法使用了 XML 响应类型，需要生成 XmlSerializer 静态缓存字段
    /// </summary>
    public bool HasXmlResponse { get; set; }

    /// <summary>
    /// 需要生成 XmlSerializer 静态缓存字段的类型名称集合（去重）
    /// </summary>
    public HashSet<string> XmlResponseTypes { get; set; } = [];

    /// <summary>
    /// 接口中是否有方法使用了 ApiKey 注入模式
    /// </summary>
    public bool HasApiKeyInjection { get; set; }

    /// <summary>
    /// 接口中是否有方法使用了 HmacSignature 注入模式
    /// </summary>
    public bool HasHmacSignatureInjection { get; set; }

    /// <summary>
    /// 接口是否继承了 ICurrentUserId 接口
    /// </summary>
    public bool ImplementsICurrentUserId { get; set; }

    public IReadOnlyList<InterfacePropertyInfo> InterfaceProperties { get; set; } = [];

    /// <summary>
    /// 方法分析结果缓存，避免同一方法被 AnalyzeMethod 重复分析。
    /// 使用 SymbolEqualityComparer.Default 确保符号比较的正确性。
    /// 注意：此 Dictionary 仅在单线程生成上下文中使用（每个 GeneratorContext 实例对应一个接口的生成），
    /// 不可跨实例共享或在多线程环境下并发读写。
    /// </summary>
    public Dictionary<IMethodSymbol, MethodAnalysisResult> MethodAnalysisCache { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// 当前接口（含父接口）的所有方法列表，在构造函数中一次性计算并缓存。
    /// 避免 MethodGenerator、InterfaceImplementationGenerator 等多处重复调用 TypeSymbolHelper.GetAllMethods。
    /// </summary>
    public IReadOnlyList<IMethodSymbol> AllMethods { get; private set; } = [];

    /// <summary>
    /// 获取或缓存方法分析结果。若缓存命中则复用，否则调用 AnalyzeMethod 并缓存结果。
    /// 传入预计算的 <see cref="InterfaceProperties"/> 以避免 AnalyzeMethod 内部重复扫描基接口属性。
    /// </summary>
    public MethodAnalysisResult GetOrAnalyzeMethod(
        Compilation compilation,
        IMethodSymbol methodSymbol,
        InterfaceDeclarationSyntax interfaceDeclaration,
        SemanticModel semanticModel)
    {
        if (MethodAnalysisCache.TryGetValue(methodSymbol, out var cached))
            return cached;

        var result = MethodAnalyzer.AnalyzeMethod(
            compilation, methodSymbol, interfaceDeclaration, semanticModel,
            cachedInterfaceProperties: InterfaceProperties);
        MethodAnalysisCache[methodSymbol] = result;
        return result;
    }

    /// <summary>
    /// 接口是否有继承其他接口
    /// </summary>
    public bool HasBaseInterfaces => InterfaceSymbol.Interfaces.Length > 0;

    /// <summary>
    /// 根据 InheritedFrom 和 BaseHasTokenManager 属性值获取 GetTokenAsync 方法的访问修饰符
    /// - 继承自指定类且基类有 TokenManager：public override
    /// - 其他情况：public virtual
    /// </summary>
    public string GetTokenAsyncAccessibility => (HasInheritedFrom && Configuration.BaseHasTokenManager)
        ? "public override"
        : "public virtual";

    public string FieldAccessibility { get; }

    public GeneratorContext(
        Compilation compilation,
        INamedTypeSymbol interfaceSymbol,
        InterfaceDeclarationSyntax interfaceDeclaration,
        SemanticModel semanticModel,
        SourceProductionContext productionContext,
        GenerationConfiguration configuration)
    {
        Compilation = compilation;
        InterfaceSymbol = interfaceSymbol;
        InterfaceDeclaration = interfaceDeclaration;
        SemanticModel = semanticModel;
        ProductionContext = productionContext;
        Configuration = configuration;

        ClassName = TypeSymbolHelper.GetImplementationClassName(interfaceSymbol.Name);
        NamespaceName = SyntaxHelper.GetNamespaceName(interfaceDeclaration, HttpClientGeneratorConstants.ImplementationNamespaceSuffix);
        FieldAccessibility = configuration.IsAbstract ? "protected " : "private ";

        // 一次性获取所有方法，避免 5 个 Detect 方法各自独立遍历接口方法树
        List<IMethodSymbol> allMethods;
        try
        {
            // 当继承自基类时，只检测当前接口自身定义的方法以及非基接口的方法（父接口方法由基类负责）
            if (!string.IsNullOrEmpty(configuration.InheritedFrom))
            {
                if (!string.IsNullOrEmpty(configuration.InheritedFromInterfaceName))
                    allMethods = TypeSymbolHelper.GetAllMethods(interfaceSymbol, true, [configuration.InheritedFromInterfaceName]).ToList();
                else
                    allMethods = interfaceSymbol.GetMembers().OfType<IMethodSymbol>().ToList();
            }
            else
                allMethods = TypeSymbolHelper.GetAllMethods(interfaceSymbol, true).ToList();
        }
        catch (Exception ex)
        {
            GeneratorDebugLogger.LogError("GeneratorContext.GetAllMethods", ex);
            // 向用户报告诊断，确保 IDE 错误列表中可见
            productionContext.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientApiGenerationError,
                interfaceDeclaration.GetLocation(),
                interfaceSymbol.Name,
                $"解析接口方法时发生异常，已回退为仅生成当前接口方法: {GeneratorDebugLogger.FormatExceptionMessage(ex)}"));
            // 回退为仅当前接口自身定义的方法，与原 MethodGenerator.GetMethodsToGenerate 的容错行为一致
            allMethods = interfaceSymbol.GetMembers().OfType<IMethodSymbol>().ToList();
        }

        AllMethods = allMethods;

        // 预计算接口属性列表（含基接口的 [Query]/[Path] 属性），后续 GetOrAnalyzeMethod 会将其传入
        // AnalyzeMethod 作为 cachedInterfaceProperties，避免对每个方法重复扫描基接口属性树（O(N×M) -> O(M)）
        try
        {
            InterfaceProperties = MethodAnalyzer.AnalyzeInterfaceProperties(
                interfaceDeclaration, compilation, semanticModel);
        }
        catch (Exception ex)
        {
            GeneratorDebugLogger.LogError("GeneratorContext.AnalyzeInterfaceProperties", ex);
            // 回退为空列表，AnalyzeMethod 内部会在 cachedInterfaceProperties 为 null 时重新计算
            // 但此处传入空列表表示已知无接口属性，避免重复计算
            InterfaceProperties = [];
        }

        // 单次遍历检测全部 5 个特性标志，避免 5 个独立 Detect 方法各自遍历 allMethods
        // 并各自调用 method.GetAttributes() 产生重复分配
        DetectFeatures(interfaceSymbol, allMethods);
    }

    /// <summary>
    /// 单次遍历所有方法，一次性检测 Cache、CacheVaryByUser、Resilience、ApiKeyInjection、HmacSignatureInjection。
    /// 接口级 Token 特性在循环前检查；方法级特性在单次循环中合并检测，尽早短路退出。
    /// </summary>
    private void DetectFeatures(INamedTypeSymbol interfaceSymbol, IReadOnlyList<IMethodSymbol> allMethods)
    {
        try
        {
            // 接口级 Token 特性检查（单次调用，不在方法循环内重复）
            var interfaceTokenAttr = interfaceSymbol.GetAttributes()
                .FirstOrDefault(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.AttributeClass?.Name));
            if (interfaceTokenAttr != null)
            {
                HasApiKeyInjection = IsInjectionMode(interfaceTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeApiKey);
                HasHmacSignatureInjection = IsInjectionMode(interfaceTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeHmacSignature);
            }

            // 全部已检出时可短路退出
            if (HasCacheVaryByUser && HasResilience && HasApiKeyInjection && HasHmacSignatureInjection)
                return;

            foreach (var method in allMethods)
            {
                var methodAttrs = method.GetAttributes();

                // Cache 检测
                if (!HasCacheVaryByUser)
                {
                    var cacheAttr = methodAttrs.FirstOrDefault(attr =>
                        HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name));
                    if (cacheAttr != null)
                    {
                        HasCache = true;
                        if (AttributeDataHelper.GetBoolValueFromAttribute(cacheAttr, HttpClientGeneratorConstants.CacheVaryByUserProperty, false))
                            HasCacheVaryByUser = true;
                    }
                }
                else if (!HasCache)
                {
                    // VaryByUser 已检出但 HasCache 尚未标记（理论上 VaryByUser 蕴含 HasCache）
                    HasCache = methodAttrs.Any(attr =>
                        HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name));
                }

                // Resilience 检测
                if (!HasResilience)
                {
                    HasResilience = methodAttrs.Any(attr =>
                        HttpClientGeneratorConstants.RetryAttributeNames.Contains(attr.AttributeClass?.Name) ||
                        HttpClientGeneratorConstants.CircuitBreakerAttributeNames.Contains(attr.AttributeClass?.Name) ||
                        HttpClientGeneratorConstants.TimeoutAttributeNames.Contains(attr.AttributeClass?.Name));
                }

                // Token 注入模式检测
                if (!HasApiKeyInjection || !HasHmacSignatureInjection)
                {
                    var methodTokenAttr = methodAttrs.FirstOrDefault(attr =>
                        HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.AttributeClass?.Name));
                    if (methodTokenAttr != null)
                    {
                        if (!HasApiKeyInjection && IsInjectionMode(methodTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeApiKey))
                            HasApiKeyInjection = true;
                        if (!HasHmacSignatureInjection && IsInjectionMode(methodTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeHmacSignature))
                            HasHmacSignatureInjection = true;
                    }
                }

                // 全部检出后短路退出
                if (HasCacheVaryByUser && HasResilience && HasApiKeyInjection && HasHmacSignatureInjection)
                    return;
            }
        }
        catch (Exception ex)
        {
            GeneratorDebugLogger.LogError("DetectFeatures", ex);
        }
    }

    /// <summary>
    /// 检查 Token 特性的 InjectionMode 是否匹配指定模式
    /// </summary>
    private static bool IsInjectionMode(AttributeData tokenAttr, string targetMode)
    {
        foreach (var namedArg in tokenAttr.NamedArguments)
        {
            if (namedArg.Key == HttpClientGeneratorConstants.TokenInjectionModeProperty)
            {
                var modeName = GetTokenInjectionModeName(namedArg.Value.Value);
                return modeName == targetMode;
            }
        }
        // 未指定 InjectionMode 时默认为 Header，不匹配 ApiKey/HmacSignature
        return false;
    }

    /// <summary>
    /// 从 TypedConstant 获取 TokenInjectionMode 枚举名称。
    /// 委托至 TokenHelper.GetTokenInjectionModeName 统一实现，覆盖全部 7 种注入模式。
    /// </summary>
    private static string GetTokenInjectionModeName(object? value)
    {
        return TokenHelper.GetTokenInjectionModeName(value);
    }
}
