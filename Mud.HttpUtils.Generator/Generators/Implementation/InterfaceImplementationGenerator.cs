// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Analyzers;
using Mud.HttpUtils.Generators.Base;
using Mud.HttpUtils.Generators.Context;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 接口实现生成器（流程编排器）
/// </summary>
internal class InterfaceImplementationGenerator
{
    private readonly Compilation _compilation;
    private readonly InterfaceDeclarationSyntax _interfaceDecl;
    private readonly SourceProductionContext _context;
    private readonly INamedTypeSymbol _interfaceSymbol;
    private readonly SemanticModel _semanticModel;
    private readonly StringBuilder _codeBuilder;
    private readonly string _optionsName;

    public InterfaceImplementationGenerator(
        Compilation compilation,
        InterfaceDeclarationSyntax interfaceDecl,
        INamedTypeSymbol interfaceSymbol,
        SemanticModel semanticModel,
        SourceProductionContext context,
        string optionsName)
    {
        _compilation = compilation;
        _interfaceDecl = interfaceDecl;
        _interfaceSymbol = interfaceSymbol;
        _semanticModel = semanticModel;
        _context = context;
        _optionsName = optionsName;

        var estimatedCapacity = EstimateCodeCapacity();
        _codeBuilder = new StringBuilder(estimatedCapacity);
    }

    /// <summary>
    /// 生成代码入口
    /// </summary>
    public void GenerateCode()
    {
        var configuration = ExtractConfigurationFromAttributes();

        if (!ValidateConfiguration(configuration))
            return;

        var generatorContext = new GeneratorContext(
            _compilation,
            _interfaceSymbol,
            _interfaceDecl,
            _semanticModel,
            _context,
            configuration);

        AnalyzeAndSetInterfaceProperties(generatorContext);

        ComputeAnyMethodRequiresUserId(generatorContext);

        DetectICurrentUserId(generatorContext);

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
            _codeBuilder.AppendLine("            System.Collections.Specialized.NameValueCollection queryParams,");
            _codeBuilder.AppendLine("            bool includeNullValues,");
            _codeBuilder.AppendLine("            bool useJsonSerialization,");
            _codeBuilder.AppendLine("            bool urlEncode = true,");
            _codeBuilder.AppendLine("            System.Collections.Generic.List<string>? rawPairs = null,");
            _codeBuilder.AppendLine("            int depth = 0)");
            _codeBuilder.AppendLine("        {");
            _codeBuilder.AppendLine("            QueryMapHelper.FlattenObjectToQueryParams(obj, prefix, separator, queryParams, includeNullValues, useJsonSerialization, urlEncode, rawPairs, depth);");
            _codeBuilder.AppendLine("        }");
        }

        _codeBuilder.AppendLine("    }");
        _codeBuilder.AppendLine("}");
        _codeBuilder.AppendLine();

        var fileName = $"{generatorContext.ClassName}.g.cs";
        _context.AddSource(
            fileName,
            SourceText.From(_codeBuilder.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// 验证生成配置的合法性
    /// </summary>
    private bool ValidateConfiguration(GenerationConfiguration configuration)
    {
        var isValid = true;

        if (_interfaceSymbol.IsGenericType)
        {
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientApiGenericInterfaceNotSupported,
                _interfaceDecl.GetLocation(),
                _interfaceSymbol.Name));
            isValid = false;
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
            ValidateHttpClientType(configuration.HttpClient);
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

    private void AnalyzeAndSetInterfaceProperties(GeneratorContext context)
    {
        var interfaceProperties = MethodAnalyzer.AnalyzeInterfaceProperties(
            _interfaceDecl,
            _compilation,
            _semanticModel);

        if (interfaceProperties.Count > 0)
        {
            context.InterfaceProperties = interfaceProperties;
        }
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

        var allMethods = TypeSymbolHelper.GetAllMethods(_interfaceSymbol, true);
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
