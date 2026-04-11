// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
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

    public string LoggerType { get; }

    public string ConstructorLoggerType { get; }

    public string OptionsFieldName { get; }

    public string OptionsParameterName { get; }

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
        LoggerType = configuration.IsAbstract ? "ILogger" : $"ILogger<{ClassName}>";
        ConstructorLoggerType = configuration.IsAbstract ? "ILogger" : $"ILogger<{ClassName}>";
        OptionsFieldName = PrivateFieldNamingHelper.GeneratePrivateFieldName(configuration.HttpClientOptionsName);
        OptionsParameterName = PrivateFieldNamingHelper.GeneratePrivateFieldName(configuration.HttpClientOptionsName, FieldNamingStyle.PureCamel);
    }
}
