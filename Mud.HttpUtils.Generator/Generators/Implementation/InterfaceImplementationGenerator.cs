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

        var generators = InitializeGenerators(generatorContext);

        foreach (var generator in generators)
        {
            generator.Generate(_codeBuilder, generatorContext);
        }

        if (generatorContext.HasQueryMap)
        {
            GenerateFlattenObjectHelper(_codeBuilder);
        }

        _codeBuilder.AppendLine("    }");
        _codeBuilder.AppendLine("}");
        _codeBuilder.AppendLine();

        //var fileName = $"{generatorContext.NamespaceName}.{generatorContext.ClassName}.g.cs".Replace('.', '_');
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

        // 只有用户令牌才添加 AccessTokenGenerator
        if (context.Configuration.IsUserAccessToken)
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

        var baseAddress = AttributeDataHelper.GetStringValueFromAttributeConstructor(
            httpClientApiAttribute,
            HttpClientGeneratorConstants.BaseAddressProperty);

        if (!string.IsNullOrEmpty(baseAddress))
        {
            _context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientApiBaseAddressObsolete,
                _interfaceDecl.GetLocation(),
                _interfaceSymbol.Name));
        }

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

    private static void GenerateFlattenObjectHelper(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("        private const int MaxFlattenRecursionDepth = 10;");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("        private static readonly System.Collections.Concurrent.ConcurrentDictionary<System.Type, System.Reflection.PropertyInfo[]> __propertyCache = new();");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("        private static void FlattenObjectToQueryParams(");
        codeBuilder.AppendLine("            object obj,");
        codeBuilder.AppendLine("            string prefix,");
        codeBuilder.AppendLine("            string separator,");
        codeBuilder.AppendLine("            System.Collections.Specialized.NameValueCollection __queryParams,");
        codeBuilder.AppendLine("            bool includeNullValues,");
        codeBuilder.AppendLine("            bool useJsonSerialization,");
        codeBuilder.AppendLine("            int __depth = 0)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            if (obj == null) throw new ArgumentNullException(nameof(obj));");
        codeBuilder.AppendLine("            if (__depth > MaxFlattenRecursionDepth) throw new InvalidOperationException(\"Maximum recursion depth exceeded while flattening object of type \" + obj.GetType().Name + \". This may be caused by a circular reference.\");");
        codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine("#pragma warning disable IL2072");
        codeBuilder.AppendLine("#endif");
        codeBuilder.AppendLine("            var __properties = __propertyCache.GetOrAdd(obj.GetType(), t => t.GetProperties());");
        codeBuilder.AppendLine("            foreach (var __prop in __properties)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                var __value = __prop.GetValue(obj);");
        codeBuilder.AppendLine("                var __key = string.IsNullOrEmpty(prefix) ? __prop.Name : prefix + separator + __prop.Name;");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("                if (__value == null)");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine("                    if (includeNullValues)");
        codeBuilder.AppendLine("                        __queryParams.Add(__key, null);");
        codeBuilder.AppendLine("                    continue;");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("                var __type = __value.GetType();");
        codeBuilder.AppendLine("                if (__type.IsPrimitive || __value is string || __value is decimal || __type.IsEnum || __value is System.DateTime || __value is System.DateTimeOffset || __value is System.Guid)");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine("                    string __stringValue;");
        codeBuilder.AppendLine("                    if (useJsonSerialization)");
        codeBuilder.AppendLine("                        __stringValue = System.Text.Json.JsonSerializer.Serialize(__value);");
        codeBuilder.AppendLine("                    else");
        codeBuilder.AppendLine("                        __stringValue = __value.ToString() ?? string.Empty;");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("                    __queryParams.Add(__key, __stringValue);");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine("                else if (__value is IQueryParameter __queryParam)");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine("                    foreach (var __kvp in __queryParam.ToQueryParameters())");
        codeBuilder.AppendLine("                    {");
        codeBuilder.AppendLine("                        var __subKey = string.IsNullOrEmpty(prefix) ? __kvp.Key : prefix + separator + __kvp.Key;");
        codeBuilder.AppendLine("                        if (includeNullValues || !string.IsNullOrEmpty(__kvp.Value))");
        codeBuilder.AppendLine("                        {");
        codeBuilder.AppendLine("                            __queryParams.Add(__subKey, __kvp.Value ?? string.Empty);");
        codeBuilder.AppendLine("                        }");
        codeBuilder.AppendLine("                    }");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine("                else");
        codeBuilder.AppendLine("                {");
        codeBuilder.AppendLine("                    FlattenObjectToQueryParams(__value, __key, separator, __queryParams, includeNullValues, useJsonSerialization, __depth + 1);");
        codeBuilder.AppendLine("                }");
        codeBuilder.AppendLine("            }");
        codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine("#pragma warning restore IL2072");
        codeBuilder.AppendLine("#endif");
        codeBuilder.AppendLine("        }");
    }
}
