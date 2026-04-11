// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Models;

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
        AttributeData? httpMethodAttributeData = null;

        if (methodSyntax != null)
        {
            var httpMethodAttr = FindHttpMethodAttribute(methodSyntax);
            if (httpMethodAttr != null)
            {
                var urlTemplateFromSyntax = GetAttributeArgumentValue(httpMethodAttr, 0)?.ToString().Trim('"') ?? "";
                if (!string.IsNullOrEmpty(urlTemplateFromSyntax))
                {
                    httpMethodAttributeData = FindHttpMethodAttributeFromSymbol(methodSymbol);
                    if (httpMethodAttributeData == null)
                        return MethodAnalysisResult.Invalid;
                }
            }
        }

        if (httpMethodAttributeData == null)
        {
            httpMethodAttributeData = FindHttpMethodAttributeFromSymbol(methodSymbol);
            if (httpMethodAttributeData == null)
                return MethodAnalysisResult.Invalid;
        }

        var httpMethodAttributeName = httpMethodAttributeData.AttributeClass?.Name ?? "";
        var httpMethod = ExtractHttpMethodName(httpMethodAttributeName);
        var urlTemplate = GetAttributeArgumentValueFromAttributeData(httpMethodAttributeData, 0)?.ToString().Trim('"') ?? "";

        if (string.IsNullOrEmpty(httpMethod) || string.IsNullOrEmpty(urlTemplate))
            return MethodAnalysisResult.Invalid;

        var methodContentType = GetMethodContentTypeFromHttpMethodAttribute(methodSymbol);
        var responseContentType = GetResponseContentTypeFromSymbol(methodSymbol);
        var responseEnableDecrypt = GetResponseEnableDecryptFromSymbol(methodSymbol);
        var parameters = ParameterAnalyzer.AnalyzeParameters(methodSymbol);
        var (bodyContentType, bodyEnableEncrypt, bodyEncryptSerializeType, bodyEncryptPropertyName) = GetBodyInfoFromParameters(parameters);
        var (interfaceAttributes, interfaceHeaderAttributes, interfaceTokenInjectionMode, interfaceTokenName) = AnalyzeInterfaceAttributes(
            compilation,
            interfaceDecl,
            semanticModel);

        return new MethodAnalysisResult
        {
            MethodOwnerInterfaceName = methodSymbol.ContainingType?.Name ?? interfaceDecl.Identifier.Text,
            CurrentInterfaceName = interfaceDecl.Identifier.Text,
            IsValid = true,
            MethodName = methodSymbol.Name,
            HttpMethod = httpMethod,
            UrlTemplate = urlTemplate,
            ReturnType = TypeSymbolHelper.GetTypeFullName(methodSymbol.ReturnType),
            AsyncInnerReturnType = TypeSymbolHelper.ExtractAsyncInnerType(methodSymbol.ReturnType),
            IsAsyncMethod = TypeSymbolHelper.IsAsyncType(methodSymbol.ReturnType),
            Parameters = parameters,
            IgnoreImplement = HasMethodAttribute(methodSymbol, HttpClientGeneratorConstants.IgnoreImplementAttributeNames),
            IgnoreWrapInterface = HasMethodAttribute(methodSymbol, HttpClientGeneratorConstants.IgnoreWrapInterfaceAttributeNames),
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
            InterfaceTokenName = interfaceTokenName
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

        return AttributeDataHelper.GetBoolValueFromAttribute(httpMethodAttr, HttpClientGeneratorConstants.HttpMethodResponseEnableDecryptProperty, false);
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
    /// 分析接口特性
    /// </summary>
    private static (HashSet<string> interfaceAttributes, List<InterfaceHeaderAttributeInfo> interfaceHeaderAttributes, string? interfaceTokenInjectionMode, string? interfaceTokenName)
        AnalyzeInterfaceAttributes(Compilation compilation, InterfaceDeclarationSyntax interfaceDecl, SemanticModel? semanticModel)
    {
        var model = semanticModel ?? SemanticModelCache.GetOrCreate(compilation, interfaceDecl.SyntaxTree);
        var interfaceSymbol = model.GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;
        var interfaceAttributes = new HashSet<string>();
        var interfaceHeaderAttributes = new List<InterfaceHeaderAttributeInfo>();
        string? interfaceTokenInjectionMode = null;
        string? interfaceTokenName = null;

        if (interfaceSymbol != null)
        {
            var headerAttributes = interfaceSymbol.GetAttributes()
                .Where(attr => attr.AttributeClass?.Name == "HeaderAttribute" || attr.AttributeClass?.Name == "Header");

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
                .Where(attr => (attr.AttributeClass?.Name == "QueryAttribute" || attr.AttributeClass?.Name == "Query") &&
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
                .Where(attr => attr.AttributeClass?.Name == "TokenAttribute" || attr.AttributeClass?.Name == "Token");

            foreach (var tokenAttr in tokenAttributes)
            {
                var injectionMode = GetTokenInjectionMode(tokenAttr);
                var tokenName = GetTokenName(tokenAttr);
                if (!string.IsNullOrEmpty(injectionMode))
                {
                    interfaceTokenInjectionMode = injectionMode;
                    interfaceTokenName = tokenName;
                    interfaceAttributes.Add($"Token:{injectionMode}:{tokenName}");
                }
            }
        }

        return (interfaceAttributes, interfaceHeaderAttributes, interfaceTokenInjectionMode, interfaceTokenName);
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
    /// 获取Header特性的名称
    /// </summary>
    private static string GetHeaderName(AttributeData headerAttr)
    {
        return AttributeDataHelper.GetStringValueFromAttribute(headerAttr, ["AliasAs", "Name"], 0, "Unknown") ?? "Unknown";
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
}
