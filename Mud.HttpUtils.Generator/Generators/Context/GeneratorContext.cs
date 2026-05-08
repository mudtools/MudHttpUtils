// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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

    public List<InterfacePropertyInfo> InterfaceProperties { get; set; } = [];

    /// <summary>
    /// 接口是否有继承其他接口
    /// </summary>
    public bool HasBaseInterfaces => InterfaceSymbol.Interfaces.Length > 0;

    /// <summary>
    /// 根据 InheritedFrom 属性值获取 GetTokenAsync 方法的访问修饰符
    /// - 有值（继承自指定类）：public override
    /// - 无值：public virtual
    /// </summary>
    public string GetTokenAsyncAccessibility => HasInheritedFrom
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

        HasCache = DetectCacheUsage(interfaceSymbol);
        HasCacheVaryByUser = DetectCacheVaryByUser(interfaceSymbol);
        HasResilience = DetectResilienceUsage(interfaceSymbol);
        HasApiKeyInjection = DetectApiKeyInjection(interfaceSymbol);
        HasHmacSignatureInjection = DetectHmacSignatureInjection(interfaceSymbol);
    }

    private static bool DetectCacheUsage(INamedTypeSymbol interfaceSymbol)
    {
        try
        {
            var allMethods = TypeSymbolHelper.GetAllMethods(interfaceSymbol, true);
            return allMethods.Any(method =>
                method.GetAttributes().Any(attr =>
                    HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name)));
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectCacheVaryByUser(INamedTypeSymbol interfaceSymbol)
    {
        try
        {
            var allMethods = TypeSymbolHelper.GetAllMethods(interfaceSymbol, true);
            return allMethods.Any(method =>
            {
                var cacheAttr = method.GetAttributes()
                    .FirstOrDefault(attr => HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name));
                if (cacheAttr == null)
                    return false;
                return AttributeDataHelper.GetBoolValueFromAttribute(cacheAttr, HttpClientGeneratorConstants.CacheVaryByUserProperty, false);
            });
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectResilienceUsage(INamedTypeSymbol interfaceSymbol)
    {
        try
        {
            var allMethods = TypeSymbolHelper.GetAllMethods(interfaceSymbol, true);
            return allMethods.Any(method =>
                method.GetAttributes().Any(attr =>
                    HttpClientGeneratorConstants.RetryAttributeNames.Contains(attr.AttributeClass?.Name) ||
                    HttpClientGeneratorConstants.CircuitBreakerAttributeNames.Contains(attr.AttributeClass?.Name) ||
                    HttpClientGeneratorConstants.TimeoutAttributeNames.Contains(attr.AttributeClass?.Name)));
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectApiKeyInjection(INamedTypeSymbol interfaceSymbol)
    {
        try
        {
            // 检查接口级 Token 特性
            var interfaceTokenAttr = interfaceSymbol.GetAttributes()
                .FirstOrDefault(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.AttributeClass?.Name));
            if (interfaceTokenAttr != null && IsInjectionMode(interfaceTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeApiKey))
                return true;

            // 检查方法级 Token 特性
            var allMethods = TypeSymbolHelper.GetAllMethods(interfaceSymbol, true);
            return allMethods.Any(method =>
            {
                var methodTokenAttr = method.GetAttributes()
                    .FirstOrDefault(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.AttributeClass?.Name));
                return methodTokenAttr != null && IsInjectionMode(methodTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeApiKey);
            });
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectHmacSignatureInjection(INamedTypeSymbol interfaceSymbol)
    {
        try
        {
            // 检查接口级 Token 特性
            var interfaceTokenAttr = interfaceSymbol.GetAttributes()
                .FirstOrDefault(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.AttributeClass?.Name));
            if (interfaceTokenAttr != null && IsInjectionMode(interfaceTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeHmacSignature))
                return true;

            // 检查方法级 Token 特性
            var allMethods = TypeSymbolHelper.GetAllMethods(interfaceSymbol, true);
            return allMethods.Any(method =>
            {
                var methodTokenAttr = method.GetAttributes()
                    .FirstOrDefault(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.AttributeClass?.Name));
                return methodTokenAttr != null && IsInjectionMode(methodTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeHmacSignature);
            });
        }
        catch
        {
            return false;
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
    /// 从 TypedConstant 获取 TokenInjectionMode 枚举名称
    /// </summary>
    private static string GetTokenInjectionModeName(object? value)
    {
        if (value == null)
            return HttpClientGeneratorConstants.TokenInjectionModeHeader;

        var str = value.ToString();
        if (string.IsNullOrEmpty(str))
            return HttpClientGeneratorConstants.TokenInjectionModeHeader;

        if (int.TryParse(str, out var num))
        {
            return num switch
            {
                3 => HttpClientGeneratorConstants.TokenInjectionModeApiKey,
                4 => HttpClientGeneratorConstants.TokenInjectionModeHmacSignature,
                _ => HttpClientGeneratorConstants.TokenInjectionModeHeader
            };
        }

        var lastDot = str.LastIndexOf('.');
        var name = lastDot >= 0 ? str.Substring(lastDot + 1) : str;

        return name switch
        {
            "ApiKey" => HttpClientGeneratorConstants.TokenInjectionModeApiKey,
            "HmacSignature" => HttpClientGeneratorConstants.TokenInjectionModeHmacSignature,
            _ => HttpClientGeneratorConstants.TokenInjectionModeHeader
        };
    }
}
