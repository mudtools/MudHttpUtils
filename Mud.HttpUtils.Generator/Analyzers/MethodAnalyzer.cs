// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// 方法分析器，负责分析接口方法的特性和元数据
/// </summary>
internal static class MethodAnalyzer
{
    /// <summary>
    /// 分析函数符号，并返回 MethodAnalysisResult 分析结果
    /// </summary>
    public static MethodAnalysisResult AnalyzeMethod(
        Compilation compilation,
        IMethodSymbol methodSymbol,
        InterfaceDeclarationSyntax interfaceDecl,
        SemanticModel? semanticModel = null)
    {
        ArgumentNullExceptionExtensions.ThrowIfNull(compilation);
        ArgumentNullExceptionExtensions.ThrowIfNull(methodSymbol);
        ArgumentNullExceptionExtensions.ThrowIfNull(interfaceDecl);

        var methodSyntax = FindMethodSyntax(compilation, methodSymbol, interfaceDecl, semanticModel);

        var httpMethodAttributeData = FindHttpMethodAttributeFromSymbol(methodSymbol);
        if (httpMethodAttributeData == null)
            return MethodAnalysisResult.Invalid;

        if (httpMethodAttributeData.AttributeClass == null)
            return MethodAnalysisResult.Invalid;

        var httpMethodAttributeName = httpMethodAttributeData.AttributeClass.Name;
        var httpMethod = ExtractHttpMethodName(httpMethodAttributeName);
        var urlTemplate = GetAttributeArgumentValueFromAttributeData(httpMethodAttributeData, 0)?.ToString().Trim('"') ?? "";

        if (string.IsNullOrEmpty(httpMethod) || string.IsNullOrEmpty(urlTemplate))
            return MethodAnalysisResult.Invalid;

        var methodContentType = GetMethodContentTypeFromHttpMethodAttribute(methodSymbol);
        var responseContentType = GetResponseContentTypeFromSymbol(methodSymbol);
        var responseEnableDecrypt = GetResponseEnableDecryptFromSymbol(methodSymbol);
        var parameters = ParameterAnalyzer.AnalyzeParameters(methodSymbol);
        var (bodyContentType, bodyEnableEncrypt, bodyEncryptSerializeType, bodyEncryptPropertyName) = GetBodyInfoFromParameters(parameters);
        var (interfaceAttributes, interfaceHeaderAttributes, interfaceTokenInjectionMode, interfaceTokenName, interfaceTokenScopes) = AnalyzeInterfaceAttributes(
            compilation,
            interfaceDecl,
            semanticModel);

        var (cacheEnabled, cacheDurationSeconds, cacheKeyTemplate, cacheVaryByUser) = AnalyzeCacheAttribute(methodSymbol);

        var methodTokenScopes = AnalyzeMethodTokenScopes(methodSymbol);

        var (methodTokenManagerKey, methodRequiresUserId) = AnalyzeMethodTokenExtended(methodSymbol);

        var tokenParameterName = parameters
            .FirstOrDefault(p => p.Attributes.Any(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.Name)))?
            .Name;

        var allowAnyStatusCode = AnalyzeAllowAnyStatusCode(methodSymbol, interfaceDecl, compilation, semanticModel);
        var (interfaceQueryParams, interfacePathParams) = AnalyzeInterfaceQueryPathAttributes(interfaceDecl, compilation, semanticModel);
        var interfaceProperties = AnalyzeInterfaceProperties(interfaceDecl, compilation, semanticModel);
        var headerMergeMode = AnalyzeHeaderMergeMode(methodSymbol, interfaceDecl, compilation, semanticModel);
        var serializationMethod = AnalyzeSerializationMethod(methodSymbol, interfaceDecl, compilation, semanticModel);

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
            IgnoreGenerator = HasMethodAttribute(methodSymbol, HttpClientGeneratorConstants.IgnoreGeneratorAttributeNames),
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
            CacheVaryByUser = cacheVaryByUser
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

        return methodSymbol.GetAttributes()
            .FirstOrDefault(attr => HttpClientGeneratorConstants.SupportedHttpMethods.Contains(attr.AttributeClass?.Name));
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

        return methodSymbol.GetAttributes()
            .Any(attr => attributeNames.Contains(attr.AttributeClass?.Name));
    }

    /// <summary>
    /// 从HTTP方法特性获取ContentType值（请求内容类型）
    /// </summary>
    public static string? GetMethodContentTypeFromHttpMethodAttribute(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null)
            return null;

        var httpMethodAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => HttpClientGeneratorConstants.SupportedHttpMethods.Contains(attr.AttributeClass?.Name));

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
    /// 从TypedConstant获取TokenInjectionMode枚举名称
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
                0 => HttpClientGeneratorConstants.TokenInjectionModeHeader,
                1 => HttpClientGeneratorConstants.TokenInjectionModeQuery,
                2 => HttpClientGeneratorConstants.TokenInjectionModePath,
                3 => HttpClientGeneratorConstants.TokenInjectionModeApiKey,
                4 => HttpClientGeneratorConstants.TokenInjectionModeHmacSignature,
                5 => HttpClientGeneratorConstants.TokenInjectionModeBasicAuth,
                6 => HttpClientGeneratorConstants.TokenInjectionModeCookie,
                _ => HttpClientGeneratorConstants.TokenInjectionModeHeader
            };
        }

        var lastDot = str.LastIndexOf('.');
        var name = lastDot >= 0 ? str.Substring(lastDot + 1) : str;

        return name switch
        {
            "Header" => HttpClientGeneratorConstants.TokenInjectionModeHeader,
            "Query" => HttpClientGeneratorConstants.TokenInjectionModeQuery,
            "Path" => HttpClientGeneratorConstants.TokenInjectionModePath,
            "ApiKey" => HttpClientGeneratorConstants.TokenInjectionModeApiKey,
            "HmacSignature" => HttpClientGeneratorConstants.TokenInjectionModeHmacSignature,
            "BasicAuth" => HttpClientGeneratorConstants.TokenInjectionModeBasicAuth,
            "Cookie" => HttpClientGeneratorConstants.TokenInjectionModeCookie,
            _ => HttpClientGeneratorConstants.TokenInjectionModeHeader
        };
    }

    /// <summary>
    /// 从方法符号获取HTTP方法特性的ResponseContentType值
    /// </summary>
    private static string? GetResponseContentTypeFromSymbol(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null)
            return null;

        var httpMethodAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => HttpClientGeneratorConstants.SupportedHttpMethods.Contains(attr.AttributeClass?.Name));

        if (httpMethodAttr == null)
            return null;

        return AttributeDataHelper.GetStringValueFromAttribute(httpMethodAttr, [HttpClientGeneratorConstants.HttpMethodResponseContentTypeProperty]);
    }

    /// <summary>
    /// 从方法符号获取HTTP方法特性的ResponseEnableDecrypt值
    /// </summary>
    private static bool GetResponseEnableDecryptFromSymbol(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null)
            return false;

        var httpMethodAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => HttpClientGeneratorConstants.SupportedHttpMethods.Contains(attr.AttributeClass?.Name));

        if (httpMethodAttr == null)
            return false;

        var value = AttributeDataHelper.GetBoolValueFromAttribute(
            httpMethodAttr,
            HttpClientGeneratorConstants.HttpMethodResponseEnableDecryptProperty);

        return value;
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

        foreach (var interfaceSyntax in allInterfaces)
        {
            var method = interfaceSyntax.Members
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m =>
                {
                    try
                    {
                        var model = SemanticModelCache.GetOrCreate(compilation, m.SyntaxTree);
                        var methodSymbolFromSyntax = model.GetDeclaredSymbol(m);
                        var targetSymbol = methodSymbolFromSyntax?.OriginalDefinition ?? methodSymbolFromSyntax;
                        var sourceSymbol = methodSymbol.OriginalDefinition ?? methodSymbol;
                        if (targetSymbol?.Equals(sourceSymbol, SymbolEqualityComparer.Default) == true)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }

                    return m.Identifier.Text == methodSymbol.Name &&
                           m.ParameterList.Parameters.Count == methodSymbol.Parameters.Length;
                });

            if (method != null)
                return method;
        }

        return null;
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

    private static readonly ConditionalWeakTable<Compilation, Dictionary<INamedTypeSymbol, InterfaceDeclarationSyntax?>> _interfaceSyntaxCache = new();

    /// <summary>
    /// 获取接口声明语法节点
    /// </summary>
    private static InterfaceDeclarationSyntax? GetInterfaceDeclarationSyntax(
        Compilation compilation,
        INamedTypeSymbol interfaceSymbol)
    {
        var innerDict = _interfaceSyntaxCache.GetOrCreateValue(compilation);
        if (innerDict.TryGetValue(interfaceSymbol, out var cached))
            return cached;

        var result = FindInterfaceDeclarationSyntax(compilation, interfaceSymbol);
        innerDict[interfaceSymbol] = result;
        return result;
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

        var interfaceName = interfaceSymbol.Name;
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var root = syntaxTree.GetRoot();
            var interfaceDeclarations = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>();
            foreach (var interfaceDecl in interfaceDeclarations)
            {
                if (interfaceDecl.Identifier.Text == interfaceName)
                {
                    var model = SemanticModelCache.GetOrCreate(compilation, syntaxTree);
                    var symbol = model.GetDeclaredSymbol(interfaceDecl);
                    if (symbol?.Equals(interfaceSymbol, SymbolEqualityComparer.Default) == true)
                    {
                        return interfaceDecl;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 分析接口的 [InterfaceQuery] 和 [InterfacePath] 特性
    /// </summary>
    private static (List<InterfaceQueryParameterInfo> queryParams, List<InterfacePathParameterInfo> pathParams)
        AnalyzeInterfaceQueryPathAttributes(InterfaceDeclarationSyntax interfaceDecl, Compilation compilation, SemanticModel? semanticModel)
    {
        var queryParams = new List<InterfaceQueryParameterInfo>();
        var pathParams = new List<InterfacePathParameterInfo>();

        var model = semanticModel ?? SemanticModelCache.GetOrCreate(compilation, interfaceDecl.SyntaxTree);
        var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;

        if (interfaceSymbol == null)
            return (queryParams, pathParams);

        foreach (var attr in interfaceSymbol.GetAttributes())
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

        var propertyDecls = interfaceDecl.Members.OfType<PropertyDeclarationSyntax>()
            .ToDictionary(p => p.Identifier.Text, p => p);

        foreach (var property in interfaceSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var queryAttr = property.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "QueryAttribute");

            var pathAttr = property.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == "PathAttribute");

            PropertyDeclarationSyntax? propertyDecl = null;
            propertyDecls.TryGetValue(property.Name, out propertyDecl);

            if (queryAttr != null)
            {
                properties.Add(CreatePropertyInfo(property, queryAttr, "Query", propertyDecl, model));
            }
            else if (pathAttr != null)
            {
                properties.Add(CreatePropertyInfo(property, pathAttr, "Path", propertyDecl, model));
            }
        }

        return properties;
    }

    private static InterfacePropertyInfo CreatePropertyInfo(IPropertySymbol property, AttributeData attribute, string attributeType, PropertyDeclarationSyntax? propertyDecl, SemanticModel model)
    {
        var propertyInfo = new InterfacePropertyInfo
        {
            Name = property.Name,
            Type = TypeSymbolHelper.GetTypeFullName(property.Type),
            AttributeType = attributeType
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
            }
        }

        if (string.IsNullOrEmpty(propertyInfo.ParameterName))
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
    private static string AnalyzeHeaderMergeMode(IMethodSymbol methodSymbol, InterfaceDeclarationSyntax interfaceDecl, Compilation compilation, SemanticModel? semanticModel)
    {
        var methodAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => HttpClientGeneratorConstants.HeaderMergeAttributeNames.Contains(attr.AttributeClass?.Name));

        if (methodAttr != null)
        {
            var mode = methodAttr.ConstructorArguments.Length > 0
                ? methodAttr.ConstructorArguments[0].Value?.ToString()
                : null;
            if (!string.IsNullOrEmpty(mode))
                return mode;
        }

        var model = semanticModel ?? SemanticModelCache.GetOrCreate(compilation, interfaceDecl.SyntaxTree);
        var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;

        if (interfaceSymbol != null)
        {
            var interfaceAttr = interfaceSymbol.GetAttributes()
                .FirstOrDefault(attr => HttpClientGeneratorConstants.HeaderMergeAttributeNames.Contains(attr.AttributeClass?.Name));

            if (interfaceAttr != null)
            {
                var mode = interfaceAttr.ConstructorArguments.Length > 0
                    ? interfaceAttr.ConstructorArguments[0].Value?.ToString()
                    : null;
                if (!string.IsNullOrEmpty(mode))
                    return mode;
            }
        }

        return "Append";
    }

    /// <summary>
    /// 分析序列化方法（方法级优先于接口级）
    /// </summary>
    private static string AnalyzeSerializationMethod(IMethodSymbol methodSymbol, InterfaceDeclarationSyntax interfaceDecl, Compilation compilation, SemanticModel? semanticModel)
    {
        var methodAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => HttpClientGeneratorConstants.SerializationMethodAttributeNames.Contains(attr.AttributeClass?.Name));

        if (methodAttr != null)
        {
            var method = methodAttr.ConstructorArguments.Length > 0
                ? methodAttr.ConstructorArguments[0].Value?.ToString()
                : null;
            if (!string.IsNullOrEmpty(method))
                return method;
        }

        var model = semanticModel ?? SemanticModelCache.GetOrCreate(compilation, interfaceDecl.SyntaxTree);
        var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;

        if (interfaceSymbol != null)
        {
            var interfaceAttr = interfaceSymbol.GetAttributes()
                .FirstOrDefault(attr => HttpClientGeneratorConstants.SerializationMethodAttributeNames.Contains(attr.AttributeClass?.Name));

            if (interfaceAttr != null)
            {
                var method = interfaceAttr.ConstructorArguments.Length > 0
                    ? interfaceAttr.ConstructorArguments[0].Value?.ToString()
                    : null;
                if (!string.IsNullOrEmpty(method))
                    return method;
            }
        }

        return "Json";
    }

    /// <summary>
    /// 分析方法是否标记了 [AllowAnyStatusCode] 特性。
    /// 方法级特性优先于接口级特性。
    /// </summary>
    private static bool AnalyzeAllowAnyStatusCode(
        IMethodSymbol methodSymbol,
        InterfaceDeclarationSyntax interfaceDecl,
        Compilation compilation,
        SemanticModel? semanticModel)
    {
        var methodHasAttr = methodSymbol.GetAttributes()
            .Any(attr => HttpClientGeneratorConstants.AllowAnyStatusCodeAttributeNames.Contains(attr.AttributeClass?.Name));

        if (methodHasAttr)
            return true;

        var model = semanticModel ?? SemanticModelCache.GetOrCreate(compilation, interfaceDecl.SyntaxTree);
        var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;

        if (interfaceSymbol == null)
            return false;

        return interfaceSymbol.GetAttributes()
            .Any(attr => HttpClientGeneratorConstants.AllowAnyStatusCodeAttributeNames.Contains(attr.AttributeClass?.Name));
    }

    /// <summary>
    /// 分析接口特性
    /// </summary>
    private static (HashSet<string> interfaceAttributes, List<InterfaceHeaderAttributeInfo> interfaceHeaderAttributes, string? interfaceTokenInjectionMode, string? interfaceTokenName, string? interfaceTokenScopes)
        AnalyzeInterfaceAttributes(Compilation compilation, InterfaceDeclarationSyntax interfaceDecl, SemanticModel? semanticModel)
    {
        var model = semanticModel ?? SemanticModelCache.GetOrCreate(compilation, interfaceDecl.SyntaxTree);
        var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;
        var interfaceAttributes = new HashSet<string>();
        var interfaceHeaderAttributes = new List<InterfaceHeaderAttributeInfo>();
        string? interfaceTokenInjectionMode = null;
        string? interfaceTokenName = null;
        string? interfaceTokenScopes = null;

        if (interfaceSymbol != null)
        {
            var headerAttributes = interfaceSymbol.GetAttributes()
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

                var isAuthorizationHeader = AttributeDataHelper.GetStringValueFromAttribute(headerAttr, ["Name"], 0) == "Authorization";
                if (isAuthorizationHeader)
                {
                    interfaceAttributes.Add($"Header:{headerName}");
                }
            }

            var queryAttributes = interfaceSymbol.GetAttributes()
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
            var tokenAttributes = interfaceSymbol.GetAttributes()
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
    /// 分析方法级别的 Token Scopes
    /// </summary>
    private static string? AnalyzeMethodTokenScopes(IMethodSymbol methodSymbol)
    {
        var tokenAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => HasAttributeWithName(attr, "TokenAttribute"));

        return tokenAttr != null ? GetTokenScopes(tokenAttr) : null;
    }

    /// <summary>
    /// 分析方法级别 Token 特性的 TokenManagerKey 和 RequiresUserId
    /// </summary>
    private static (string? tokenManagerKey, bool? requiresUserId) AnalyzeMethodTokenExtended(IMethodSymbol methodSymbol)
    {
        var tokenAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => HasAttributeWithName(attr, "TokenAttribute"));

        if (tokenAttr == null)
            return (null, null);

        var tokenManagerKey = TokenHelper.GetTokenManagerKeyFromAttribute(tokenAttr);
        var requiresUserIdValue = tokenAttr.NamedArguments
            .FirstOrDefault(na => na.Key.Equals("RequiresUserId", StringComparison.OrdinalIgnoreCase)).Value.Value;

        bool? requiresUserId = requiresUserIdValue != null ? (bool)requiresUserIdValue : null;

        return (tokenManagerKey, requiresUserId);
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

    private static (bool enabled, int durationSeconds, string? keyTemplate, bool varyByUser) AnalyzeCacheAttribute(IMethodSymbol methodSymbol)
    {
        var cacheAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name));

        if (cacheAttr == null)
            return (false, 300, null, false);

        var durationSeconds = AttributeDataHelper.GetIntValueFromAttribute(
            cacheAttr, HttpClientGeneratorConstants.CacheDurationSecondsProperty, 300);

        if (cacheAttr.ConstructorArguments.Length > 0 &&
            cacheAttr.ConstructorArguments[0].Value is int constructorDuration)
        {
            durationSeconds = constructorDuration;
        }

        var keyTemplate = AttributeDataHelper.GetStringValueFromAttribute(
            cacheAttr, [HttpClientGeneratorConstants.CacheKeyTemplateProperty]);

        var varyByUser = AttributeDataHelper.GetBoolValueFromAttribute(
            cacheAttr, HttpClientGeneratorConstants.CacheVaryByUserProperty);

        return (true, durationSeconds, keyTemplate, varyByUser);
    }
}
