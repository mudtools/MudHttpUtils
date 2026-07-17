// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;

namespace Mud.HttpUtils;

/// <summary>
/// HttpClient API 注册源生成器
/// </summary>
/// <remarks>
/// 基于 Roslyn 技术，自动为标记了 [HttpClientApi] 特性的接口生成依赖注入注册代码
/// </remarks>
[Generator(LanguageNames.CSharp)]
internal class HttpInvokeRegistrationGenerator : HttpInvokeBaseSourceGenerator
{
    /// <inheritdoc/>
    protected override void ExecuteGenerator(
        ImmutableArray<InterfaceModel> interfaces,
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider configOptionsProvider)
    {
        if (interfaces.IsDefaultOrEmpty || configOptionsProvider == null)
            return;

        // T5.3: 全局禁用开关（调试与渐进迁移）
        if (ProjectConfigHelper.ReadConfigValueAsBool(configOptionsProvider.GlobalOptions, "build_property.DisableMudSourceGenerator", false))
            return;

        // [v2.4 §3.4] 读取消费项目 nullable 配置，条件化发射 #nullable enable
        EmitNullableEnable = ProjectConfigHelper.ReadConfigValue(
            configOptionsProvider.GlobalOptions, "build_property.Nullable", "enable") == "enable";

        var httpClientApis = CollectHttpClientApis(interfaces, context);

        if (httpClientApis.Count == 0)
            return;

        var compilation = interfaces[0].Context.SemanticModel.Compilation;

        // 1. 生成 HttpClientApiExtensions.g.cs（DI 注册扩展方法）
        var extensionSourceCode = GenerateExtensionClassCode(compilation, httpClientApis, context);
        AddSourceValidated(context, "HttpClientApiExtensions.g.cs", extensionSourceCode);

        // 2. T0.2: 生成 GeneratedFactoryRegistration.g.cs（ModuleInitializer 工厂注册）
        var factorySourceCode = GenerateFactoryRegistrationCode(httpClientApis);
        if (!string.IsNullOrEmpty(factorySourceCode))
        {
            AddSourceValidated(context, "GeneratedFactoryRegistration.g.cs", factorySourceCode);
        }
    }

    /// <inheritdoc/>
    protected override System.Collections.ObjectModel.Collection<string> GetFileUsingNameSpaces()
    {
        return ["System", "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Extensions", "System.Runtime.CompilerServices", "System.Net.Http", "Microsoft.Extensions.Logging"];
    }

    private List<HttpClientApiInfo> CollectHttpClientApis(ImmutableArray<InterfaceModel> models, SourceProductionContext context)
    {
        return CollectApiInfos<HttpClientApiInfo>(models, context, model => ProcessInterface(model, context));
    }

    /// <summary>
    /// 通用的 API 信息收集方法，消除重复代码
    /// </summary>
    private List<T> CollectApiInfos<T>(ImmutableArray<InterfaceModel> models,
        SourceProductionContext context,
        Func<InterfaceModel, T?> processor)
    {
        var apiInfos = new List<T>();

        foreach (var model in models)
        {
            if (context.CancellationToken.IsCancellationRequested)
                return apiInfos;

            try
            {
                var apiInfo = processor(model);
                if (apiInfo != null)
                {
                    apiInfos.Add(apiInfo);
                }
            }
            catch (Exception ex)
            {
                ReportInterfaceProcessingError(context, model.Syntax, ex);
            }
        }

        return apiInfos;
    }

    private HttpClientApiInfo? ProcessInterface(InterfaceModel model, SourceProductionContext context)
    {
        var interfaceSyntax = model.Syntax;
        var semanticModel = model.Context.SemanticModel;
        var compilation = semanticModel.Compilation;
        var interfaceSymbol = model.Symbol ?? semanticModel.GetDeclaredSymbol(interfaceSyntax) as INamedTypeSymbol;
        if (interfaceSymbol == null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientApiGenerationError,
                interfaceSyntax.GetLocation(),
                interfaceSyntax.Identifier.Text,
                "Could not resolve interface symbol. This may occur when the interface has syntax errors or is in an incomplete state."));
            return null;
        }

        var httpClientApiAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(interfaceSymbol, HttpClientGeneratorConstants.HttpClientApiAttributeNames);
        if (httpClientApiAttribute == null)
            return null;

        // 检查是否标记为忽略实现
        var ignoreGeneratorAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(interfaceSymbol, HttpClientGeneratorConstants.IgnoreGeneratorAttributeNames);
        if (ignoreGeneratorAttribute != null)
            return null;

        var isAbstract = AttributeDataHelper.GetBoolValueFromAttribute(httpClientApiAttribute, HttpClientGeneratorConstants.IsAbstractProperty, false);
        if (isAbstract)
            return null;

        var timeout = ExtractTimeoutParameter(httpClientApiAttribute);

        var registryGroupName = AttributeDataHelper.GetStringValueFromAttribute(httpClientApiAttribute, HttpClientGeneratorConstants.RegistryGroupNameProperty);

        // 验证 RegistryGroupName
        if (!CSharpCodeValidator.ValidateAndReportRegistryGroupName(context, interfaceSyntax.GetLocation(), registryGroupName))
            return null;

        var implementationName = TypeSymbolHelper.GetImplementationClassName(interfaceSymbol.Name);
        var namespaceName = SyntaxHelper.GetNamespaceName(interfaceSyntax);

        // 提取 HttpClient 和 TokenManager 类型信息，用于生成注册提示注释
        var httpClient = AttributeDataHelper.GetStringValueFromAttribute(httpClientApiAttribute, HttpClientGeneratorConstants.HttpClientProperty);
        var tokenManage = AttributeDataHelper.GetStringValueFromAttribute(httpClientApiAttribute, HttpClientGeneratorConstants.TokenManageProperty);
        // 互斥逻辑：HttpClient 优先，与 InterfaceImplementationGenerator 一致
        var effectiveTokenManage = !string.IsNullOrEmpty(httpClient) ? null : tokenManage;
        var tokenManagerType = !string.IsNullOrEmpty(effectiveTokenManage)
            ? TypeSymbolHelper.GetTypeAllDisplayString(compilation, effectiveTokenManage!)
            : null;

        return new HttpClientApiInfo(
            interfaceSymbol.Name,
            implementationName,
            namespaceName,
            string.Empty,
            timeout,
            registryGroupName,
            httpClient,
            tokenManagerType);
    }

    private int ExtractTimeoutParameter(AttributeData httpClientApiAttribute)
    {
        return AttributeDataHelper.GetIntValueFromAttribute(httpClientApiAttribute, HttpClientGeneratorConstants.TimeoutProperty, 100);
    }


    private void ReportInterfaceProcessingError(SourceProductionContext context, InterfaceDeclarationSyntax interfaceSyntax, Exception ex)
    {
        ReportErrorDiagnostic(context, Diagnostics.HttpClientRegistrationGenerationError, interfaceSyntax.Identifier.Text, ex, interfaceSyntax.GetLocation());
    }

    private string GenerateSourceCode(Compilation compilation, List<HttpClientApiInfo> apis, SourceProductionContext context)
    {
        // 预估容量：每个API注册约200字符，基础结构约500字符
        var estimatedCapacity = 500 + (apis.Count * 200);
        var codeBuilder = new StringBuilder(estimatedCapacity);
        GenerateExtensionClass(compilation, codeBuilder, apis, context);
        return codeBuilder.ToString();
    }

    private string GenerateExtensionClassCode(Compilation compilation, List<HttpClientApiInfo> apis, SourceProductionContext context)
        => GenerateSourceCode(compilation, apis, context);

    private void GenerateExtensionClass(Compilation compilation, StringBuilder codeBuilder, List<HttpClientApiInfo> apis, SourceProductionContext context)
    {
        GenerateFileHeader(codeBuilder);

        codeBuilder.AppendLine();
        var @namespace = compilation.AssemblyName;
        var targetNamespace = string.IsNullOrEmpty(@namespace) ? "Microsoft.Extensions.DependencyInjection" : @namespace;

        codeBuilder.AppendLine($"namespace {targetNamespace}");
        codeBuilder.AppendLine("{");
        codeBuilder.AppendLine($"    {CompilerGeneratedAttribute}");
        codeBuilder.AppendLine($"    {GeneratedCodeAttribute}");
        codeBuilder.AppendLine("    internal static class HttpClientApiExtensions");
        codeBuilder.AppendLine("    {");
        GenerateAddWebApiHttpClientMethod(codeBuilder, apis, context);
        codeBuilder.AppendLine("    }");
        codeBuilder.AppendLine("}");
    }

    /// <summary>
    /// 生成 GeneratedFactoryRegistration.g.cs：包含 [ModuleInitializer] 自动注册代码。
    /// 仅在 net5.0+ 下生成 ModuleInitializer；netstandard2.0 生成普通静态方法（需手动调用）。
    /// 工厂委托内部构造 DefaultHttpRequestExecutor 等依赖。
    /// <para>
    /// v3.4 修正（L-11）：仅对默认模式（无 TokenManagerType 且无 HttpClientType）的接口生成注册代码。
    /// HttpClient / TokenManager 模式的实现类构造函数需要用户自定义类型，工厂委托无法构造，需通过 DI 容器使用。
    /// </para>
    /// </summary>
    private string GenerateFactoryRegistrationCode(List<HttpClientApiInfo> apis)
    {
        // v3.4 L-11：仅对默认模式接口生成注册代码（无 TokenManagerType 且无 HttpClientType）
        var defaultModeApis = apis
            .Where(a => string.IsNullOrEmpty(a.HttpClientType) && string.IsNullOrEmpty(a.TokenManagerType))
            .ToList();

        if (defaultModeApis.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(500 + defaultModeApis.Count * 600);
        GenerateFileHeader(sb);

        sb.AppendLine();
        sb.AppendLine("namespace Mud.HttpUtils");
        sb.AppendLine("{");
        sb.AppendLine($"    {CompilerGeneratedAttribute}");
        sb.AppendLine($"    {GeneratedCodeAttribute}");
        sb.AppendLine("    internal static partial class GeneratedFactoryRegistration");
        sb.AppendLine("    {");

        // net5.0+ 生成 [ModuleInitializer]
        sb.AppendLine("#if NET5_0_OR_GREATER");
        sb.AppendLine("        [System.Diagnostics.CodeAnalysis.SuppressMessage(");
        sb.AppendLine("            \"Usage\",");
        sb.AppendLine("            \"CA2255:The ModuleInitializer attribute should not be used in libraries\",");
        sb.AppendLine("            Justification = \"ModuleInitializer 用于自动注册源生成的 API 客户端工厂\")]");
        sb.AppendLine("        [System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Initialize()");
        sb.AppendLine("        {");
        foreach (var api in defaultModeApis)
        {
            GenerateFactoryRegistrationCall(sb, api);
        }
        sb.AppendLine("        }");
        sb.AppendLine("#else");
        // netstandard2.0：生成普通静态方法供消费方手动调用
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// 注册所有源生成的 API 客户端工厂（netstandard2.0 不支持 ModuleInitializer，需手动调用）。</summary>");
        sb.AppendLine("        internal static void RegisterAllFactories()");
        sb.AppendLine("        {");
        foreach (var api in defaultModeApis)
        {
            GenerateFactoryRegistrationCall(sb, api);
        }
        sb.AppendLine("        }");
        sb.AppendLine("#endif");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 生成单个默认模式接口的工厂注册调用代码。
    /// 工厂委托构造 DefaultHttpRequestExecutor 并调用实现类构造函数（默认模式签名）。
    /// </summary>
    private void GenerateFactoryRegistrationCall(StringBuilder sb, HttpClientApiInfo api)
    {
        var fullyQualifiedInterface = $"global::{api.Namespace}.{api.InterfaceName}";
        var fullyQualifiedImplementation = $"global::{api.Namespace}.{HttpClientGeneratorConstants.ImplementationNamespaceSuffix}.{api.ImplementationName}";

        sb.AppendLine($"            global::Mud.HttpUtils.RestService.RegisterGeneratedFactory<{fullyQualifiedInterface}>((client, options) =>");
        sb.AppendLine("            {");
        // v3.4 L-11：AppContext 为必需参数（IMudAppContext 无通用默认实现）
        sb.AppendLine("                var appContext = options?.AppContext");
        sb.AppendLine("                    ?? throw new System.InvalidOperationException(");
        sb.AppendLine($"                        \"ForGenerated<{api.InterfaceName}> requires options.AppContext to be set. \" +");
        sb.AppendLine("                        \"IMudAppContext has no default implementation; construct one and assign to GeneratedClientOptions.AppContext.\");");
        // AppContextHolder 为可选，为 null 时创建默认 AsyncLocalAppContextSwitcher
        sb.AppendLine("                var appContextHolder = options?.AppContextHolder");
        sb.AppendLine("                    ?? new global::Mud.HttpUtils.AsyncLocalAppContextSwitcher();");
        // DefaultHttpRequestExecutor 构造函数：第一个参数是 ILogger<DefaultHttpRequestExecutor>，不是 HttpClient
        sb.AppendLine("                var executorLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<global::Mud.HttpUtils.DefaultHttpRequestExecutor>.Instance;");
        sb.AppendLine("                var executor = new global::Mud.HttpUtils.DefaultHttpRequestExecutor(");
        sb.AppendLine("                    executorLogger,");
        sb.AppendLine("                    options?.CacheProvider,");
        sb.AppendLine("                    options?.ResilienceResolver,");
        sb.AppendLine("                    appResilienceResolver: null,");
        sb.AppendLine("                    appContextHolder: appContextHolder,");
        sb.AppendLine("                    contentSerializer: options?.ContentSerializer,");
        sb.AppendLine("                    exceptionRedactor: options?.ExceptionRedactor,");
        sb.AppendLine("                    maxExceptionContentLength: options?.MaxExceptionContentLength,");
        sb.AppendLine("                    captureRequestContent: options?.CaptureRequestContent,");
        sb.AppendLine("#if NET6_0_OR_GREATER");
        sb.AppendLine("                    httpVersion: options?.HttpVersion,");
        sb.AppendLine("                    httpVersionPolicy: options?.HttpVersionPolicy,");
        sb.AppendLine("#endif");
        sb.AppendLine("                    httpRequestMessageOptions: options?.HttpRequestMessageOptions);");
        // 调用实现类构造函数（默认模式签名）
        sb.AppendLine($"                return new {fullyQualifiedImplementation}(");
        sb.AppendLine("                    appContext,");
        sb.AppendLine("                    appContextHolder,");
        sb.AppendLine("                    executor,");
        sb.AppendLine("                    appManager: null,");
        sb.AppendLine("                    cacheProvider: options?.CacheProvider,");
        sb.AppendLine("                    resilienceResolver: options?.ResilienceResolver,");
        sb.AppendLine("                    contentSerializer: options?.ContentSerializer,");
        sb.AppendLine("                    logger: null);");
        sb.AppendLine("            });");
    }

    private void GenerateAddWebApiHttpClientMethod(StringBuilder codeBuilder, List<HttpClientApiInfo> apis, SourceProductionContext context)
    {
        GenerateDefaultRegistrationMethod(codeBuilder, apis);
        GenerateGroupedRegistrationMethods(codeBuilder, apis, context);
    }

    /// <summary>
    /// 生成默认注册函数（用于未分组的APIs）
    /// </summary>
    private void GenerateDefaultRegistrationMethod(StringBuilder codeBuilder, List<HttpClientApiInfo> apis)
    {
        GenerateDefaultRegistrationMethodInternal(codeBuilder, apis, HttpClientApiInfoBaseExtensions, "AddWebApiHttpClient");
    }

    /// <summary>
    /// 通用的默认注册函数生成方法
    /// </summary>
    private void GenerateDefaultRegistrationMethodInternal<T>(StringBuilder codeBuilder, List<T> apis, Action<StringBuilder, T> registrationGenerator, string methodName) where T : HttpClientApiInfoBase
    {
        var ungroupedApis = apis.Where(api => string.IsNullOrEmpty(api.RegistryGroupName)).ToList();

        if (ungroupedApis.Count == 0)
            return;

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 注册所有未分组的API服务");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"services\">服务集合</param>");
        codeBuilder.AppendLine("        /// <returns>服务集合，用于链式调用</returns>");
        codeBuilder.AppendLine($"        {CompilerGeneratedAttribute}");
        codeBuilder.AppendLine($"        {GeneratedCodeAttribute}");
        codeBuilder.AppendLine($"        public static IServiceCollection {methodName}(this IServiceCollection services)");
        codeBuilder.AppendLine("        {");

        foreach (var api in ungroupedApis)
        {
            registrationGenerator(codeBuilder, api);
        }

        codeBuilder.AppendLine("            return services;");
        codeBuilder.AppendLine("        }");
    }

    /// <summary>
    /// HttpClientApiInfo 注册生成的委托
    /// </summary>
    private void HttpClientApiInfoBaseExtensions(StringBuilder codeBuilder, HttpClientApiInfo api)
    {
        GenerateHttpClientRegistration(codeBuilder, api);
    }

    /// <summary>
    /// 生成分组注册函数
    /// </summary>
    private void GenerateGroupedRegistrationMethods(StringBuilder codeBuilder, List<HttpClientApiInfo> apis, SourceProductionContext context)
    {
        GenerateGroupedRegistrations<HttpClientApiInfo>(
            codeBuilder,
            apis,
            HttpClientApiInfoBaseExtensions,
            "HttpClient",
            "注册所有标记了 [HttpClientApi] 特性且 RegistryGroupName = \"{0}\" 的接口及其 HttpClient 实现",
            context);
    }

    /// <summary>
    /// 通用的分组注册生成方法，用于处理 HttpClientApi 和 WrapApi
    /// </summary>
    private void GenerateGroupedRegistrations<T>(StringBuilder codeBuilder,
        List<T> apiInfos,
        Action<StringBuilder, T> registrationGenerator,
        string serviceType,
        string descriptionTemplate,
        SourceProductionContext context) where T : HttpClientApiInfoBase
    {
        var groupedApis = apiInfos
            .Where(api => !string.IsNullOrEmpty(api.RegistryGroupName))
            .GroupBy(api => api.RegistryGroupName!)
            .ToList();

        foreach (var group in groupedApis)
        {
            var description = descriptionTemplate.Replace("{0}", group.Key);
            GenerateGroupedRegistrationMethod(
                codeBuilder,
                group.Key,
                group,
                registrationGenerator,
                serviceType,
                description,
                context);
        }
    }

    /// <summary>
    /// 生成单个分组注册方法
    /// </summary>
    private void GenerateGroupedRegistrationMethod<T>(StringBuilder codeBuilder,
        string groupName,
        IEnumerable<T> apiInfos,
        Action<StringBuilder, T> registrationGenerator,
        string serviceType,
        string description,
        SourceProductionContext context) where T : HttpClientApiInfoBase
    {
        // 验证 RegistryGroupName 是否为合法的 C# 标识符
        if (!CSharpCodeValidator.IsValidCSharpIdentifier(groupName))
        {
            CSharpCodeValidator.ValidateAndReportRegistryGroupName(context, Location.None, groupName);
            return;
        }

        codeBuilder.AppendLine();
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine($"        /// {description}");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"services\">服务集合</param>");
        codeBuilder.AppendLine("        /// <returns>服务集合，用于链式调用</returns>");
        codeBuilder.AppendLine($"        {CompilerGeneratedAttribute}");
        codeBuilder.AppendLine($"        {GeneratedCodeAttribute}");
        codeBuilder.AppendLine($"        public static IServiceCollection Add{groupName}WebApi{serviceType}(this IServiceCollection services)");
        codeBuilder.AppendLine("        {");

        foreach (var api in apiInfos)
        {
            registrationGenerator(codeBuilder, api);
        }

        codeBuilder.AppendLine("            return services;");
        codeBuilder.AppendLine("        }");
    }

    private void GenerateHttpClientRegistration(StringBuilder codeBuilder, HttpClientApiInfo api)
    {
        var fullyQualifiedInterface = $"global::{api.Namespace}.{api.InterfaceName}";
        var fullyQualifiedImplementation = $"global::{api.Namespace}.{HttpClientGeneratorConstants.ImplementationNamespaceSuffix}.{api.ImplementationName}";

        if (!string.IsNullOrEmpty(api.HttpClientType))
        {
            codeBuilder.AppendLine($"            // 注册 {api.InterfaceName} 的 HttpClient 包装实现类（瞬时服务）");
            codeBuilder.AppendLine($"            // 注意：实现类构造函数依赖 {api.HttpClientType}，请确保已通过 AddMudHttpClient 等方法注册此服务");
            // HttpClient 模式下，实现类构造函数还需注入 IHttpRequestExecutor。
            // 使用 TryAddTransient 自动注册默认执行器（若用户未自定义注册）。
            // DefaultHttpRequestExecutor 构造函数的 cacheProvider 和 resilienceResolver 为可选参数，
            // DI 容器会在对应服务已注册时自动注入，未注册时使用默认值 null。
            codeBuilder.AppendLine("            services.TryAddTransient<global::Mud.HttpUtils.IBaseHttpClient>(sp => sp.GetRequiredService<global::Mud.HttpUtils.IEnhancedHttpClient>());");
            codeBuilder.AppendLine("            services.TryAddTransient<global::Mud.HttpUtils.IHttpRequestExecutor, global::Mud.HttpUtils.DefaultHttpRequestExecutor>();");
        }
        else if (!string.IsNullOrEmpty(api.TokenManagerType))
        {
            codeBuilder.AppendLine($"            // 注册 {api.InterfaceName} 的 HttpClient 包装实现类（瞬时服务）");
            codeBuilder.AppendLine($"            // 注意：实现类构造函数依赖 {api.TokenManagerType}，请确保已注册此令牌管理器服务");
        }
        else
        {
            codeBuilder.AppendLine($"            // 注册 {api.InterfaceName} 的 HttpClient 包装实现类（瞬时服务）");
        }

        var httpClientName = $"{api.InterfaceName}_HttpClient";
        var timeoutSeconds = api.Timeout;

        codeBuilder.AppendLine($"            services.AddHttpClient(\"{httpClientName}\", client =>");
        codeBuilder.AppendLine($"            {{");
        codeBuilder.AppendLine($"                client.Timeout = TimeSpan.FromSeconds({timeoutSeconds});");
        // BaseAddress 应通过 AddMudHttpClient(clientName, baseAddress) 在运行时配置，此处不生成
        codeBuilder.AppendLine($"            }});");
        codeBuilder.AppendLine($"            services.AddTransient<{fullyQualifiedInterface}, {fullyQualifiedImplementation}>();");
    }
}
