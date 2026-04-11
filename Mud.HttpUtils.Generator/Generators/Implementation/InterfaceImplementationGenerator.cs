// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
        var generatorContext = new GeneratorContext(
            _compilation,
            _interfaceSymbol,
            _interfaceDecl,
            _semanticModel,
            _context,
            configuration);

        var generators = InitializeGenerators(generatorContext);

        foreach (var generator in generators)
        {
            generator.Generate(_codeBuilder, generatorContext);
        }

        _codeBuilder.AppendLine("    }");
        _codeBuilder.AppendLine("}");
        _codeBuilder.AppendLine();

        var fileName = $"{generatorContext.NamespaceName}.{generatorContext.ClassName}.g.cs".Replace('.', '_');
        _context.AddSource(
            fileName,
            SourceText.From(_codeBuilder.ToString(), Encoding.UTF8));
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

        // 互斥逻辑：HttpClient 优先
        var effectiveTokenManage = !string.IsNullOrEmpty(httpClient) ? null : tokenManage;

        return new GenerationConfiguration
        {
            HttpClientOptionsName = _optionsName,
            DefaultContentType = GetHttpClientApiContentTypeFromAttribute(httpClientApiAttribute),
            TimeoutFromAttribute = AttributeDataHelper.GetIntValueFromAttribute(
                httpClientApiAttribute,
                HttpClientGeneratorConstants.TimeoutProperty,
                100),
            BaseAddressFromAttribute = AttributeDataHelper.GetStringValueFromAttributeConstructor(
                httpClientApiAttribute,
                HttpClientGeneratorConstants.BaseAddressProperty),
            IsAbstract = isAbstract,
            InheritedFrom = inheritedFrom,
            HttpClient = httpClient,
            TokenManager = effectiveTokenManage,
            TokenManagerType = !string.IsNullOrEmpty(effectiveTokenManage)
                ? TypeSymbolHelper.GetTypeAllDisplayString(_compilation, effectiveTokenManage!)
                : null,
            TokenType = GetInterfaceTokenType(),
            IsUserAccessToken = GetInterfaceTokenType() == "UserAccessToken"
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
}
