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
    private readonly StringBuilder _codeBuilder;
    private readonly string _optionsName;
    private readonly bool _isAotEnabled;
    private readonly bool _emitNullableEnable;
    private readonly bool _emitGeneratedCodeMarkers;

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

        var estimatedCapacity = EstimateCodeCapacity();
        _codeBuilder = new StringBuilder(estimatedCapacity);
    }

    /// <summary>
    /// 生成代码入口
    /// </summary>
    public void GenerateCode()
    {
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

        // InterfaceProperties 已在 GeneratorContext 构造函数中预计算（含基接口 [Query]/[Path] 属性），
        // 后续 GetOrAnalyzeMethod 会将其作为 cachedInterfaceProperties 传入 AnalyzeMethod，避免重复扫描。

        ComputeAnyMethodRequiresUserId(generatorContext);

        DetectICurrentUserId(generatorContext);

        PrecomputeXmlResponseTypes(generatorContext);

        // NEW-GEN-03/08 修复：检测方法 CacheAttribute 中被生成器忽略的属性并发出诊断
        ReportCacheAttributeIgnoredProperties(generatorContext);

        var generators = InitializeGenerators(generatorContext);

        foreach (var generator in generators)
        {
            generator.Generate(_codeBuilder, generatorContext);
        }

        if (generatorContext.HasQueryMap)
        {
            _codeBuilder.AppendLine();
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
        }

        _codeBuilder.AppendLine("    }");
        _codeBuilder.AppendLine("}");
        _codeBuilder.AppendLine();

        // 使用命名空间文件夹 + 类名的方式（如 MyApp/Apis/Implementation/UserServiceImpl.g.cs），
        // 比扁平命名（UserServiceImpl.g.cs）更清晰地反映代码结构，并避免跨命名空间同名接口冲突
        var namespacePath = generatorContext.NamespaceName.Replace('.', '/');
        var fileName = string.IsNullOrEmpty(namespacePath)
            ? $"{generatorContext.ClassName}.g.cs"
            : $"{namespacePath}/{generatorContext.ClassName}.g.cs";
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
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientAndTokenManagerMutuallyExclusive,
                _interfaceDecl.GetLocation(),
                _interfaceSymbol.Name));
            isValid = false;
        }

        if (!string.IsNullOrEmpty(configuration.HttpClient))
        {
            // HttpClient 类型校验仅输出诊断不阻断生成：GetTypeByMetadataName 需要完全限定名，
            // 用户可能使用短名称引用同编译中的类型，阻断会导致误报。错误诊断已足以提示用户。
            ValidateHttpClientType(configuration.HttpClient);
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
                configuration.InheritedFrom,
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
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientTypeNotFound,
                _interfaceDecl.GetLocation(),
                _interfaceSymbol.Name,
                httpClientType));
        }
    }

    private bool ValidateTokenManagerType(GenerationConfiguration configuration)
    {
        if (string.IsNullOrEmpty(configuration.TokenManager))
            return true;

        var tokenManagerTypeName = configuration.TokenManager;
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

        return generators;
    }

    /// <summary>
    /// 估算生成的代码容量
    /// </summary>
    private int EstimateCodeCapacity()
    {
        int methodCount = 0;
        try
        {
            var methods = TypeSymbolHelper.GetAllMethods(_interfaceSymbol, true);
            foreach (var method in methods)
            {
                methodCount++;
            }
        }
        catch
        {
            methodCount = 10;
        }

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
        var interfaceScopes = GetInterfaceTokenScopes();
        var interfaceTokenName = GetInterfaceTokenName();

        var basePath = ExtractBasePath();

        return new GenerationConfiguration
        {
            HttpClientOptionsName = _optionsName,
            DefaultContentType = GetHttpClientApiContentTypeFromAttribute(httpClientApiAttribute),
            Timeout = AttributeDataHelper.GetIntValueFromAttribute(
                httpClientApiAttribute,
                HttpClientGeneratorConstants.TimeoutProperty,
                100),
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
            InheritedFromInterfaceName = inheritedFromInterfaceName,
            TokenType = tokenType,
            IsUserAccessToken = tokenType == "UserAccessToken",
            TokenManagerKey = tokenManagerKey,
            RequiresUserId = requiresUserId,
            InterfaceScopes = interfaceScopes,
            InterfaceTokenName = interfaceTokenName,
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
        return string.IsNullOrEmpty(contentType) ? HttpClientGeneratorConstants.DefaultContentType : contentType;
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
    /// 从接口的 Token 特性中提取 Scopes 值
    /// </summary>
    private string? GetInterfaceTokenScopes()
    {
        var tokenAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(
            _interfaceSymbol,
            HttpClientGeneratorConstants.TokenAttributeNames);
        return TokenHelper.GetScopesFromAttribute(tokenAttribute);
    }

    /// <summary>
    /// 从接口的 Token 特性中提取 Name 值
    /// </summary>
    private string? GetInterfaceTokenName()
    {
        var tokenAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(
            _interfaceSymbol,
            HttpClientGeneratorConstants.TokenAttributeNames);
        if (tokenAttribute == null)
            return null;
        return AttributeDataHelper.GetStringValueFromAttribute(tokenAttribute, ["Name"]);
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
            var isHttpMethod = MethodAnalyzer.FindHttpMethodAttributeFromSymbol(method) != null;
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
                            context.XmlResponseTypes.Add(innerType);
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

    /// <summary>
    /// NEW-GEN-03/08 修复：检测方法 CacheAttribute 中被生成器忽略的属性（UseSlidingExpiration、Priority），
    /// 当用户显式设置这些属性时发出信息性诊断，提示这些配置不会在生成的代码中生效。
    /// </summary>
    private void ReportCacheAttributeIgnoredProperties(GeneratorContext context)
    {
        foreach (var method in context.AllMethods)
        {
            var cacheAttr = method.GetAttributes()
                .FirstOrDefault(attr => HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name));

            if (cacheAttr == null)
                continue;

            // 获取特性在源代码中的位置，回退到方法声明位置或接口声明位置
            var location = (cacheAttr.ApplicationSyntaxReference?.GetSyntax()?.GetLocation()
                ?? method.Locations.FirstOrDefault()
                ?? _interfaceDecl.GetLocation())!;

            // 检查 UseSlidingExpiration：仅当显式设置为 true 时发出诊断（设置为 false 等同于默认值）
            var slidingArg = cacheAttr.NamedArguments
                .FirstOrDefault(na => na.Key == "UseSlidingExpiration");
            if (slidingArg.Value.Value is bool useSliding && useSliding)
            {
                _context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.CacheAttributePropertyIgnored,
                    location,
                    _interfaceSymbol.Name,
                    method.Name,
                    "UseSlidingExpiration"));
            }

            // 检查 Priority：只要显式设置（无论值为何）即发出诊断，因为该属性被生成器完全忽略
            if (cacheAttr.NamedArguments.Any(na => na.Key == "Priority"))
            {
                _context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.CacheAttributePropertyIgnored,
                    location,
                    _interfaceSymbol.Name,
                    method.Name,
                    "Priority"));
            }
        }
    }

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

}
