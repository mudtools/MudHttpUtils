// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// 方法分析器，负责分析接口方法的特性和元数据
/// </summary>
internal static class MethodAnalyzer
{
    /// <summary>
    /// 分析函数符号，并返回 MethodAnalysisResult 分析结果
    /// </summary>
    /// <param name="cachedInterfaceProperties">
    /// 可选的预计算接口属性列表。当由 <see cref="GeneratorContext"/> 批量调用时传入已缓存的属性，
    /// 避免 <see cref="AnalyzeInterfaceProperties"/> 对同一接口被每个方法重复调用导致的 O(N×M) 性能退化。
    /// 传入 null 时将内部计算。
    /// </param>
    /// <param name="cachedInterfaceAttributes">
    /// 可选的预计算接口特性列表（<c>INamedTypeSymbol.GetAttributes()</c> 结果）。
    /// 当由 <see cref="GeneratorContext"/> 批量调用时传入已缓存的特性，避免对同一接口的每个方法
    /// 重复调用 <c>GetAttributes()</c> 产生多次分配。传入 <c>default</c> 时将内部计算。
    /// </param>
    public static MethodAnalysisResult AnalyzeMethod(
        Compilation compilation,
        IMethodSymbol methodSymbol,
        InterfaceDeclarationSyntax interfaceDecl,
        SemanticModel? semanticModel = null,
        IReadOnlyList<InterfacePropertyInfo>? cachedInterfaceProperties = null,
        ImmutableArray<AttributeData> cachedInterfaceAttributes = default)
    {
        ArgumentNullExceptionExtensions.ThrowIfNull(compilation);
        ArgumentNullExceptionExtensions.ThrowIfNull(methodSymbol);
        ArgumentNullExceptionExtensions.ThrowIfNull(interfaceDecl);

        var methodSyntax = FindMethodSyntax(compilation, methodSymbol, interfaceDecl, semanticModel);

        // 一次性获取方法特性列表，避免在后续分析中重复调用 GetAttributes() 产生多次分配
        var methodAttributes = methodSymbol.GetAttributes();

        var httpMethodAttributeData = FindHttpMethodAttributeFromAttributes(methodAttributes, compilation);
        if (httpMethodAttributeData == null)
            return MethodAnalysisResult.CreateInvalid();

        if (httpMethodAttributeData.AttributeClass == null)
            return MethodAnalysisResult.CreateInvalid();

        var httpMethodAttributeName = httpMethodAttributeData.AttributeClass.Name;
        var httpMethod = ExtractHttpMethodName(httpMethodAttributeName);
        var urlTemplate = GetAttributeArgumentValueFromAttributeData(httpMethodAttributeData, 0)?.ToString() ?? "";

        if (string.IsNullOrEmpty(httpMethod) || string.IsNullOrEmpty(urlTemplate))
            return MethodAnalysisResult.CreateInvalid();

        var methodContentType = GetMethodContentTypeFromHttpMethodAttr(httpMethodAttributeData);
        var responseContentType = GetResponseContentTypeFromHttpMethodAttr(httpMethodAttributeData);
        var responseEnableDecrypt = GetResponseEnableDecryptFromHttpMethodAttr(httpMethodAttributeData);
        var parameters = ParameterAnalyzer.AnalyzeParameters(methodSymbol);
        var (bodyContentType, bodyEnableEncrypt, bodyEncryptSerializeType, bodyEncryptPropertyName) = GetBodyInfoFromParameters(parameters);

        // 一次性获取接口特性列表，避免 5 个子方法各自独立调用 GetAttributes() 产生多次分配
        // 优先使用调用方预计算的接口特性缓存，避免对同一接口的每个方法重复调用 GetAttributes()
        ImmutableArray<AttributeData> interfaceAttrs;
        if (cachedInterfaceAttributes.IsDefault)
        {
            var interfaceModel = semanticModel ?? SemanticModelCache.GetOrCreate(compilation, interfaceDecl.SyntaxTree);
            var interfaceSymbol = interfaceModel.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;
            interfaceAttrs = interfaceSymbol?.GetAttributes() ?? ImmutableArray<AttributeData>.Empty;
        }
        else
        {
            interfaceAttrs = cachedInterfaceAttributes;
        }

        var (interfaceAttributes, interfaceHeaderAttributes, interfaceTokenInjectionMode, interfaceTokenName, interfaceTokenScopes) = AnalyzeInterfaceAttributes(interfaceAttrs);

        var (cacheEnabled, cacheDurationSeconds, cacheKeyTemplate, cacheVaryByUser) = AnalyzeCacheAttribute(methodAttributes);

        var (retryEnabled, retryMaxRetries, retryDelayMilliseconds, retryUseExponentialBackoff) = AnalyzeRetryAttribute(methodAttributes);
        var (circuitBreakerEnabled, circuitBreakerFailureThreshold, circuitBreakerBreakDurationSeconds, circuitBreakerSamplingDurationSeconds, circuitBreakerMinimumThroughput) = AnalyzeCircuitBreakerAttribute(methodAttributes);
        var (methodTimeoutEnabled, methodTimeoutMilliseconds) = AnalyzeTimeoutAttribute(methodAttributes);

        var methodTokenScopes = AnalyzeMethodTokenScopes(methodAttributes);

        var (methodTokenManagerKey, methodRequiresUserId, methodTokenInjectionMode) = AnalyzeMethodTokenExtended(methodAttributes);

        var tokenParameterName = parameters
            .FirstOrDefault(p => p.Attributes.Any(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.Name)))?
            .Name;

        var allowAnyStatusCode = AnalyzeAllowAnyStatusCode(methodSymbol, methodAttributes, interfaceAttrs);
        var (interfaceQueryParams, interfacePathParams) = AnalyzeInterfaceQueryPathAttributes(interfaceAttrs);
        // 优先使用调用方预计算的接口属性缓存，避免对同一接口被每个方法重复扫描基接口属性
        var interfaceProperties = cachedInterfaceProperties ?? AnalyzeInterfaceProperties(interfaceDecl, compilation, semanticModel);
        var headerMergeMode = AnalyzeHeaderMergeMode(methodSymbol, methodAttributes, interfaceAttrs);
        var serializationMethod = AnalyzeSerializationMethod(methodSymbol, methodAttributes, interfaceAttrs);

        var returnTypeFullName = TypeSymbolHelper.GetTypeFullName(methodSymbol.ReturnType);
        var isAsyncEnumerable = TypeDetectionHelper.IsAsyncEnumerableType(returnTypeFullName, out var asyncEnumerableElementType);

        return new MethodAnalysisResult
        {
            IsValid = true,
            MethodName = methodSymbol.Name,
            HttpMethod = httpMethod,
            UrlTemplate = urlTemplate,
            ReturnType = returnTypeFullName,
            AsyncInnerReturnType = TypeSymbolHelper.ExtractAsyncInnerType(methodSymbol.ReturnType),
            IsAsyncMethod = TypeSymbolHelper.IsAsyncType(methodSymbol.ReturnType),
            IsAsyncEnumerableReturn = isAsyncEnumerable,
            AsyncEnumerableElementType = asyncEnumerableElementType,
            Parameters = parameters,
            IgnoreGenerator = HasMethodAttribute(methodAttributes, HttpClientGeneratorConstants.IgnoreGeneratorAttributeNames),
            InterfaceAttributes = interfaceAttributes,
            InterfaceHeaderAttributes = interfaceHeaderAttributes,
            MethodContentType = methodContentType,
            BodyContentType = bodyContentType,
            ResponseContentType = responseContentType,
            ResponseEnableDecrypt = responseEnableDecrypt,
            BodyEnableEncrypt = bodyEnableEncrypt,
            BodyEncryptSerializeType = bodyEncryptSerializeType,
            BodyEncryptPropertyName = bodyEncryptPropertyName,
            InterfaceTokenInjectionMode = interfaceTokenInjectionMode,
            InterfaceTokenName = interfaceTokenName,
            InterfaceTokenScopes = interfaceTokenScopes,
            MethodTokenScopes = methodTokenScopes,
            MethodTokenInjectionMode = methodTokenInjectionMode,
            TokenParameterName = tokenParameterName,
            MethodTokenManagerKey = methodTokenManagerKey,
            MethodRequiresUserId = methodRequiresUserId,
            AllowAnyStatusCode = allowAnyStatusCode,
            InterfaceQueryParameters = interfaceQueryParams,
            InterfacePathParameters = interfacePathParams,
            InterfaceProperties = interfaceProperties,
            HeaderMergeMode = headerMergeMode,
            SerializationMethod = serializationMethod,
            CacheEnabled = cacheEnabled,
            CacheDurationSeconds = cacheDurationSeconds,
            CacheKeyTemplate = cacheKeyTemplate,
            CacheVaryByUser = cacheVaryByUser,
            RetryEnabled = retryEnabled,
            RetryMaxRetries = retryMaxRetries,
            RetryDelayMilliseconds = retryDelayMilliseconds,
            RetryUseExponentialBackoff = retryUseExponentialBackoff,
            CircuitBreakerEnabled = circuitBreakerEnabled,
            CircuitBreakerFailureThreshold = circuitBreakerFailureThreshold,
            CircuitBreakerBreakDurationSeconds = circuitBreakerBreakDurationSeconds,
            CircuitBreakerSamplingDurationSeconds = circuitBreakerSamplingDurationSeconds,
            CircuitBreakerMinimumThroughput = circuitBreakerMinimumThroughput,
            MethodTimeoutEnabled = methodTimeoutEnabled,
            MethodTimeoutMilliseconds = methodTimeoutMilliseconds
        };
    }

    /// <summary>
    /// 从方法语法节点查找HTTP方法特性
    /// </summary>
    public static AttributeSyntax? FindHttpMethodAttribute(MethodDeclarationSyntax methodSyntax)
    {
        if (methodSyntax == null)
            return null;

        foreach (var methodName in HttpClientGeneratorConstants.SupportedHttpMethods)
        {
            var attributes = AttributeSyntaxHelper.GetAttributeSyntaxes(methodSyntax, methodName);
            if (attributes.Any())
                return attributes[0];
        }

        return null;
    }

    /// <summary>
    /// 从方法符号查找HTTP方法特性
    /// </summary>
    public static AttributeData? FindHttpMethodAttributeFromSymbol(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null)
            return null;

        return FindHttpMethodAttributeFromAttributes(methodSymbol.GetAttributes());
    }

    /// <summary>
    /// 从已缓存的特性列表中查找HTTP方法特性
    /// </summary>
    internal static AttributeData? FindHttpMethodAttributeFromAttributes(ImmutableArray<AttributeData> attributes)
    {
        return attributes
            .FirstOrDefault(attr => HttpClientGeneratorConstants.SupportedHttpMethods.Contains(attr.AttributeClass?.Name));
    }

    /// <summary>
    /// 从已缓存的特性列表中查找HTTP方法特性（含自定义特性 fallback）。
    /// v3.3 Phase 5 T5.2：当已知特性名匹配失败时，检查是否有特性继承自 HttpMethodAttribute。
    /// </summary>
    /// <param name="attributes">方法特性列表。</param>
    /// <param name="compilation">编译上下文（用于解析 HttpMethodAttribute 类型）。</param>
    internal static AttributeData? FindHttpMethodAttributeFromAttributes(
        ImmutableArray<AttributeData> attributes,
        Compilation compilation)
    {
        // 1. 先尝试已知特性名匹配（快速路径）
        var known = FindHttpMethodAttributeFromAttributes(attributes);
        if (known != null)
            return known;

        // 2. Fallback：检查是否有特性继承自 HttpMethodAttribute
        var httpMethodAttrType = compilation.GetTypeByMetadataName("Mud.HttpUtils.Attributes.HttpMethodAttribute");
        if (httpMethodAttrType == null)
            return null;

        foreach (var attr in attributes)
        {
            if (attr.AttributeClass == null)
                continue;

            // 跳过已知特性（已在快速路径中排除）
            if (HttpClientGeneratorConstants.SupportedHttpMethods.Contains(attr.AttributeClass.Name))
                continue;

            // 检查继承链：attr.AttributeClass 是否继承自 HttpMethodAttribute
            if (InheritsFrom(attr.AttributeClass, httpMethodAttrType))
                return attr;
        }

        return null;
    }

    /// <summary>
    /// 检查类型是否继承自指定基类型（含多级继承）。
    /// </summary>
    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// 从特性名称中提取HTTP方法名称
    /// </summary>
    public static string ExtractHttpMethodName(string attributeName)
    {
        if (string.IsNullOrEmpty(attributeName))
            return "";

        if (attributeName.EndsWith("Attribute", StringComparison.Ordinal))
        {
            return attributeName.Substring(0, attributeName.Length - "Attribute".Length);
        }

        return attributeName;
    }

    /// <summary>
    /// 从AttributeData获取构造函数参数值
    /// </summary>
    public static object? GetAttributeArgumentValueFromAttributeData(AttributeData attribute, int index)
    {
        if (attribute == null || attribute.ConstructorArguments.Length <= index)
            return null;

        return attribute.ConstructorArguments[index].Value;
    }

    /// <summary>
    /// 获取特性构造函数参数值
    /// </summary>
    public static object? GetAttributeArgumentValue(AttributeSyntax attribute, int index)
    {
        return attribute.GetConstructorArgument(null, index);
    }

    /// <summary>
    /// 检查方法是否具有指定的特性
    /// </summary>
    public static bool HasMethodAttribute(IMethodSymbol methodSymbol, params string[] attributeNames)
    {
        if (methodSymbol == null)
            return false;

        return HasMethodAttribute(methodSymbol.GetAttributes(), attributeNames);
    }

    /// <summary>
    /// 从已缓存的特性列表中检查是否具有指定的特性
    /// </summary>
    private static bool HasMethodAttribute(ImmutableArray<AttributeData> attributes, string[] attributeNames)
    {
        return attributes.Any(attr => attributeNames.Contains(attr.AttributeClass?.Name));
    }

    /// <summary>
    /// 从HTTP方法特性获取ContentType值（请求内容类型）
    /// </summary>
    public static string? GetMethodContentTypeFromHttpMethodAttribute(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null)
            return null;

        var httpMethodAttr = FindHttpMethodAttributeFromAttributes(methodSymbol.GetAttributes());
        return GetMethodContentTypeFromHttpMethodAttr(httpMethodAttr);
    }

    /// <summary>
    /// 从已缓存的特性列表中获取ContentType值
    /// </summary>
    private static string? GetMethodContentTypeFromHttpMethodAttr(AttributeData? httpMethodAttr)
    {
        if (httpMethodAttr == null)
            return null;

        return AttributeDataHelper.GetStringValueFromAttribute(httpMethodAttr, [HttpClientGeneratorConstants.HttpMethodContentTypeProperty]);
    }

    /// <summary>
    /// 从参数列表中提取Body参数的ContentType、加密配置等信息
    /// </summary>
    private static (string? bodyContentType, bool bodyEnableEncrypt, string? bodyEncryptSerializeType, string? bodyEncryptPropertyName)
        GetBodyInfoFromParameters(IReadOnlyList<ParameterInfo> parameters)
    {
        var bodyParam = parameters.FirstOrDefault(p =>
            p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.BodyAttribute));

        if (bodyParam == null)
            return (null, false, null, null);

        var bodyAttr = bodyParam.Attributes.First(attr => attr.Name == HttpClientGeneratorConstants.BodyAttribute);

        string? contentType = null;
        bool enableEncrypt = false;
        string? encryptSerializeType = null;
        string? encryptPropertyName = null;

        // 先检查构造函数参数（如 [Body("application/xml")]）
        if (bodyAttr.Arguments.Length > 0)
            contentType = bodyAttr.Arguments[0]?.ToString();

        // 再检查命名参数（如 [Body(ContentType = "application/xml")]）
        if (bodyAttr.NamedArguments.TryGetValue("ContentType", out var ctValue))
            contentType = ctValue?.ToString();

        if (bodyAttr.NamedArguments.TryGetValue(HttpClientGeneratorConstants.BodyEnableEncryptProperty, out var encValue))
            bool.TryParse(encValue?.ToString(), out enableEncrypt);

        if (bodyAttr.NamedArguments.TryGetValue(HttpClientGeneratorConstants.BodyEncryptSerializeTypeProperty, out var estValue))
            encryptSerializeType = GetEnumNameFromTypedConstant(estValue, "Json");

        if (bodyAttr.NamedArguments.TryGetValue(HttpClientGeneratorConstants.BodyEncryptPropertyNameProperty, out var epnValue))
            encryptPropertyName = epnValue?.ToString();

        return (contentType, enableEncrypt, encryptSerializeType, encryptPropertyName);
    }

    /// <summary>
    /// 从TypedConstant获取枚举名称，如果获取失败则返回默认值
    /// </summary>
    private static string GetEnumNameFromTypedConstant(object? value, string defaultValue)
    {
        if (value == null)
            return defaultValue;

        var str = value.ToString();
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        // 如果是数字，转换为枚举名称
        if (int.TryParse(str, out var num))
        {
            return num switch
            {
                0 => "Json",
                1 => "Xml",
                _ => defaultValue
            };
        }

        // 已经是名称，移除命名空间前缀
        var lastDot = str.LastIndexOf('.');
        return lastDot >= 0 ? str.Substring(lastDot + 1) : str;
    }

    /// <summary>
    /// 从 TypedConstant 获取 TokenInjectionMode 枚举名称。
    /// 委托至 TokenHelper.GetTokenInjectionModeName 统一实现，避免重复代码。
    /// </summary>
    private static string GetTokenInjectionModeName(object? value)
    {
        return TokenHelper.GetTokenInjectionModeName(value);
    }

    /// <summary>
    /// 从已缓存的特性列表中获取ResponseContentType值
    /// </summary>
    private static string? GetResponseContentTypeFromHttpMethodAttr(AttributeData? httpMethodAttr)
    {
        if (httpMethodAttr == null)
            return null;

        return AttributeDataHelper.GetStringValueFromAttribute(httpMethodAttr, [HttpClientGeneratorConstants.HttpMethodResponseContentTypeProperty]);
    }

    /// <summary>
    /// 从已缓存的特性列表中获取ResponseEnableDecrypt值
    /// </summary>
    private static bool GetResponseEnableDecryptFromHttpMethodAttr(AttributeData? httpMethodAttr)
    {
        if (httpMethodAttr == null)
            return false;

        return AttributeDataHelper.GetBoolValueFromAttribute(
            httpMethodAttr,
            HttpClientGeneratorConstants.HttpMethodResponseEnableDecryptProperty);
    }

    /// <summary>
    /// 查询方法的语法对象
    /// </summary>
    public static MethodDeclarationSyntax? FindMethodSyntax(
        Compilation compilation,
        IMethodSymbol methodSymbol,
        InterfaceDeclarationSyntax interfaceDecl,
        SemanticModel? semanticModel = null)
    {
        if (interfaceDecl == null || methodSymbol == null || compilation == null)
            return null;

        var allInterfaces = GetAllBaseInterfaceSyntaxNodes(compilation, interfaceDecl, semanticModel);
        var targetName = methodSymbol.Name;
        var targetParamCount = methodSymbol.Parameters.Length;

        foreach (var interfaceSyntax in allInterfaces)
        {
            // 先用廉价的名称和参数数量过滤候选集，避免对每个方法都调用昂贵的 GetDeclaredSymbol
            var candidates = interfaceSyntax.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == targetName &&
                            m.ParameterList.Parameters.Count == targetParamCount)
                .ToList();

            if (candidates.Count == 0)
                continue;

            // 仅对候选方法进行语义分析确认
            foreach (var candidate in candidates)
            {
                try
                {
                    var model = SemanticModelCache.GetOrCreate(compilation, candidate.SyntaxTree);
                    var methodSymbolFromSyntax = model.GetDeclaredSymbol(candidate);
                    var targetSymbol = methodSymbolFromSyntax?.OriginalDefinition ?? methodSymbolFromSyntax;
                    var sourceSymbol = methodSymbol.OriginalDefinition ?? methodSymbol;
                    if (targetSymbol?.Equals(sourceSymbol, SymbolEqualityComparer.Default) == true)
                    {
                        return candidate;
                    }
                }
                catch (Exception ex)
                {
                    // 语义分析失败，继续尝试下一个候选
                    GeneratorDebugLogger.LogError("FindMethodSyntax 语义分析", ex);
                }
            }

            // 语义分析未精确匹配，尝试通过参数类型符号匹配（避免重载误判）
            // 候选方法位于同一接口声明，共享同一 SyntaxTree，获取一次 SemanticModel
            var fallbackModel = SemanticModelCache.GetOrCreate(compilation, interfaceSyntax.SyntaxTree);
            foreach (var candidate in candidates)
            {
                if (TryMatchByParameterTypes(candidate, methodSymbol, fallbackModel))
                    return candidate;
            }

            // 无法精确匹配时返回 null，由调用方降级处理（符号侧信息仍可用）
            return null;
        }

        return null;
    }

    /// <summary>
    /// 通过参数类型符号匹配方法声明与目标方法符号。
    /// 用于在语义分析失败时的回退匹配，避免重载方法误判。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="SemanticModel.GetTypeInfo(SyntaxNode)"/> 获取候选参数的类型符号，
    /// 通过 <see cref="SymbolEqualityComparer"/> 进行符号相等性比较，
    /// 而非源文本字符串比较（<see cref="TypeSyntax.ToString"/> 仅返回源代码写法，
    /// 与 <see cref="ISymbol.ToDisplayString(SymbolDisplayFormat)"/> 的全限定格式不可比，会导致匹配失败）。
    /// </remarks>
    private static bool TryMatchByParameterTypes(
        MethodDeclarationSyntax candidate,
        IMethodSymbol targetSymbol,
        SemanticModel semanticModel)
    {
        var candidateParams = candidate.ParameterList.Parameters;
        if (candidateParams.Count != targetSymbol.Parameters.Length)
            return false;

        for (var i = 0; i < candidateParams.Count; i++)
        {
            var candidateTypeSyntax = candidateParams[i].Type;
            if (candidateTypeSyntax == null)
                return false;

            // 使用语义模型获取候选参数的类型符号，与目标符号进行符号级比较
            var candidateTypeSymbol = semanticModel.GetTypeInfo(candidateTypeSyntax).Type;
            var targetTypeSymbol = targetSymbol.Parameters[i].Type;

            if (candidateTypeSymbol == null || targetTypeSymbol == null)
                return false;

            if (!candidateTypeSymbol.Equals(targetTypeSymbol, SymbolEqualityComparer.Default))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 获取接口及其所有基接口的语法节点
    /// </summary>
    public static IEnumerable<InterfaceDeclarationSyntax> GetAllBaseInterfaceSyntaxNodes(
        Compilation compilation,
        InterfaceDeclarationSyntax interfaceDecl,
        SemanticModel? semanticModel = null)
    {
        yield return interfaceDecl;

        var model = semanticModel ?? SemanticModelCache.GetOrCreate(compilation, interfaceDecl.SyntaxTree);
        var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl);

        if (interfaceSymbol == null)
            yield break;

        foreach (var baseInterface in interfaceSymbol.Interfaces)
        {
            var baseInterfaceSyntax = GetInterfaceDeclarationSyntax(compilation, baseInterface);
            if (baseInterfaceSyntax != null)
            {
                yield return baseInterfaceSyntax;

                var baseInterfaceModel = SemanticModelCache.GetOrCreate(compilation, baseInterfaceSyntax.SyntaxTree);
                foreach (var deeperBase in GetAllBaseInterfaceSyntaxNodes(compilation, baseInterfaceSyntax, baseInterfaceModel))
                {
                    yield return deeperBase;
                }
            }
        }
    }

    // NEW-GEN-02 说明：ConditionalWeakTable 依赖 Compilation 的 GC 回收自动清理。
    // 在 IDE 场景下 Compilation 可能保持较长时间引用，但这是 Roslyn 源生成器的标准缓存模式。
    // 如遇内存问题，可考虑改用 IncrementalValueProvider 提供的缓存机制。
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<INamedTypeSymbol, InterfaceDeclarationSyntax?>> _interfaceSyntaxCache = new();

    /// <summary>
    /// 获取接口声明语法节点
    /// </summary>
    private static InterfaceDeclarationSyntax? GetInterfaceDeclarationSyntax(
        Compilation compilation,
        INamedTypeSymbol interfaceSymbol)
    {
        var innerDict = _interfaceSyntaxCache.GetValue(
            compilation,
            _ => new ConcurrentDictionary<INamedTypeSymbol, InterfaceDeclarationSyntax?>(SymbolEqualityComparer.Default));
        return innerDict.GetOrAdd(interfaceSymbol, symbol => FindInterfaceDeclarationSyntax(compilation, symbol));
    }

    /// <summary>
    /// 查找接口声明语法节点
    /// </summary>
    private static InterfaceDeclarationSyntax? FindInterfaceDeclarationSyntax(
        Compilation compilation,
        INamedTypeSymbol interfaceSymbol)
    {
        foreach (var syntaxReference in interfaceSymbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();
            if (syntax is InterfaceDeclarationSyntax interfaceDecl)
            {
                return interfaceDecl;
            }
        }

        // 回退路径：DeclaringSyntaxReferences 未找到
        // 如果接口符号的所有位置都在元数据中（即来自引用的程序集），则无需遍历源代码语法树
        if (interfaceSymbol.Locations.Length > 0 && interfaceSymbol.Locations.All(loc => loc.IsInMetadata))
        {
            GeneratorDebugLogger.Log(
                $"FindInterfaceDeclarationSyntax 跳过语法树遍历: {interfaceSymbol.ToDisplayString()} (来自引用程序集)");
            return null;
        }

        // 优先从符号的 Locations 直接定位源代码位置，避免全量遍历所有语法树
        // Locations 包含符号的声明位置，O(Locations) 远小于 O(语法树数 × 节点数)
        foreach (var location in interfaceSymbol.Locations)
        {
            if (location.IsInMetadata || location.SourceTree == null)
                continue;

            var root = location.SourceTree.GetRoot();
            var node = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);
            if (node is InterfaceDeclarationSyntax directDecl)
                return directDecl;
            if (node.FirstAncestorOrSelf<InterfaceDeclarationSyntax>() is { } ancestorDecl)
                return ancestorDecl;
        }

        // DeclaringSyntaxReferences 和 Locations 均未命中源代码位置
        // 说明接口声明不在当前编译的源代码中（可能来自引用程序集或部分类型定义缺失）。
        // 不再遍历所有语法树（O(N) 风险），直接返回 null 由调用方降级处理。
        return null;
    }

    /// <summary>
    /// 分析接口的 [InterfaceQuery] 和 [InterfacePath] 特性
    /// </summary>
    private static (List<InterfaceQueryParameterInfo> queryParams, List<InterfacePathParameterInfo> pathParams)
        AnalyzeInterfaceQueryPathAttributes(ImmutableArray<AttributeData> interfaceAttrs)
    {
        var queryParams = new List<InterfaceQueryParameterInfo>();
        var pathParams = new List<InterfacePathParameterInfo>();

        foreach (var attr in interfaceAttrs)
        {
            if (HttpClientGeneratorConstants.InterfaceQueryAttributeNames.Contains(attr.AttributeClass?.Name))
            {
                var name = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value?.ToString() : null;
                var value = attr.ConstructorArguments.Length > 1 ? attr.ConstructorArguments[1].Value?.ToString() : null;
                if (!string.IsNullOrEmpty(name))
                {
                    queryParams.Add(new InterfaceQueryParameterInfo { Name = name, Value = value });
                }
            }
            else if (HttpClientGeneratorConstants.InterfacePathAttributeNames.Contains(attr.AttributeClass?.Name))
            {
                var name = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value?.ToString() : null;
                var value = attr.ConstructorArguments.Length > 1 ? attr.ConstructorArguments[1].Value?.ToString() : null;
                if (!string.IsNullOrEmpty(name))
                {
                    pathParams.Add(new InterfacePathParameterInfo { Name = name, Value = value });
                }
            }
        }

        return (queryParams, pathParams);
    }

    internal static List<InterfacePropertyInfo> AnalyzeInterfaceProperties(InterfaceDeclarationSyntax interfaceDecl, Compilation compilation, SemanticModel? semanticModel)
    {
        var properties = new List<InterfacePropertyInfo>();

        var model = semanticModel ?? SemanticModelCache.GetOrCreate(compilation, interfaceDecl.SyntaxTree);
        var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;

        if (interfaceSymbol == null)
            return properties;

        // 收集当前接口语法树中的属性声明（基接口的语法节点可能在不同语法树或跨程序集，符号侧统一处理）
        var propertyDecls = interfaceDecl.Members.OfType<PropertyDeclarationSyntax>()
            .ToDictionary(p => p.Identifier.Text, p => p);

        // 遍历当前接口及其所有基接口的属性，确保基接口中定义的 [Query]/[Path] 属性也能被识别
        var visitedProperties = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
        CollectInterfaceProperties(interfaceSymbol, propertyDecls, model, properties, visitedProperties);

        return properties;
    }

    private static void CollectInterfaceProperties(
        INamedTypeSymbol interfaceSymbol,
        Dictionary<string, PropertyDeclarationSyntax> propertyDecls,
        SemanticModel model,
        List<InterfacePropertyInfo> properties,
        HashSet<IPropertySymbol> visitedProperties)
    {
        foreach (var property in interfaceSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (!visitedProperties.Add(property))
                continue;

            var queryAttr = property.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "QueryAttribute");

            var pathAttr = property.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "PathAttribute");

            var headerAttr = property.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "HeaderAttribute");

            // 仅当前接口的语法树中有对应的属性声明；基接口（尤其跨程序集）的属性声明可能不可达，传 null 安全处理
            propertyDecls.TryGetValue(property.Name, out var propertyDecl);

            if (queryAttr != null)
            {
                properties.Add(CreatePropertyInfo(property, queryAttr, "Query", propertyDecl, model));
            }
            else if (pathAttr != null)
            {
                properties.Add(CreatePropertyInfo(property, pathAttr, "Path", propertyDecl, model));
            }
            else if (headerAttr != null)
            {
                properties.Add(CreatePropertyInfo(property, headerAttr, "Header", propertyDecl, model));
            }
        }

        // 递归处理所有基接口
        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            foreach (var property in baseInterface.GetMembers().OfType<IPropertySymbol>())
            {
                if (!visitedProperties.Add(property))
                    continue;

                var queryAttr = property.GetAttributes()
                        .FirstOrDefault(attr => attr.AttributeClass?.Name == "QueryAttribute");

                var pathAttr = property.GetAttributes()
                        .FirstOrDefault(attr => attr.AttributeClass?.Name == "PathAttribute");

                var headerAttr = property.GetAttributes()
                        .FirstOrDefault(attr => attr.AttributeClass?.Name == "HeaderAttribute");

                // 基接口的语法节点通常不在当前接口的语法树中，传 null
                PropertyDeclarationSyntax? propertyDecl = null;
                // 尝试从基接口的语法引用中获取属性声明
                var syntaxRef = property.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxRef != null)
                {
                    try
                    {
                        var syntax = syntaxRef.GetSyntax();
                        if (syntax is PropertyDeclarationSyntax pds)
                            propertyDecl = pds;
                    }
                    catch
                    {
                        // 跨程序集引用可能无法获取语法节点，忽略
                    }
                }

                if (queryAttr != null)
                {
                    properties.Add(CreatePropertyInfo(property, queryAttr, "Query", propertyDecl, model));
                }
                else if (pathAttr != null)
                {
                    properties.Add(CreatePropertyInfo(property, pathAttr, "Path", propertyDecl, model));
                }
                else if (headerAttr != null)
                {
                    properties.Add(CreatePropertyInfo(property, headerAttr, "Header", propertyDecl, model));
                }
            }
        }
    }

    private static InterfacePropertyInfo CreatePropertyInfo(IPropertySymbol property, AttributeData attribute, string attributeType, PropertyDeclarationSyntax? propertyDecl, SemanticModel model)
    {
        var propertyInfo = new InterfacePropertyInfo
        {
            Name = property.Name,
            Type = TypeSymbolHelper.GetTypeFullName(property.Type),
            AttributeType = attributeType,
            // GEN-05 修复：捕获接口属性是否为只读，用于决定生成的实现属性是否包含 setter。
            IsReadOnly = property.IsReadOnly
        };

        if (attribute.ConstructorArguments.Length > 0)
        {
            propertyInfo.ParameterName = attribute.ConstructorArguments[0].Value?.ToString();
        }

        foreach (var namedArg in attribute.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "Name":
                    propertyInfo.ParameterName = namedArg.Value.Value?.ToString();
                    break;
                case "FormatString":
                    propertyInfo.Format = namedArg.Value.Value?.ToString();
                    break;
                case "Format":
                    propertyInfo.Format = namedArg.Value.Value?.ToString();
                    break;
                case "UrlEncode":
                    if (namedArg.Value.Value is bool urlEncode)
                        propertyInfo.UrlEncode = urlEncode;
                    break;
                case "Replace":
                    if (namedArg.Value.Value is bool replace)
                        propertyInfo.Replace = replace;
                    break;
                case "AliasAs":
                    propertyInfo.AliasAs = namedArg.Value.Value?.ToString();
                    break;
            }
        }

        // 对于 Header 属性，如果未通过构造函数或 Name 指定名称，则尝试使用 AliasAs
        if (attributeType == "Header" && string.IsNullOrEmpty(propertyInfo.ParameterName))
        {
            if (!string.IsNullOrEmpty(propertyInfo.AliasAs))
            {
                propertyInfo.ParameterName = propertyInfo.AliasAs;
            }
            else
            {
                propertyInfo.ParameterName = property.Name;
            }
        }
        else if (string.IsNullOrEmpty(propertyInfo.ParameterName))
        {
            propertyInfo.ParameterName = property.Name;
        }

        if (propertyDecl?.Initializer != null)
        {
            var constantValue = model.GetConstantValue(propertyDecl.Initializer.Value);
            if (constantValue.HasValue && constantValue.Value != null)
            {
                propertyInfo.DefaultValue = TypeConverter.GetDefaultValueLiteral(property.Type, constantValue.Value);
            }
            else
            {
                propertyInfo.DefaultValue = propertyDecl.Initializer.Value.ToString();
            }
        }
        else if (property.Type.IsValueType)
        {
            propertyInfo.DefaultValue = "default";
        }

        return propertyInfo;
    }

    /// <summary>
    /// 分析头部合并模式（方法级优先于接口级）
    /// </summary>
    private static string AnalyzeHeaderMergeMode(IMethodSymbol methodSymbol, ImmutableArray<AttributeData> methodAttributes, ImmutableArray<AttributeData> interfaceAttrs)
    {
        var methodAttr = methodAttributes
            .FirstOrDefault(attr => HttpClientGeneratorConstants.HeaderMergeAttributeNames.Contains(attr.AttributeClass?.Name));

        if (methodAttr != null)
        {
            var mode = methodAttr.ConstructorArguments.Length > 0
                ? methodAttr.ConstructorArguments[0].Value?.ToString()
                : null;
            if (!string.IsNullOrEmpty(mode))
                return mode;
        }

        var interfaceAttr = interfaceAttrs
            .FirstOrDefault(attr => HttpClientGeneratorConstants.HeaderMergeAttributeNames.Contains(attr.AttributeClass?.Name));

        if (interfaceAttr != null)
        {
            var mode = interfaceAttr.ConstructorArguments.Length > 0
                ? interfaceAttr.ConstructorArguments[0].Value?.ToString()
                : null;
            if (!string.IsNullOrEmpty(mode))
                return mode;
        }

        return "Append";
    }

    /// <summary>
    /// 分析序列化方法（方法级优先于接口级）
    /// </summary>
    private static string AnalyzeSerializationMethod(IMethodSymbol methodSymbol, ImmutableArray<AttributeData> methodAttributes, ImmutableArray<AttributeData> interfaceAttrs)
    {
        var methodAttr = methodAttributes
            .FirstOrDefault(attr => HttpClientGeneratorConstants.SerializationMethodAttributeNames.Contains(attr.AttributeClass?.Name));

        if (methodAttr != null)
        {
            var method = methodAttr.ConstructorArguments.Length > 0
                ? methodAttr.ConstructorArguments[0].Value?.ToString()
                : null;
            if (!string.IsNullOrEmpty(method))
                return method;
        }

        var interfaceAttr = interfaceAttrs
            .FirstOrDefault(attr => HttpClientGeneratorConstants.SerializationMethodAttributeNames.Contains(attr.AttributeClass?.Name));

        if (interfaceAttr != null)
        {
            var method = interfaceAttr.ConstructorArguments.Length > 0
                ? interfaceAttr.ConstructorArguments[0].Value?.ToString()
                : null;
            if (!string.IsNullOrEmpty(method))
                return method;
        }

        return "Json";
    }

    /// <summary>
    /// 分析方法是否标记了 [AllowAnyStatusCode] 特性。
    /// 方法级特性优先于接口级特性。
    /// </summary>
    private static bool AnalyzeAllowAnyStatusCode(
        IMethodSymbol methodSymbol,
        ImmutableArray<AttributeData> methodAttributes,
        ImmutableArray<AttributeData> interfaceAttrs)
    {
        var methodHasAttr = methodAttributes
            .Any(attr => HttpClientGeneratorConstants.AllowAnyStatusCodeAttributeNames.Contains(attr.AttributeClass?.Name));

        if (methodHasAttr)
            return true;

        return interfaceAttrs
            .Any(attr => HttpClientGeneratorConstants.AllowAnyStatusCodeAttributeNames.Contains(attr.AttributeClass?.Name));
    }

    /// <summary>
    /// 分析接口特性
    /// </summary>
    private static (HashSet<string> interfaceAttributes, List<InterfaceHeaderAttributeInfo> interfaceHeaderAttributes, string? interfaceTokenInjectionMode, string? interfaceTokenName, string? interfaceTokenScopes)
        AnalyzeInterfaceAttributes(ImmutableArray<AttributeData> interfaceAttrs)
    {
        var interfaceAttributes = new HashSet<string>();
        var interfaceHeaderAttributes = new List<InterfaceHeaderAttributeInfo>();
        string? interfaceTokenInjectionMode = null;
        string? interfaceTokenName = null;
        string? interfaceTokenScopes = null;

        if (!interfaceAttrs.IsDefault)
        {
            var headerAttributes = interfaceAttrs
                .Where(attr => HasAttributeWithName(attr, "HeaderAttribute"));

            foreach (var headerAttr in headerAttributes)
            {
                var headerName = GetHeaderName(headerAttr);
                var interfaceHeaderAttr = new InterfaceHeaderAttributeInfo
                {
                    Name = headerName,
                    Value = GetHeaderValue(headerAttr),
                    Replace = GetHeaderReplace(headerAttr)
                };

                interfaceHeaderAttributes.Add(interfaceHeaderAttr);

                var isAuthorizationHeader = AttributeDataHelper.GetStringValueFromAttribute(headerAttr, ["AliasAs", "Name"], 0) == "Authorization";
                if (isAuthorizationHeader)
                {
                    interfaceAttributes.Add($"Header:{headerName}");
                }
            }

            var queryAttributes = interfaceAttrs
                .Where(attr => HasAttributeWithName(attr, "QueryAttribute") &&
                               attr.ConstructorArguments.Length > 0 &&
                               attr.ConstructorArguments[0].Value?.ToString() == "Authorization");

            foreach (var queryAttr in queryAttributes)
            {
                var aliasAs = queryAttr.NamedArguments.FirstOrDefault(arg => arg.Key == "AliasAs").Value.Value?.ToString();
                var queryName = string.IsNullOrEmpty(aliasAs) ? "Authorization" : aliasAs;
                interfaceAttributes.Add($"Query:{queryName}");
            }

            // 处理 Token 特性的注入模式
            var tokenAttributes = interfaceAttrs
                .Where(attr => HasAttributeWithName(attr, "TokenAttribute"));

            foreach (var tokenAttr in tokenAttributes)
            {
                var injectionMode = GetTokenInjectionMode(tokenAttr);
                var tokenName = GetTokenName(tokenAttr);
                var tokenScopes = GetTokenScopes(tokenAttr);
                if (!string.IsNullOrEmpty(injectionMode))
                {
                    interfaceTokenInjectionMode = injectionMode;
                    interfaceTokenName = tokenName;
                    interfaceTokenScopes = tokenScopes;
                    interfaceAttributes.Add($"Token:{injectionMode}:{tokenName}");
                }
            }
        }

        return (interfaceAttributes, interfaceHeaderAttributes, interfaceTokenInjectionMode, interfaceTokenName, interfaceTokenScopes);
    }

    /// <summary>
    /// 获取Token特性的InjectionMode值
    /// </summary>
    private static string? GetTokenInjectionMode(AttributeData tokenAttr)
    {
        if (tokenAttr == null)
            return HttpClientGeneratorConstants.TokenInjectionModeHeader;

        // 先尝试从命名参数获取
        foreach (var namedArg in tokenAttr.NamedArguments)
        {
            if (namedArg.Key == HttpClientGeneratorConstants.TokenInjectionModeProperty)
            {
                return GetTokenInjectionModeName(namedArg.Value.Value);
            }
        }

        return HttpClientGeneratorConstants.TokenInjectionModeHeader;
    }

    /// <summary>
    /// 获取Token特性的Name值
    /// </summary>
    private static string? GetTokenName(AttributeData tokenAttr)
    {
        if (tokenAttr == null)
            return null;

        foreach (var namedArg in tokenAttr.NamedArguments)
        {
            if (namedArg.Key == HttpClientGeneratorConstants.TokenNameProperty)
            {
                return namedArg.Value.Value?.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// 获取Token特性的Scopes值
    /// </summary>
    private static string? GetTokenScopes(AttributeData tokenAttr)
    {
        if (tokenAttr == null)
            return null;

        foreach (var namedArg in tokenAttr.NamedArguments)
        {
            if (namedArg.Key == "Scopes")
            {
                return namedArg.Value.Value?.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// 获取Header特性的名称
    /// </summary>
    private static string GetHeaderName(AttributeData headerAttr)
    {
        return AttributeDataHelper.GetStringValueFromAttribute(headerAttr, ["AliasAs", "Name"], 0, "Unknown") ?? "Unknown";
    }

    /// <summary>
    /// 从已缓存的特性列表中分析方法级别的 Token Scopes
    /// </summary>
    private static string? AnalyzeMethodTokenScopes(ImmutableArray<AttributeData> attributes)
    {
        var tokenAttr = attributes
            .FirstOrDefault(attr => HasAttributeWithName(attr, "TokenAttribute"));

        return tokenAttr != null ? GetTokenScopes(tokenAttr) : null;
    }

    /// <summary>
    /// 从已缓存的特性列表中分析方法级别 Token 特性的 TokenManagerKey 和 RequiresUserId
    /// </summary>
    private static (string? tokenManagerKey, bool? requiresUserId, string? injectionMode) AnalyzeMethodTokenExtended(ImmutableArray<AttributeData> attributes)
    {
        var tokenAttr = attributes
            .FirstOrDefault(attr => HasAttributeWithName(attr, "TokenAttribute"));

        if (tokenAttr == null)
            return (null, null, null);

        var tokenManagerKey = TokenHelper.GetTokenManagerKeyFromAttribute(tokenAttr);
        var requiresUserIdValue = tokenAttr.NamedArguments
            .FirstOrDefault(na => na.Key.Equals("RequiresUserId", StringComparison.OrdinalIgnoreCase)).Value.Value;

        bool? requiresUserId = requiresUserIdValue is bool b ? b : (bool?)null;
        var injectionMode = GetTokenInjectionMode(tokenAttr);

        return (tokenManagerKey, requiresUserId, injectionMode);
    }

    /// <summary>
    /// 获取Header特性的值
    /// </summary>
    private static object? GetHeaderValue(AttributeData headerAttr)
    {
        var valueProperty = headerAttr.NamedArguments.FirstOrDefault(arg => arg.Key == "Value").Value.Value;
        if (valueProperty != null)
            return valueProperty;

        if (headerAttr.ConstructorArguments.Length > 1)
        {
            return headerAttr.ConstructorArguments[1].Value;
        }

        return null;
    }

    /// <summary>
    /// 获取Header特性的Replace设置
    /// </summary>
    private static bool GetHeaderReplace(AttributeData headerAttr)
    {
        return AttributeDataHelper.GetBoolValueFromAttribute(headerAttr, "Replace", false);
    }

    /// <summary>
    /// 检查特性是否具有指定的名称（支持带或不带 Attribute 后缀）
    /// </summary>
    private static bool HasAttributeWithName(AttributeData attr, string attributeName)
    {
        var name = attr.AttributeClass?.Name;
        return name == attributeName || name == attributeName.Replace("Attribute", "");
    }

    private static (bool enabled, int durationSeconds, string? keyTemplate, bool varyByUser) AnalyzeCacheAttribute(ImmutableArray<AttributeData> attributes)
    {
        var cacheAttr = attributes
            .FirstOrDefault(attr => HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name));

        if (cacheAttr == null)
            return (false, 300, null, false);

        var durationSeconds = AttributeDataHelper.GetAttributeIntValue(
            cacheAttr, 0, HttpClientGeneratorConstants.CacheDurationSecondsProperty, 300);

        var keyTemplate = AttributeDataHelper.GetStringValueFromAttribute(
            cacheAttr, [HttpClientGeneratorConstants.CacheKeyTemplateProperty]);

        var varyByUser = AttributeDataHelper.GetBoolValueFromAttribute(
            cacheAttr, HttpClientGeneratorConstants.CacheVaryByUserProperty);

        return (true, durationSeconds, keyTemplate, varyByUser);
    }

    private static (bool enabled, int maxRetries, int delayMilliseconds, bool useExponentialBackoff) AnalyzeRetryAttribute(ImmutableArray<AttributeData> attributes)
    {
        var retryAttr = attributes
            .FirstOrDefault(attr => HttpClientGeneratorConstants.RetryAttributeNames.Contains(attr.AttributeClass?.Name));

        if (retryAttr == null)
            return (false, 3, 1000, true);

        var maxRetries = AttributeDataHelper.GetAttributeIntValue(
            retryAttr, 0, HttpClientGeneratorConstants.RetryMaxRetriesProperty, 3);

        var delayMilliseconds = AttributeDataHelper.GetIntValueFromAttribute(
            retryAttr, HttpClientGeneratorConstants.RetryDelayMillisecondsProperty, 1000);

        var useExponentialBackoff = AttributeDataHelper.GetBoolValueFromAttribute(
            retryAttr, HttpClientGeneratorConstants.RetryUseExponentialBackoffProperty, true);

        return (true, maxRetries, delayMilliseconds, useExponentialBackoff);
    }

    private static (bool enabled, int failureThreshold, int breakDurationSeconds, int samplingDurationSeconds, int minimumThroughput) AnalyzeCircuitBreakerAttribute(ImmutableArray<AttributeData> attributes)
    {
        var cbAttr = attributes
            .FirstOrDefault(attr => HttpClientGeneratorConstants.CircuitBreakerAttributeNames.Contains(attr.AttributeClass?.Name));

        if (cbAttr == null)
            return (false, 5, 30, 0, 10);

        var failureThreshold = AttributeDataHelper.GetAttributeIntValue(
            cbAttr, 0, HttpClientGeneratorConstants.CircuitBreakerFailureThresholdProperty, 5);

        var breakDurationSeconds = AttributeDataHelper.GetIntValueFromAttribute(
            cbAttr, HttpClientGeneratorConstants.CircuitBreakerBreakDurationSecondsProperty, 30);

        var samplingDurationSeconds = AttributeDataHelper.GetIntValueFromAttribute(
            cbAttr, HttpClientGeneratorConstants.CircuitBreakerSamplingDurationSecondsProperty, 0);

        var minimumThroughput = AttributeDataHelper.GetIntValueFromAttribute(
            cbAttr, HttpClientGeneratorConstants.CircuitBreakerMinimumThroughputProperty, 10);

        return (true, failureThreshold, breakDurationSeconds, samplingDurationSeconds, minimumThroughput);
    }

    private static (bool enabled, int timeoutMilliseconds) AnalyzeTimeoutAttribute(ImmutableArray<AttributeData> attributes)
    {
        var timeoutAttr = attributes
            .FirstOrDefault(attr => HttpClientGeneratorConstants.TimeoutAttributeNames.Contains(attr.AttributeClass?.Name));

        if (timeoutAttr == null)
            return (false, 0);

        var timeoutMilliseconds = 0;

        if (timeoutAttr.ConstructorArguments.Length > 0 &&
            timeoutAttr.ConstructorArguments[0].Value is int constructorTimeout)
        {
            timeoutMilliseconds = constructorTimeout;
        }

        if (timeoutMilliseconds <= 0)
        {
            timeoutMilliseconds = AttributeDataHelper.GetIntValueFromAttribute(
                timeoutAttr, HttpClientGeneratorConstants.TimeoutMillisecondsProperty, 0);
        }

        return (true, timeoutMilliseconds);
    }
}
