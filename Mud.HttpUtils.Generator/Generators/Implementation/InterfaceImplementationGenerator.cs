// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using Mud.HttpUtils;
using Mud.HttpUtils.Analyzers;
using Mud.HttpUtils.Generators.Base;
using Mud.HttpUtils.Generators.Context;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 接口实现生成器（流程编排器）
/// </summary>
internal class InterfaceImplementationGenerator
{
    /// <summary>
    /// 类型解析缓存：以 Compilation 为主键的 ConditionalWeakTable，Compilation 变化时自动失效；
    /// 内层以类型名为键缓存解析结果（包括 null），避免对同一 Compilation 重复执行全局命名空间扫描。
    /// </summary>
    // NEW-GEN-02 说明：ConditionalWeakTable 依赖 Compilation 的 GC 回收自动清理。
    // 在 IDE 场景下 Compilation 可能保持较长时间引用，但这是 Roslyn 源生成器的标准缓存模式。
    // 如遇内存问题，可考虑改用 IncrementalValueProvider 提供的缓存机制。
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<string, INamedTypeSymbol?>> _typeResolveCache = new();

    private readonly Compilation _compilation;
    private readonly InterfaceDeclarationSyntax _interfaceDecl;
    private readonly SourceProductionContext _context;
    private readonly INamedTypeSymbol _interfaceSymbol;
    private readonly SemanticModel _semanticModel;
    private readonly string _optionsName;
    private readonly bool _isAotEnabled;
    private readonly bool _emitNullableEnable;
    private readonly bool _emitGeneratedCodeMarkers;

    /// <summary>
    /// 生成代码缓冲区。在 <see cref="GenerateCode"/> 中于 <see cref="GeneratorContext"/> 构造之后按
    /// 实际方法数分配容量（§6.5：避免构造期二次遍历接口方法树）。
    /// </summary>
    private StringBuilder _codeBuilder = null!;

    public InterfaceImplementationGenerator(
        Compilation compilation,
        InterfaceDeclarationSyntax interfaceDecl,
        INamedTypeSymbol interfaceSymbol,
        SemanticModel semanticModel,
        SourceProductionContext context,
        string optionsName,
        bool isAotEnabled = false,
        bool emitNullableEnable = true,
        bool emitGeneratedCodeMarkers = true)
    {
        _compilation = compilation;
        _interfaceDecl = interfaceDecl;
        _interfaceSymbol = interfaceSymbol;
        _semanticModel = semanticModel;
        _context = context;
        _optionsName = optionsName;
        _isAotEnabled = isAotEnabled;
        _emitNullableEnable = emitNullableEnable;
        _emitGeneratedCodeMarkers = emitGeneratedCodeMarkers;
    }

    /// <summary>
    /// 生成代码入口
    /// </summary>
    public void GenerateCode()
    {
        // [E-5 修复] 接口级 [IgnoreGenerator]：生成器完全不介入（用户自备实现），
        // 亦不产生生成期诊断（AOT/MUD 由分析器各自豁免）。
        // 必须早于 ExtractConfigurationFromAttributes 与 ValidateConfiguration，
        // 确保既不产出源码也不产生生成期诊断。
        if (Mud.HttpUtils.Analyzers.GeneratorAttributeFilters.HasIgnoreGenerator(_interfaceSymbol))
            return;

        var configuration = ExtractConfigurationFromAttributes();

        // GEN-04 修复：当 TokenManagerKey 和 TokenType 均未显式指定时，发出警告诊断。
        // 生成器会使用默认值（GetDefaultTokenType），多接口共享同一 TokenManager 场景下可能产生注册冲突。
        // 注意：此处使用 !string.IsNullOrEmpty(configuration.TokenManager) 而非 GeneratorContext.HasTokenManager，
        // 因为 GeneratorContext 尚未构造（在 ExtractConfigurationFromAttributes 之后、new GeneratorContext 之前）。
        if (!string.IsNullOrEmpty(configuration.TokenManager)
            && string.IsNullOrEmpty(configuration.TokenManagerKey)
            && string.IsNullOrEmpty(configuration.TokenType))
        {
            var defaultKeyType = TokenHelper.GetDefaultTokenType();
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.TokenManagerKeyInferredFromDefault,
                _interfaceDecl.GetLocation(),
                _interfaceSymbol.Name,
                defaultKeyType));
        }

        if (!ValidateConfiguration(configuration))
            return;

        var generatorContext = new GeneratorContext(
            _compilation,
            _interfaceSymbol,
            _interfaceDecl,
            _semanticModel,
            _context,
            configuration,
            _isAotEnabled,
            _emitNullableEnable,
            _emitGeneratedCodeMarkers);

        // [Phase5 修复 3.5] 容量估算复用 GeneratorContext 已算出的 AllMethods，
        // 不再二次调用 TypeSymbolHelper.GetAllMethods 遍历接口方法树。
        _codeBuilder = new StringBuilder(EstimateCodeCapacity(generatorContext.AllMethods.Count));

        // InterfaceProperties 已在 GeneratorContext 构造函数中预计算（含基接口 [Query]/[Path] 属性），
        // 后续 GetOrAnalyzeMethod 会将其作为 cachedInterfaceProperties 传入 AnalyzeMethod，避免重复扫描。

        ComputeAnyMethodRequiresUserId(generatorContext);

        DetectICurrentUserId(generatorContext);

        PrecomputeXmlResponseTypes(generatorContext);

        // M2-#12：非幂等方法声明 [Retry] 但未显式 AllowNonIdempotent 时发出 Warning
        ReportRetryNonIdempotentWithoutAllow(generatorContext);

        // CFG-07：方法级 [Timeout] 超过接口级 HttpClient 超时时发出 Warning
        ReportMethodTimeoutConflicts(generatorContext);

        // CFG-29 / CFG-32：方法级 [CircuitBreaker] / [Timeout] 特性参数值域校验（Error）
        ReportResilienceAttributeValueRangeViolations(generatorContext);

        // 必须在生成器循环之前登记：MethodGenerator 需要据此决定是否补发占位方法，
        // 而其执行顺序早于 AccessTokenGenerator 等后续片段生成器。
        RegisterInfrastructureMembers(generatorContext);

        var generators = InitializeGenerators(generatorContext);

        foreach (var generator in generators)
        {
            generator.Generate(_codeBuilder, generatorContext);
        }

        if (generatorContext.HasQueryMap)
        {
            _codeBuilder.AppendLine();
            // 该包装方法委托给已标注 RUC/RDC 的 QueryMapHelper（运行时反射展平）。
            // 它仅作为 TypeSymbol 不可用时的兜底（真实项目走编译期内联展平），
            // 因此局部豁免 AOT 分析告警，避免污染消费方的生成代码构建（严格模式下会变为错误）。
            _codeBuilder.AppendLine("#pragma warning disable IL2026, IL3050");
            _codeBuilder.AppendLine("        private static void FlattenObjectToQueryParams(");
            _codeBuilder.AppendLine("            object obj,");
            _codeBuilder.AppendLine("            string prefix,");
            _codeBuilder.AppendLine("            string separator,");
            _codeBuilder.AppendLine("            global::Mud.HttpUtils.QueryParameterBuilder queryParams,");
            _codeBuilder.AppendLine("            bool includeNullValues,");
            _codeBuilder.AppendLine("            bool useJsonSerialization,");
            _codeBuilder.AppendLine("            bool urlEncode = true,");
            _codeBuilder.AppendLine("            System.Collections.Generic.List<string>? rawPairs = null,");
            _codeBuilder.AppendLine("            int depth = 0,");
            _codeBuilder.AppendLine("            global::Mud.HttpUtils.IHttpContentSerializer? contentSerializer = null)");
            _codeBuilder.AppendLine("        {");
            _codeBuilder.AppendLine("            QueryMapHelper.FlattenObjectToQueryParams(obj, prefix, separator, queryParams, includeNullValues, useJsonSerialization, urlEncode, rawPairs, depth, contentSerializer);");
            _codeBuilder.AppendLine("        }");
            _codeBuilder.AppendLine("#pragma warning restore IL2026, IL3050");
        }

        _codeBuilder.AppendLine("    }");
        _codeBuilder.AppendLine("}");
        _codeBuilder.AppendLine();

        // FIX-03: hintName 必须唯一。原实现仅用 ClassName（来自 interfaceSymbol.Name，不含元数），
        // 导致 IFoo<T> 与 IFoo（同命名空间）产生相同 hintName → CS8785 全量产物消失。
        // 修复：纳入泛型元数（`1, `2…）与嵌套类型路径（Outer_）。
        var namespacePath = generatorContext.NamespaceName.Replace('.', '/');
        var nestingSuffix = BuildNestingSuffix(_interfaceSymbol);
        var aritySuffix = _interfaceSymbol.Arity > 0 ? $"`{_interfaceSymbol.Arity}" : string.Empty;
        var fullClassName = $"{nestingSuffix}{generatorContext.ClassName}{aritySuffix}";
        var fileName = string.IsNullOrEmpty(namespacePath)
            ? $"{fullClassName}.g.cs"
            : $"{namespacePath}/{fullClassName}.g.cs";
        TransitiveCodeGenerator.AddSourceValidated(_context, fileName, _codeBuilder.ToString());
    }

    /// <summary>
    /// 验证生成配置的合法性
    /// </summary>
    private bool ValidateConfiguration(GenerationConfiguration configuration)
    {
        var isValid = true;

        if (_interfaceSymbol.IsGenericType)
        {
            // [v2.4 §3.2] 泛型接口现支持代码生成（类型参数转发），不再阻断。
            // 保留 Info 级诊断告知用户生成器已感知泛型接口。
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientApiGenericInterfaceNotSupported,
                _interfaceDecl.GetLocation(),
                _interfaceSymbol.Name));
        }

        if (!string.IsNullOrEmpty(configuration.HttpClient) && !string.IsNullOrEmpty(configuration.RawTokenManager))
        {
            // [Phase1 修复 1.2] 诊断 Location 定位到 [HttpClientApi] 特性语法位置，
            // 使 CodeFix 能通过 FindToken(span.Start).Parent?.FirstAncestorOrSelf<AttributeSyntax>() 命中。
            var mutualExclusionLocation = GetHttpClientApiAttributeLocation() ?? _interfaceDecl.GetLocation();
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientAndTokenManagerMutuallyExclusive,
                mutualExclusionLocation,
                _interfaceSymbol.Name));
            isValid = false;
        }

        if (!string.IsNullOrEmpty(configuration.HttpClient))
        {
            // HttpClient 类型校验仅输出诊断不阻断生成：GetTypeByMetadataName 需要完全限定名，
            // 用户可能使用短名称引用同编译中的类型，阻断会导致误报。错误诊断已足以提示用户。
            ValidateHttpClientType(configuration.HttpClient!);
        }

        if (!string.IsNullOrEmpty(configuration.TokenManager))
        {
            if (!ValidateTokenManagerType(configuration))
                isValid = false;
        }

        if (!string.IsNullOrEmpty(configuration.InheritedFrom))
        {
            var hasTokenManager = !string.IsNullOrEmpty(configuration.TokenManager);
            var validationResult = BaseClassValidator.ValidateBaseClass(
                _compilation,
                configuration.InheritedFrom!,
                hasTokenManager,
                _interfaceSymbol.ContainingNamespace);

            if (!validationResult.IsValid)
            {
                _context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.HttpClientApiGenerationError,
                    _interfaceDecl.GetLocation(),
                    _interfaceSymbol.Name,
                    validationResult.ErrorMessage ?? $"基类 '{configuration.InheritedFrom}' 验证失败"));
                isValid = false;
            }
        }

        return isValid;
    }

    private void ValidateHttpClientType(string httpClientType)
    {
        if (string.IsNullOrWhiteSpace(httpClientType))
            return;

        if (httpClientType == "IEnhancedHttpClient" || httpClientType == "IBaseHttpClient")
            return;

        var typeSymbol = _compilation.GetTypeByMetadataName(httpClientType);

        if (typeSymbol == null)
        {
            var candidates = new[]
            {
                httpClientType,
                $"Mud.HttpUtils.{httpClientType}",
                $"System.Net.Http.{httpClientType}"
            };

            foreach (var candidate in candidates)
            {
                typeSymbol = _compilation.GetTypeByMetadataName(candidate);
                if (typeSymbol != null)
                    break;
            }
        }

        if (typeSymbol == null)
        {
            // [E-4 修复] HTTPCLIENT014 定位到 [HttpClientApi] 特性语法，而非整个接口声明。
            var location = GetHttpClientApiAttributeLocation() ?? _interfaceDecl.GetLocation();
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientTypeNotFound,
                location,
                _interfaceSymbol.Name,
                httpClientType));
        }
    }

    /// <summary>
    /// 定位 [HttpClientApi] 特性的语法位置（E-4），未找到时返回 null。
    /// </summary>
    private Location? GetHttpClientApiAttributeLocation()
    {
        var attribute = _interfaceSymbol.GetAttributes()
            .FirstOrDefault(a => HttpClientGeneratorConstants.HttpClientApiAttributeNames.Contains(a.AttributeClass?.Name));
        return attribute?.ApplicationSyntaxReference?.GetSyntax()?.GetLocation();
    }

    private bool ValidateTokenManagerType(GenerationConfiguration configuration)
    {
        if (string.IsNullOrEmpty(configuration.TokenManager))
            return true;

        var tokenManagerTypeName = configuration.TokenManager!;
        var tokenManagerType = ResolveType(tokenManagerTypeName);

        if (tokenManagerType == null)
        {
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.TokenManagerTypeNotFound,
                _interfaceDecl.GetLocation(),
                _interfaceSymbol.Name,
                tokenManagerTypeName));
            return false;
        }

        var getDefaultAppMethod = TypeSymbolHelper.GetAllMethods(tokenManagerType, includeParentInterfaces: true)
            .FirstOrDefault(m => m.Name == "GetDefaultApp" && m.Parameters.IsEmpty && m.TypeParameters.IsEmpty);

        if (getDefaultAppMethod == null)
        {
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.TokenManagerMissingMethod,
                _interfaceDecl.GetLocation(),
                _interfaceSymbol.Name,
                tokenManagerTypeName,
                "GetDefaultApp()"));
            return false;
        }

        var mudAppContextType = _compilation.GetTypeByMetadataName("Mud.HttpUtils.IMudAppContext");
        if (mudAppContextType != null && getDefaultAppMethod.ReturnType != null)
        {
            var returnType = getDefaultAppMethod.ReturnType;
            if (!SymbolEqualityComparer.Default.Equals(returnType, mudAppContextType) &&
                returnType.AllInterfaces.All(i => !SymbolEqualityComparer.Default.Equals(i, mudAppContextType)))
            {
                _context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.TokenManagerMissingMethod,
                    _interfaceDecl.GetLocation(),
                    _interfaceSymbol.Name,
                    tokenManagerTypeName,
                    $"GetDefaultApp() 返回类型 '{returnType.ToDisplayString()}' 不是 IMudAppContext 或其子类型"));
                return false;
            }
        }

        var getAppMethod = TypeSymbolHelper.GetAllMethods(tokenManagerType, includeParentInterfaces: true)
            .FirstOrDefault(m => m.Name == "GetApp" && m.Parameters.Length == 1 &&
                m.Parameters[0].Type.SpecialType == SpecialType.System_String);

        if (getAppMethod == null)
        {
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.TokenManagerMissingMethod,
                _interfaceDecl.GetLocation(),
                _interfaceSymbol.Name,
                tokenManagerTypeName,
                "GetApp(string appKey)"));
            return false;
        }

        return true;
    }

    private INamedTypeSymbol? ResolveType(string typeName)
    {
        // 使用缓存避免对同一 Compilation 重复执行全局命名空间扫描。
        // 缓存 null 结果（ContainsKey + null value）以跳过已知无法解析的类型名。
        var innerCache = _typeResolveCache.GetOrCreateValue(_compilation);
        return innerCache.GetOrAdd(typeName, ResolveTypeUncached);
    }

    private INamedTypeSymbol? ResolveTypeUncached(string typeName)
    {
        var type = _compilation.GetTypeByMetadataName(typeName);
        if (type != null)
            return type;

        type = _compilation.GetTypeByMetadataName($"Mud.HttpUtils.{typeName}");
        if (type != null)
            return type;

        var namespacePrefix = _interfaceSymbol.ContainingNamespace?.ToDisplayString();
        if (!string.IsNullOrEmpty(namespacePrefix))
        {
            type = _compilation.GetTypeByMetadataName($"{namespacePrefix}.{typeName}");
            if (type != null)
                return type;
        }

        // 仅对简单类型名（不含 .）执行全局命名空间扫描。
        // 全限定名（含 .）应已由前面 GetTypeByMetadataName 解析，若未命中说明类型不存在。
        if (typeName.Contains('.'))
            return null;

        // 全局命名空间扫描作为最终回退，仅处理简单类型名
        foreach (var ns in _compilation.GlobalNamespace.GetNamespaceMembers())
        {
            type = FindTypeInNamespace(ns, typeName);
            if (type != null)
                return type;
        }

        return null;
    }

    private INamedTypeSymbol? FindTypeInNamespace(INamespaceSymbol ns, string typeName)
    {
        foreach (var member in ns.GetTypeMembers())
        {
            if (member.Name == typeName)
                return member;
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            var found = FindTypeInNamespace(childNs, typeName);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// 登记各片段生成器「按模式无条件发射」的成员名，供契约占位实现避让（避免 CS0111/CS0102）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这些成员与「接口是否声明」无关（例如 AppContext 模式的 <c>Current</c>/<c>BeginScope</c>、
    /// 令牌模式的 <c>GetTokenAsync</c>），无法由符号侧推导；若不登记，占位实现会与其重名冲突。
    /// </para>
    /// <para>
    /// 登记条件从宽：多登记只会少补一个占位成员，不会破坏编译；漏登记则会产生重复成员（编译错误）。
    /// <b>新增「无条件发射」成员时须同步本方法</b>（另见 <see cref="GeneratorContext.ProvidedMemberNames"/>）。
    /// </para>
    /// </remarks>
    private static void RegisterInfrastructureMembers(GeneratorContext context)
    {
        // 接口属性（[Query]/[Path]/[Header]）→ ConstructorGenerator.GenerateInterfaceProperties
        foreach (var property in context.InterfaceProperties)
            context.MarkMemberProvided(property.Name);

        // 当前用户 ID → ConstructorGenerator（ICurrentUserId / CacheVaryByUser 场景）
        if (context.ImplementsICurrentUserId || context.HasCacheVaryByUser)
            context.MarkMemberProvided("CurrentUserId");

        // AppContext 相关成员 → ConstructorGenerator.GenerateAppContextMembers / GenerateUseAppMethod
        if (!context.HasHttpClient)
        {
            context.MarkMemberProvided("Current");
            context.MarkMemberProvided("SwitchTo");
            context.MarkMemberProvided("BeginScope");
            context.MarkMemberProvided("UseApp");
            context.MarkMemberProvided("UseDefaultApp");
            context.MarkMemberProvided("UseDefaultAppScope");
        }

        // 令牌辅助成员 → AccessTokenGenerator / TokenMethodHelper
        if (context.HasTokenManager && !context.HasHttpClient)
        {
            context.MarkMemberProvided("GetTokenAsync");
            context.MarkMemberProvided("GetApiKeyAsync");
            context.MarkMemberProvided("ApplyHmacSignatureAsync");
            context.MarkMemberProvided("GetTokenManagerKey");
        }
    }

    /// <summary>
    /// 初始化代码片段生成器
    /// </summary>
    private IEnumerable<ICodeFragmentGenerator> InitializeGenerators(GeneratorContext context)
    {
        var generators = new List<ICodeFragmentGenerator>
        {
            new ClassStructureGenerator(_interfaceSymbol),
            new ConstructorGenerator(context),
            new MethodGenerator()
        };

        if (context.HasTokenManager && !context.HasHttpClient)
        {
            generators.Add(new AccessTokenGenerator(context));
        }

        // 契约补全必须最后执行：为前述生成器未实现的接口成员
        // （无法生成调用实现的方法、无 [Query]/[Path]/[Header] 的属性、索引器、事件）发射占位实现，
        // 保证实现类满足接口契约、不再产生 CS0535。
        // 依赖各生成器在 Generate 中登记的 GeneratorContext.ProvidedMemberNames 做避让。
        generators.Add(new InterfaceContractCompletionGenerator());

        return generators;
    }

    /// <summary>
    /// 估算生成的代码容量
    /// </summary>
    /// <param name="methodCount">
    /// 接口（含基接口）方法数，由 <see cref="GeneratorContext.AllMethods"/> 提供。
    /// [Phase5 修复 3.5] 原实现在构造函数中独立遍历接口方法树，与 GeneratorContext 的遍历重复。
    /// </param>
    private static int EstimateCodeCapacity(int methodCount)
    {
        if (methodCount <= 0)
            methodCount = 10;

        var estimatedCapacity = 2000 + (methodCount * 700);
        return Math.Min(estimatedCapacity, 30000);
    }

    /// <summary>
    /// 从特性提取配置
    /// </summary>
    private GenerationConfiguration ExtractConfigurationFromAttributes()
    {
        var httpClientApiAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(
            _interfaceSymbol,
            HttpClientGeneratorConstants.HttpClientApiAttributeNames);

        var isAbstract = AttributeDataHelper.GetBoolValueFromAttribute(
            httpClientApiAttribute,
            HttpClientGeneratorConstants.IsAbstractProperty);

        var inheritedFrom = AttributeDataHelper.GetStringValueFromAttribute(
            httpClientApiAttribute,
            HttpClientGeneratorConstants.InheritedFromProperty);

        // 自动检测 InheritedFrom：如果未显式指定，检查是否有带 [HttpClientApi(IsAbstract = true)] 的基接口
        var baseHasTokenManager = false;
        var baseHasAppAuthorizer = false;
        string? inheritedFromInterfaceName = null;
        if (string.IsNullOrEmpty(inheritedFrom))
        {
            foreach (var baseInterface in _interfaceSymbol.Interfaces)
            {
                var baseApiAttr = baseInterface.GetAttributes()
                    .FirstOrDefault(attr => HttpClientGeneratorConstants.HttpClientApiAttributeNames.Contains(attr.AttributeClass?.Name));
                if (baseApiAttr == null) continue;

                var isBaseAbstract = AttributeDataHelper.GetBoolValueFromAttribute(baseApiAttr, HttpClientGeneratorConstants.IsAbstractProperty);
                if (isBaseAbstract)
                {
                    inheritedFrom = TypeSymbolHelper.GetImplementationClassName(baseInterface.Name);
                    inheritedFromInterfaceName = baseInterface.Name;

                    // 检查基接口是否具有 TokenManage
                    var baseTokenManage = AttributeDataHelper.GetStringValueFromAttribute(baseApiAttr, HttpClientGeneratorConstants.TokenManageProperty);
                    var baseHttpClient = AttributeDataHelper.GetStringValueFromAttribute(baseApiAttr, HttpClientGeneratorConstants.HttpClientProperty);
                    baseHasTokenManager = !string.IsNullOrWhiteSpace(baseTokenManage) && string.IsNullOrWhiteSpace(baseHttpClient);
                    // 基类为非 HttpClient 模式（TokenManager / AppContext）时，其生成的抽象基类会声明 protected _appAuthorizer 字段。
                    baseHasAppAuthorizer = string.IsNullOrWhiteSpace(baseHttpClient);
                    break;
                }
            }
        }

        // 合并的 AllInterfaces 单次扫描：显式 InheritedFrom 反查 + Cache/Resilience 基接口特性检测。
        // auto-detect 分支已设置 inheritedFromInterfaceName，反查逻辑（inheritedFromInterfaceName == null）自动跳过；
        // 显式分支在此循环中同时完成反查和 Cache/Resilience 扫描，消除原先的两次独立 AllInterfaces 遍历。
        var baseHasCache = false;
        var baseHasResilience = false;
        if (!string.IsNullOrEmpty(inheritedFrom))
        {
            foreach (var baseInterface in _interfaceSymbol.AllInterfaces)
            {
                var baseApiAttr = baseInterface.GetAttributes()
                    .FirstOrDefault(attr => HttpClientGeneratorConstants.HttpClientApiAttributeNames.Contains(attr.AttributeClass?.Name));

                // 显式 InheritedFrom 反查：通过类名匹配基接口以确定 BaseHasTokenManager 和 InheritedFromInterfaceName
                if (inheritedFromInterfaceName == null)
                {
                    var baseImplName = TypeSymbolHelper.GetImplementationClassName(baseInterface.Name);
                    if (string.Equals(baseImplName, inheritedFrom, StringComparison.Ordinal))
                    {
                        inheritedFromInterfaceName = baseInterface.Name;
                        if (baseApiAttr != null)
                        {
                            var baseTokenManage = AttributeDataHelper.GetStringValueFromAttribute(baseApiAttr, HttpClientGeneratorConstants.TokenManageProperty);
                            var baseHttpClient = AttributeDataHelper.GetStringValueFromAttribute(baseApiAttr, HttpClientGeneratorConstants.HttpClientProperty);
                            baseHasTokenManager = !string.IsNullOrWhiteSpace(baseTokenManage) && string.IsNullOrWhiteSpace(baseHttpClient);
                            // 同上：基类非 HttpClient 模式时声明 protected _appAuthorizer，派生类改为透传而非重复声明。
                            baseHasAppAuthorizer = string.IsNullOrWhiteSpace(baseHttpClient);
                        }
                    }
                }

                // Cache/Resilience 扫描（需要 baseApiAttr 存在）
                if (baseApiAttr != null && (!baseHasCache || !baseHasResilience))
                {
                    try
                    {
                        var baseMethods = TypeSymbolHelper.GetAllMethods(baseInterface, true);
                        foreach (var method in baseMethods)
                        {
                            if (!baseHasCache && method.GetAttributes().Any(attr =>
                                HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name)))
                                baseHasCache = true;
                            if (!baseHasResilience && method.GetAttributes().Any(attr =>
                                HttpClientGeneratorConstants.RetryAttributeNames.Contains(attr.AttributeClass?.Name) ||
                                HttpClientGeneratorConstants.CircuitBreakerAttributeNames.Contains(attr.AttributeClass?.Name) ||
                                HttpClientGeneratorConstants.TimeoutAttributeNames.Contains(attr.AttributeClass?.Name)))
                                baseHasResilience = true;
                            if (baseHasCache && baseHasResilience) break;
                        }
                    }
                    catch { /* 忽略基接口方法解析异常 */ }
                }

                // 反查完成且 Cache/Resilience 全部检出时短路退出
                if (inheritedFromInterfaceName != null && baseHasCache && baseHasResilience) break;
            }
        }

        var httpClient = AttributeDataHelper.GetStringValueFromAttribute(
            httpClientApiAttribute,
            HttpClientGeneratorConstants.HttpClientProperty);

        var tokenManage = AttributeDataHelper.GetStringValueFromAttribute(
            httpClientApiAttribute,
            HttpClientGeneratorConstants.TokenManageProperty);

        var effectiveTokenManage = !string.IsNullOrWhiteSpace(httpClient) ? null : tokenManage;

        var tokenType = GetInterfaceTokenType();

        var tokenManagerKey = GetInterfaceTokenManagerKey();
        var requiresUserId = GetInterfaceRequiresUserId();
        var basePath = ExtractBasePath();

        return new GenerationConfiguration
        {
            HttpClientOptionsName = _optionsName,
            DefaultContentType = GetHttpClientApiContentTypeFromAttribute(httpClientApiAttribute),
            // CFG-03/CFG-21：Timeout 由 HttpInvokeRegistrationGenerator 从特性直接读取（含默认值），
            // GenerationConfiguration.Timeout 为死字段，已删除，不再在此赋值。
            IsAbstract = isAbstract,
            InheritedFrom = inheritedFrom,
            HttpClient = httpClient,
            TokenManager = effectiveTokenManage,
            RawTokenManager = tokenManage,
            TokenManagerType = !string.IsNullOrEmpty(effectiveTokenManage)
                ? TypeSymbolHelper.GetTypeAllDisplayString(_compilation, effectiveTokenManage!)
                : null,
            BaseHasCache = baseHasCache,
            BaseHasResilience = baseHasResilience,
            BaseHasTokenManager = baseHasTokenManager,
            BaseHasAppAuthorizer = baseHasAppAuthorizer,
            InheritedFromInterfaceName = inheritedFromInterfaceName,
            TokenType = tokenType,
            IsUserAccessToken = tokenType == "UserAccessToken",
            TokenManagerKey = tokenManagerKey,
            RequiresUserId = requiresUserId,
            BasePath = basePath
        };
    }

    /// <summary>
    /// 从特性获取内容类型
    /// </summary>
    private string GetHttpClientApiContentTypeFromAttribute(AttributeData? attribute)
    {
        if (attribute == null)
            return HttpClientGeneratorConstants.DefaultContentType;

        var contentTypeArg = attribute.NamedArguments.FirstOrDefault(a => a.Key == "ContentType");
        var contentType = contentTypeArg.Value.Value?.ToString();
        return string.IsNullOrEmpty(contentType) ? HttpClientGeneratorConstants.DefaultContentType : contentType!;
    }

    /// <summary>
    /// 从接口的 Token 特性中提取 TokenType 值
    /// </summary>
    private string? GetInterfaceTokenType()
    {
        var tokenAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(
            _interfaceSymbol,
            HttpClientGeneratorConstants.TokenAttributeNames);
        return TokenHelper.GetTokenTypeFromAttribute(tokenAttribute);
    }

    /// <summary>
    /// 从接口的 Token 特性中提取 TokenManagerKey 值
    /// </summary>
    private string? GetInterfaceTokenManagerKey()
    {
        var tokenAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(
            _interfaceSymbol,
            HttpClientGeneratorConstants.TokenAttributeNames);
        return TokenHelper.GetTokenManagerKeyFromAttribute(tokenAttribute);
    }

    /// <summary>
    /// 从接口的 Token 特性中提取 RequiresUserId 值
    /// </summary>
    private bool? GetInterfaceRequiresUserId()
    {
        var tokenAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(
            _interfaceSymbol,
            HttpClientGeneratorConstants.TokenAttributeNames);
        return TokenHelper.GetRequiresUserIdFromAttribute(tokenAttribute);
    }

    /// <summary>
    /// 从接口的 [BasePath] 特性中提取基础路径前缀
    /// </summary>
    private string? ExtractBasePath()
    {
        var basePathAttr = _interfaceSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "BasePathAttribute" || attr.AttributeClass?.Name == "BasePath");

        if (basePathAttr == null || basePathAttr.ConstructorArguments.Length == 0)
            return null;

        return basePathAttr.ConstructorArguments[0].Value?.ToString();
    }

    /// <summary>
    /// 计算是否有任何方法需要 UserId，设置 Configuration.AnyMethodRequiresUserId
    /// </summary>
    private void ComputeAnyMethodRequiresUserId(GeneratorContext context)
    {
        if (!context.HasTokenManager)
        {
            context.Configuration.AnyMethodRequiresUserId = false;
            return;
        }

        var interfaceRequiresUserId = context.Configuration.RequiresUserId ?? context.Configuration.IsUserAccessToken;
        if (interfaceRequiresUserId)
        {
            context.Configuration.AnyMethodRequiresUserId = true;
            return;
        }

        var allMethods = context.AllMethods;
        foreach (var method in allMethods)
        {
            var methodRequiresUserId = GetMethodRequiresUserId(method);
            if (methodRequiresUserId == true)
            {
                context.Configuration.AnyMethodRequiresUserId = true;
                return;
            }
        }

        context.Configuration.AnyMethodRequiresUserId = false;
    }

    /// <summary>
    /// 检测接口是否继承了 ICurrentUserId 接口
    /// </summary>
    private void DetectICurrentUserId(GeneratorContext context)
    {
        context.ImplementsICurrentUserId = _interfaceSymbol.AllInterfaces
            .Any(i => i.Name == "ICurrentUserId");
    }

    /// <summary>
    /// 预计算接口方法中使用的 XML 类型（包括请求体和响应体），
    /// 以便 ConstructorGenerator 能在生成字段时正确生成 XmlSerializer 静态缓存字段。
    /// </summary>
    /// <remarks>
    /// Phase 5 修复：原实现仅收集 XML 响应类型，但 RequestBuilder.GenerateBodyParameter 也为
    /// XML 请求体生成 _xmlSerializer_{type} 字段引用。若请求体类型未被收集，ConstructorGenerator
    /// 不会生成对应静态字段，导致编译报 CS0103。
    /// </remarks>
    private void PrecomputeXmlResponseTypes(GeneratorContext context)
    {
        var methods = context.AllMethods;
        foreach (var method in methods)
        {
            // 与 MethodGenerator 口径一致：仅已知 HTTP 方法特性名（生成器由特性名推导动词）。
            var isHttpMethod = MethodAnalyzer.FindHttpMethodAttributeFromAttributes(method.GetAttributes()) != null;
            if (!isHttpMethod)
                continue;

            var methodInfo = context.GetOrAnalyzeMethod(
                context.Compilation,
                method,
                context.InterfaceDeclaration,
                context.SemanticModel);

            if (!methodInfo.IsValid)
                continue;

            // 1. 收集 XML 响应类型（反序列化用）
            var isXmlResponse = ContentTypeHelper.IsXmlContentType(methodInfo.ResponseContentType);
            if (isXmlResponse)
            {
                var deserializeType = methodInfo.IsAsyncMethod ? methodInfo.AsyncInnerReturnType : methodInfo.ReturnType;
                if (!string.IsNullOrEmpty(deserializeType) && deserializeType != "void" && deserializeType != "System.Void")
                {
                    if (TypeSymbolHelper.IsResponseType(deserializeType))
                    {
                        var innerType = TypeSymbolHelper.ExtractResponseInnerType(deserializeType);
                        if (!string.IsNullOrEmpty(innerType) && innerType != "void" && innerType != "System.Void")
                        {
                            context.XmlResponseTypes.Add(innerType!);
                        }
                    }
                    else
                    {
                        context.XmlResponseTypes.Add(deserializeType);
                    }
                }
            }

            // 2. 收集 XML 请求体类型（序列化用）
            // RequestBuilder.GenerateBodyParameter 在 isXmlContentType 分支中引用 _xmlSerializer_{type} 字段，
            // 但仅在非加密路径下使用（加密路径走 EncryptContent，不使用静态字段）。
            var bodyParam = methodInfo.Parameters
                .FirstOrDefault(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.BodyAttribute));
            if (bodyParam != null && !methodInfo.BodyEnableEncrypt && !string.IsNullOrEmpty(bodyParam.Type))
            {
                var effectiveContentType = methodInfo.GetEffectiveContentType();
                var isXmlRequest = methodInfo.SerializationMethod == "Xml"
                    || ContentTypeHelper.IsXmlContentType(effectiveContentType);
                if (isXmlRequest)
                {
                    context.XmlResponseTypes.Add(bodyParam.Type);
                }
            }
        }

        context.HasXmlResponse = context.XmlResponseTypes.Count > 0;
    }

    // CFG-27：原 ReportCacheAttributeIgnoredProperties（HTTPCLIENT019）已移除 ——
    // 其唯一触发点 CacheAttribute.Priority 已被删除；UseSlidingExpiration 早已受支持。
    // [Cache] 当前已无「被生成器忽略」的属性，故诊断不再需要（ID HTTPCLIENT019 保留为未使用占位）。

    /// <summary>
    /// M2-#12：非幂等 HTTP 方法（POST/PATCH 等未在全局 RetryableHttpMethods 白名单中的方法）
    /// 声明了 [Retry] 但未显式设置 <c>AllowNonIdempotent = true</c> 时发出 Warning：
    /// 运行时重试将被静默跳过（保留超时与熔断）。
    /// </summary>
    private void ReportRetryNonIdempotentWithoutAllow(GeneratorContext context)
    {
        // 幂等方法白名单（与 RetryOptions.RetryableHttpMethods 默认值一致，不区分大小写）
        var idempotentMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GET", "HEAD", "OPTIONS", "PUT", "DELETE", "TRACE",
        };

        foreach (var method in context.AllMethods)
        {
            var retryAttr = method.GetAttributes()
                .FirstOrDefault(attr => HttpClientGeneratorConstants.RetryAttributeNames.Contains(attr.AttributeClass?.Name));

            if (retryAttr == null)
                continue;

            // 需要知道该方法的 HTTP 方法名：从 Http 特性推断
            var httpAttr = MethodAnalyzer.FindHttpMethodAttributeFromAttributes(method.GetAttributes());
            var httpMethodName = httpAttr?.AttributeClass?.Name;
            if (httpMethodName == null)
                continue;
            // 特性名如 "PostAttribute"/"Post" → "POST"（netstandard2.0 目标无 Range/Index，用 Substring）
            var httpMethod = httpMethodName.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase)
                ? httpMethodName.Substring(0, httpMethodName.Length - "Attribute".Length).ToUpperInvariant()
                : httpMethodName.ToUpperInvariant();

            // 幂等方法不提示；已显式 AllowNonIdempotent 不提示
            if (idempotentMethods.Contains(httpMethod))
                continue;
            if (retryAttr.NamedArguments.Any(na =>
                    na.Key == "AllowNonIdempotent" && na.Value.Value is true))
                continue;

            var location = (retryAttr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                ?? method.Locations.FirstOrDefault()
                ?? _interfaceDecl.GetLocation())!;

            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.RetryNonIdempotentWithoutAllow,
                location,
                _interfaceSymbol.Name,
                method.Name,
                httpMethod));
        }
    }

    /// <summary>
    /// CFG-07：方法级 <c>[Timeout(ms)]</c> 超过接口级 <c>[HttpClientApi(Timeout=秒)]</c> 声明的
    /// HttpClient 超时时发出 Warning —— <c>HttpClient.Timeout</c> 是硬上限，会使 Polly 方法级超时永不触发。
    /// 仅在「方法级显式声明 <c>[Timeout]</c>」且「接口级 <c>Timeout</c> 显式声明」时报告（避免默认值场景误报）。
    /// </summary>
    private void ReportMethodTimeoutConflicts(GeneratorContext context)
    {
        var httpClientApiAttr = AttributeDataHelper.GetAttributeDataFromSymbol(
            _interfaceSymbol, HttpClientGeneratorConstants.HttpClientApiAttributeNames);
        if (httpClientApiAttr == null)
            return;

        // 仅在接口级 Timeout 被显式赋值时报告：未显式设置时生成器回退默认 50s，不应据此误报。
        var interfaceTimeoutArg = httpClientApiAttr.NamedArguments
            .FirstOrDefault(na => na.Key == HttpClientGeneratorConstants.TimeoutProperty);
        if (interfaceTimeoutArg.Key == null || interfaceTimeoutArg.Value.Value is not int interfaceTimeoutSeconds)
            return;

        var interfaceTimeoutMs = (long)interfaceTimeoutSeconds * 1000;

        foreach (var method in context.AllMethods)
        {
            var timeoutAttr = method.GetAttributes()
                .FirstOrDefault(a => HttpClientGeneratorConstants.TimeoutAttributeNames.Contains(a.AttributeClass?.Name));
            if (timeoutAttr == null)
                continue;

            // CFG-28 / I-9：与 MethodAnalyzer 同口径（命名参数优先），
            // 否则 [Timeout(0, TimeoutMilliseconds = 5000)] 会读到位置值 0 而漏过本冲突检查。
            var methodTimeoutMs = AttributeDataHelper.GetIntValuePreferNamed(
                timeoutAttr, HttpClientGeneratorConstants.TimeoutMillisecondsProperty, 0) ?? -1;
            if (methodTimeoutMs <= 0 || methodTimeoutMs <= interfaceTimeoutMs)
                continue;

            var location = (timeoutAttr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                ?? method.Locations.FirstOrDefault()
                ?? _interfaceDecl.GetLocation())!;

            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.MethodTimeoutExceedsHttpClientTimeout,
                location,
                _interfaceSymbol.Name,
                method.Name,
                methodTimeoutMs,
                interfaceTimeoutSeconds));
        }
    }

    /// <summary>
    /// CFG-29 / CFG-32：校验方法级 <c>[CircuitBreaker]</c> / <c>[Timeout]</c> 的特性参数取值域。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为何必须在生成器侧</b>：Roslyn 从不实例化 Attribute，写在 Attribute <c>setter</c> 中的校验永不执行
    /// （见 <see cref="Diagnostics.CircuitBreakerAttributeValueOutOfRange"/> 备注）。本方法是这两类特性唯一可行的
    /// 编译期防线，把「运行期静默降级」前移为「构建失败」（v2 §3.1 不静默原则）。
    /// </para>
    /// <para>
    /// <b>取值口径</b>：一律经 <see cref="AttributeDataHelper.GetIntValuePreferNamed"/>（命名参数优先，不变量 I-9），
    /// 与 <c>MethodAnalyzer</c> 的分析口径逐位一致 —— 否则会出现「生成器按 5 校验、生成代码按 200 生效」的口径分裂。
    /// </para>
    /// <para>
    /// <b>位置</b>：以特性语法节点为诊断位置（IDE 可直接定位到 <c>[CircuitBreaker(...)]</c> 行）。
    /// </para>
    /// </remarks>
    private void ReportResilienceAttributeValueRangeViolations(GeneratorContext context)
    {
        foreach (var method in context.AllMethods)
        {
            var attributes = method.GetAttributes();

            ReportCircuitBreakerRangeViolation(method, attributes);
            ReportTimeoutRangeViolation(method, attributes);
        }
    }

    /// <summary>
    /// CFG-29：<c>[CircuitBreaker]</c> 四个值域条件（均为 Error，共用 <c>HTTPCLIENT026</c>）。
    /// </summary>
    private void ReportCircuitBreakerRangeViolation(IMethodSymbol method, ImmutableArray<AttributeData> attributes)
    {
        var cbAttr = attributes
            .FirstOrDefault(a => HttpClientGeneratorConstants.CircuitBreakerAttributeNames.Contains(a.AttributeClass?.Name));
        if (cbAttr == null)
            return;

        var failureThreshold = AttributeDataHelper.GetIntValuePreferNamed(
            cbAttr, HttpClientGeneratorConstants.CircuitBreakerFailureThresholdProperty, 0) ?? 5;
        var breakDurationSeconds = AttributeDataHelper.GetIntValuePreferNamed(
            cbAttr, HttpClientGeneratorConstants.CircuitBreakerBreakDurationSecondsProperty, -1) ?? 30;
        var samplingDurationSeconds = AttributeDataHelper.GetIntValuePreferNamed(
            cbAttr, HttpClientGeneratorConstants.CircuitBreakerSamplingDurationSecondsProperty, -1) ?? 0;
        var minimumThroughput = AttributeDataHelper.GetIntValuePreferNamed(
            cbAttr, HttpClientGeneratorConstants.CircuitBreakerMinimumThroughputProperty, -1) ?? 10;

        string? detail = null;

        if (failureThreshold < 1)
        {
            detail = $"FailureThreshold = {failureThreshold}，须 ≥ 1" +
                     (samplingDurationSeconds > 0
                         ? "（当前 SamplingDurationSeconds > 0，该值为采样窗口内的失败率百分比）"
                         : "（当前 SamplingDurationSeconds = 0，该值为连续失败次数上限；≤ 0 会使熔断策略构建失败）");
        }
        else if (samplingDurationSeconds > 0 && failureThreshold > 100)
        {
            detail = $"FailureThreshold = {failureThreshold}，超范围" +
                     "（SamplingDurationSeconds > 0 时该值表示失败率百分比，须 ≤ 100；运行时会被静默按 100% 处理）";
        }
        else if (samplingDurationSeconds > 0 && minimumThroughput < 2)
        {
            detail = $"MinimumThroughput = {minimumThroughput}，须 ≥ 2" +
                     "（Polly AdvancedCircuitBreakerAsync 下限要求）";
        }
        else if (breakDurationSeconds <= 0)
        {
            detail = $"BreakDurationSeconds = {breakDurationSeconds}，须 > 0";
        }

        if (detail == null)
            return;

        _context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CircuitBreakerAttributeValueOutOfRange,
            GetAttributeLocation(cbAttr, method),
            _interfaceSymbol.Name,
            method.Name,
            detail));
    }

    /// <summary>
    /// CFG-32：<c>[Timeout]</c> 有效值必须为正毫秒数（Error，<c>HTTPCLIENT027</c>）。
    /// 不误报「未声明 <c>[Timeout]</c>」（该情形表示 <c>MethodTimeoutEnabled = false</c>）。
    /// </summary>
    private void ReportTimeoutRangeViolation(IMethodSymbol method, ImmutableArray<AttributeData> attributes)
    {
        var timeoutAttr = attributes
            .FirstOrDefault(a => HttpClientGeneratorConstants.TimeoutAttributeNames.Contains(a.AttributeClass?.Name));
        if (timeoutAttr == null)
            return;

        var timeoutMilliseconds = AttributeDataHelper.GetIntValuePreferNamed(
            timeoutAttr, HttpClientGeneratorConstants.TimeoutMillisecondsProperty, 0) ?? 0;
        if (timeoutMilliseconds > 0)
            return;

        _context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.TimeoutAttributeNonPositive,
            GetAttributeLocation(timeoutAttr, method),
            _interfaceSymbol.Name,
            method.Name,
            $"{timeoutMilliseconds}ms"));
    }

    /// <summary>
    /// 取特性语法节点位置（优先），回退方法位置，再回退接口声明位置。
    /// </summary>
    private Location GetAttributeLocation(AttributeData attribute, IMethodSymbol method)
        => (attribute.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
            ?? method.Locations.FirstOrDefault()
            ?? _interfaceDecl.GetLocation())!;

    /// <summary>
    /// 从方法的 Token 特性中提取 RequiresUserId 值
    /// </summary>
    private static bool? GetMethodRequiresUserId(IMethodSymbol methodSymbol)
    {
        var tokenAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "TokenAttribute" || attr.AttributeClass?.Name == "Token");

        if (tokenAttr == null)
            return null;

        var namedArg = tokenAttr.NamedArguments
            .FirstOrDefault(na => na.Key.Equals("RequiresUserId", StringComparison.OrdinalIgnoreCase)).Value.Value;

        if (namedArg != null)
            return (bool)namedArg;

        return null;
    }

    /// <summary>
    /// FIX-03: 构建接口的嵌套类型路径后缀（如 "Outer+Inner+_"），用于 hintName 唯一化。
    /// 顶级接口返回空字符串；嵌套接口返回从外到内的类型名用 "+" 连接。
    /// </summary>
    /// <remarks>
    /// [GEN-17][§8.5] 旧实现用 "_" 连接，导致「A{class B_C{IFoo}}」与「A{class B{class C{IFoo}}}」
    /// 平铺后均得到 "B_C_" 后缀 → hintName 冲突（CS8785/产物覆盖）。
    /// 改用元数据名风格的分隔符 "+"（在文件路径中合法、且与用户类型名中的 "_" 可区分），消除平铺歧义：
    ///   B_C 嵌套链 → "B_C+_IFoo"；B→C 两层嵌套 → "B+C+_IFoo"。
    /// </remarks>
    private static string BuildNestingSuffix(INamedTypeSymbol interfaceSymbol)
    {
        if (interfaceSymbol.ContainingType is null)
            return string.Empty;

        var parts = new List<string>();
        var current = interfaceSymbol.ContainingType;
        while (current is not null)
        {
            // 嵌套类型的 Name 也不含元数，但嵌套接口本身已有 Arity 后缀，
            // 包含类型链只需用名称（不含元数），因为同一嵌套链中不会出现同名不同元数的包含类型。
            parts.Insert(0, current.Name);
            current = current.ContainingType;
        }

        return string.Join("+", parts) + "_";
    }

}
