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
    /// <summary>注册代码 namespace 的回退目标（AssemblyName 非法/为空时使用）。</summary>
    private const string FallbackNamespace = "Microsoft.Extensions.DependencyInjection";
    /// <inheritdoc/>
    protected override void ExecuteGenerator(
        ImmutableArray<InterfaceModel> interfaces,
        SourceProductionContext context,
        GeneratorConfigSnapshot configSnapshot,
        string generationSalt)
    {
        if (interfaces.IsDefaultOrEmpty || configSnapshot == null)
            return;

        // T5.3: 全局禁用开关（调试与渐进迁移）；起从配置值快照读取
        if (configSnapshot.Disable)
            return;

        var httpClientApis = CollectHttpClientApis(interfaces, context);

        if (httpClientApis.Count == 0)
            return;

        // G7-04a：同一编译 ≥2 个 [HttpClientApi] 接口共存时提示命名客户端绑定脱节。
        // 实现类构造函数注入类型级 IEnhancedHttpClient/IHttpRequestExecutor（不按命名客户端解析），
        // 各接口的命名客户端与 [HttpClientApi(Timeout)] 配置仅在对应名称成为默认 IEnhancedHttpClient 时生效。
        // 级别 Info + 仅多接口报告，避免单接口工程的构建噪音（与 降噪口径一致）。
        if (httpClientApis.Count >= 2)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientNamedClientBindingMismatch,
                httpClientApis[0].Location ?? Location.None,
                httpClientApis.Count));
        }

        // 此处 Compilation 可能来自**上一轮编译**（InterfaceModel 的等价性由源码指纹决定，
        // 仅当接口声明变化时才会重新 transform）。因此它**只允许**用于跨编译稳定的信息：
        //   · _compilation.AssemblyName（ResolveRegistrationNamespace，:242-252）
        //   · 生成注释中的类型全名（TypeSymbolHelper.GetTypeAllDisplayString）
        // 新增用法前必须评估「编译代际一致性」——与历史 HTTPCLIENT004（语法树/编译代际不一致）同源的
        // 易复发模式（见 HttpInvokeBaseSourceGenerator.Initialize 的「已接受的权衡」说明）。
        // 若将来确实需要新鲜 Compilation，须注意：引入 CompilationProvider 会把全局注册点退化为
        // 编译级粒度（每次编辑重跑）——本册 §10 已登记为「不做」。
        var compilation = interfaces[0].Context.SemanticModel.Compilation;

        // 1. 生成 HttpClientApiExtensions.g.cs（DI 注册扩展方法）
        // nullable 配置以参数显式下传（原实例可变属性 EmitNullableEnable 已移除）
        var extensionSourceCode = GenerateExtensionClassCode(compilation, httpClientApis, context, configSnapshot.NullableEnable);
        AddSourceValidated(context, "HttpClientApiExtensions.g.cs", extensionSourceCode);

        // 2. T0.2: 生成 GeneratedFactoryRegistration.g.cs（ModuleInitializer 工厂注册）
        var factorySourceCode = GenerateFactoryRegistrationCode(httpClientApis, configSnapshot.NullableEnable);
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

        // 泛型接口只生成实现类（类型参数与约束转发），不生成 DI 注册：
        // 开放泛型无法用 AddTransient<TService, TImplementation>() 配合默认模式的
        // appContext 工厂 lambda（无法闭合类型参数），生成的注册代码必然 CS0305。
        // 消费方需按封闭泛型手动注册；HTTPCLIENT012 已提示该边界。
        if (interfaceSymbol.IsGenericType)
            return null;

        var timeout = ExtractTimeoutParameter(httpClientApiAttribute);

        var registryGroupName = AttributeDataHelper.GetStringValueFromAttribute(httpClientApiAttribute, HttpClientGeneratorConstants.RegistryGroupNameProperty);

        // 验证 RegistryGroupName
        if (!CSharpCodeValidator.ValidateAndReportRegistryGroupName(context, interfaceSyntax.GetLocation(), registryGroupName))
            return null;

        var implementationName = TypeSymbolHelper.GetImplementationClassName(interfaceSymbol.Name);
        var namespaceName = SyntaxHelper.GetNamespaceName(interfaceSyntax);

        // [F7 修复] 接口声明在全局命名空间时，无法生成合法的 DI 注册代码（global::.IApi 不合法）。
        // 明确报错并给出迁移指引，而非静默产出非法 C#。
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientRegistrationGenerationError,
                interfaceSyntax.GetLocation(),
                interfaceSyntax.Identifier.Text,
                "接口声明在全局命名空间中，生成器无法为其生成 DI 注册代码。请将接口放入显式命名空间。"));
            return null;
        }

        // 提取 HttpClient 和 TokenManager 类型信息，用于生成注册提示注释
        var httpClient = AttributeDataHelper.GetStringValueFromAttribute(httpClientApiAttribute, HttpClientGeneratorConstants.HttpClientProperty);
        var tokenManage = AttributeDataHelper.GetStringValueFromAttribute(httpClientApiAttribute, HttpClientGeneratorConstants.TokenManageProperty);
        // 互斥逻辑：HttpClient 优先，与 InterfaceImplementationGenerator 一致（统一 IsNullOrWhiteSpace，F16）。
        var effectiveTokenManage = !string.IsNullOrWhiteSpace(httpClient) ? null : tokenManage;
        var tokenManagerType = !string.IsNullOrEmpty(effectiveTokenManage)
            ? TypeSymbolHelper.GetTypeAllDisplayString(compilation, effectiveTokenManage!)
            : null;

        // [P0-1 修复·注册侧对齐] 复刻构造侧 ExtractConfigurationFromAttributes 的 InheritedFrom
        // 来源解析口径（auto-detect 抽象基 + 显式 InheritedFrom 名称反查），判定是否为
        // 「宿主基类模式」——该模式下普通基接口的方法由宿主基类实现，派生侧不传缓存/弹性依赖。
        // 与 InterfaceImplementationGenerator.cs:634-704 同口径；漂移由
        // RegistrationConstructorContractGuardTests（名集合差 + 值级断言）兜底。
        var inheritedFrom = AttributeDataHelper.GetStringValueFromAttribute(
            httpClientApiAttribute, HttpClientGeneratorConstants.InheritedFromProperty);
        if (string.IsNullOrEmpty(inheritedFrom))
        {
            foreach (var directBaseInterface in interfaceSymbol.Interfaces)
            {
                var baseApiAttr = directBaseInterface.GetAttributes()
                    .FirstOrDefault(attr => HttpClientGeneratorConstants.HttpClientApiAttributeNames.Contains(attr.AttributeClass?.Name));
                if (baseApiAttr != null && AttributeDataHelper.GetBoolValueFromAttribute(
                        baseApiAttr, HttpClientGeneratorConstants.IsAbstractProperty, false))
                {
                    inheritedFrom = TypeSymbolHelper.GetImplementationClassName(directBaseInterface.Name);
                    break;
                }
            }
        }

        var hostBaseClassMode = !string.IsNullOrEmpty(inheritedFrom)
            && !interfaceSymbol.AllInterfaces.Any(baseInterface => string.Equals(
                TypeSymbolHelper.GetImplementationClassName(baseInterface.Name),
                inheritedFrom, StringComparison.Ordinal));

        // F-04：扫描接口（含继承链）方法级特性，判定 cacheProvider / resilienceResolver 在
        // 工厂注册中是否必需解析，与 ConstructorGenerator 的 needsCacheParam / needsResilienceParam
        // 口径一致。AllInterfaces 已覆盖继承链。
        // [P0-1 修复·注册侧对齐] 基接口扫描面按「继承形态」分流，与构造侧逐场景恒等（已推演验证）：
        //   · 非继承 / 生成基模式（InheritedFrom 名称反查到来源接口）：全部基接口 —— 前者的
        //     AllMethods 含全部基接口方法，后者的来源接口方法由基类实现且经 BaseHasCache
        //     （要求基接口带 [HttpClientApi]）覆盖，两侧扫描天然重合；
        //   · 宿主基类模式（InheritedFrom 有值但反查不到来源接口）：仅带 [HttpClientApi] 的基接口 ——
        //     普通基接口的方法由宿主基类实现（构造侧 AllMethods 仅含自身方法），派生侧不传
        //     缓存/弹性依赖；若不过滤，注册侧会向派生类可选形参传 GetRequiredService，
        //     其中 IResiliencePolicyResolver 无默认注册 ⇒ 运行期 DI 解析失败。
        var baseInterfacesToScan = hostBaseClassMode
            ? interfaceSymbol.AllInterfaces.Where(i => i.GetAttributes().Any(attr =>
                HttpClientGeneratorConstants.HttpClientApiAttributeNames.Contains(attr.AttributeClass?.Name)))
            : interfaceSymbol.AllInterfaces;

        var hasCache = false;
        var hasResilience = false;
        foreach (var method in interfaceSymbol.GetMembers()
            .Concat(baseInterfacesToScan.SelectMany(i => i.GetMembers()))
            .OfType<IMethodSymbol>())
        {
            foreach (var attr in method.GetAttributes())
            {
                var attrName = attr.AttributeClass?.Name;
                if (HttpClientGeneratorConstants.CacheAttributeNames.Contains(attrName))
                    hasCache = true;
                if (HttpClientGeneratorConstants.RetryAttributeNames.Contains(attrName)
                    || HttpClientGeneratorConstants.CircuitBreakerAttributeNames.Contains(attrName)
                    || HttpClientGeneratorConstants.TimeoutAttributeNames.Contains(attrName))
                    hasResilience = true;
            }
        }

        return new HttpClientApiInfo(
            interfaceSymbol.Name,
            implementationName,
            namespaceName,
            timeout,
            registryGroupName,
            httpClient,
            tokenManagerType,
            interfaceSyntax.GetLocation())
        {
            HasCache = hasCache,
            HasResilience = hasResilience
        };
    }

    private int ExtractTimeoutParameter(AttributeData httpClientApiAttribute)
    {
        // 与 HttpClientApiAttribute.DefaultTimeoutSeconds 保持一致（50）。
        // 生成器按字符串名匹配特性，不引用 Attributes 程序集，故此处引用 Generator 常量；
        // 二者一致性由测试守护：Attributes 侧 CFG03_HttpClientApiAttribute_DefaultTimeout_Is50（Client.Tests）、
        // Generator 常量 CFG03_DefaultTimeoutConstant_Is50（Generator.Tests）、
        // 生成产物 RegistrationTimeoutGenerationTests（Generator.Tests，T-06）。
        return AttributeDataHelper.GetIntValueFromAttribute(
            httpClientApiAttribute,
            HttpClientGeneratorConstants.TimeoutProperty,
            HttpClientGeneratorConstants.DefaultHttpClientTimeoutSeconds);
    }


    private void ReportInterfaceProcessingError(SourceProductionContext context, InterfaceDeclarationSyntax interfaceSyntax, Exception ex)
    {
        ReportErrorDiagnostic(context, Diagnostics.HttpClientRegistrationGenerationError, interfaceSyntax.Identifier.Text, ex, interfaceSyntax.GetLocation());
    }

    private string GenerateSourceCode(Compilation compilation, List<HttpClientApiInfo> apis, SourceProductionContext context, bool emitNullableEnable)
    {
        // 预估容量：每个API注册约200字符，基础结构约500字符
        var estimatedCapacity = 500 + (apis.Count * 200);
        var codeBuilder = new StringBuilder(estimatedCapacity);
        GenerateExtensionClass(compilation, codeBuilder, apis, context, emitNullableEnable);
        return codeBuilder.ToString();
    }

    /// <summary>
    /// [F7] 解析注册代码的命名空间：AssemblyName 参与拼接时，逐段校验合法 C# 标识符。
    /// 非法/为空时回退到 <see cref="FallbackNamespace"/>，避免产出非法 C#。
    /// </summary>
    private static string ResolveRegistrationNamespace(Compilation compilation)
    {
        var assemblyName = compilation.AssemblyName;
        if (assemblyName is null || assemblyName.Trim().Length == 0)
            return FallbackNamespace;

        return assemblyName.Split('.')
            .All(segment => CSharpCodeValidator.IsValidCSharpIdentifier(segment))
                ? assemblyName
                : FallbackNamespace;
    }

    private string GenerateExtensionClassCode(Compilation compilation, List<HttpClientApiInfo> apis, SourceProductionContext context, bool emitNullableEnable)
        => GenerateSourceCode(compilation, apis, context, emitNullableEnable);

    private void GenerateExtensionClass(Compilation compilation, StringBuilder codeBuilder, List<HttpClientApiInfo> apis, SourceProductionContext context, bool emitNullableEnable)
    {
        GenerateFileHeader(codeBuilder, emitNullableEnable);

        codeBuilder.AppendLine();
        // [F7 修复] namespace 取 AssemblyName 时须校验其每个以 . 分隔的段都是合法 C# 标识符；
        // AssemblyName 允许 '-'、空格、首字符数字等非法标识符字符（MSBuild 默认取项目名不做替换），
        // 直接代入会产出非法 C#。非法/为空时回退到 DI 扩展方法约定的命名空间。
        var targetNamespace = ResolveRegistrationNamespace(compilation);

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
    private string GenerateFactoryRegistrationCode(List<HttpClientApiInfo> apis, bool emitNullableEnable)
    {
        // v3.4 仅对默认模式接口生成注册代码（无 TokenManagerType 且无 HttpClientType）
        var defaultModeApis = apis
            .Where(a => string.IsNullOrEmpty(a.HttpClientType) && string.IsNullOrEmpty(a.TokenManagerType))
            .ToList();

        if (defaultModeApis.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(500 + defaultModeApis.Count * 600);
        GenerateFileHeader(sb, emitNullableEnable);

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
        // 接口名可能是保留字（如 interface @class）：ISymbol.Name 不含 @ ⇒ 直接拼接产出 global::Ns.class（CS1001）。
        var fullyQualifiedInterface = $"global::{api.Namespace}.{IdentifierEscaper.Escape(api.InterfaceName)}";
        var fullyQualifiedImplementation = $"global::{api.Namespace}.{HttpClientGeneratorConstants.ImplementationNamespaceSuffix}.{api.ImplementationName}";

        sb.AppendLine($"            global::Mud.HttpUtils.RestService.RegisterGeneratedFactory<{fullyQualifiedInterface}>((client, options) =>");
        sb.AppendLine("            {");
        // v3.4 AppContext 为必需参数（IMudAppContext 无通用默认实现）
        sb.AppendLine("                var appContext = options?.AppContext");
        sb.AppendLine("                    ?? throw new System.InvalidOperationException(");
        sb.AppendLine($"                        \"ForGenerated<{api.InterfaceName}> requires options.AppContext to be set. \" +");
        sb.AppendLine("                        \"IMudAppContext has no default implementation; construct one and assign to GeneratedClientOptions.AppContext.\");");
        // AppContextHolder 为可选：为 null 时创建默认 AsyncLocalAppContextSwitcher。
        // [AOT demo 场景13修复] 新建的 holder 此前从未 SwitchTo(appContext)，方法体的
        // _appContextHolder.Current 恒为 null → ForGenerated 无 DI 入口调用任意方法即抛
        // "无法找到当前服务的应用上下文"。options 显式传入的 holder 归调用方所有（可能共享），
        // 不得擅自切换；仅对工厂新建的 holder 注入 appContext。
        sb.AppendLine("                var appContextHolder = options?.AppContextHolder;");
        sb.AppendLine("                if (appContextHolder is null)");
        sb.AppendLine("                {");
        sb.AppendLine("                    appContextHolder = new global::Mud.HttpUtils.AsyncLocalAppContextSwitcher();");
        sb.AppendLine("                    appContextHolder.SwitchTo(appContext);");
        sb.AppendLine("                }");
        // DefaultHttpRequestExecutor 构造函数：第一个参数是 ILogger<DefaultHttpRequestExecutor>，不是 HttpClient
        sb.AppendLine("                var executorLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<global::Mud.HttpUtils.DefaultHttpRequestExecutor>.Instance;");
        sb.AppendLine("                var executor = new global::Mud.HttpUtils.DefaultHttpRequestExecutor(");
        sb.AppendLine("                    executorLogger,");
        sb.AppendLine("                    options?.CacheProvider,");
        sb.AppendLine("                    options?.ResilienceResolver,");
        // P3-7：接线 options.AppResilienceResolver / options.AppManager（无 DI 工厂路径此前
        // 硬编码 null ⇒ 应用级弹性策略隔离与 UseApp/BeginScope 能力缺失）；两者均可选，
        // 为 null 时行为与既往一致。
        sb.AppendLine("                    appResilienceResolver: options?.AppResilienceResolver,");
        sb.AppendLine("                    appContextHolder: appContextHolder,");
        sb.AppendLine("                    appManager: options?.AppManager,");
        sb.AppendLine("                    contentSerializer: options?.ContentSerializer,");
        // 无 DI 工厂路径的默认脱敏（P2-3）：DI 路径由 AddMudHttpClient 的 TryAddSingleton 兜底，
        // 而工厂路径此前传 null ⇒ 异常消息中的敏感字段不脱敏（两条路径安全能力不对等）。
        // 显式注入仍优先（options?.ExceptionRedactor ?? 默认实现）。
        sb.AppendLine("                    exceptionRedactor: options?.ExceptionRedactor ?? new global::Mud.HttpUtils.DefaultSensitiveFieldExceptionRedactor(),");
        sb.AppendLine("                    maxExceptionContentLength: options?.MaxExceptionContentLength,");
        // 接线成功响应体守卫（DI 路径早已接通；无 DI 工厂此前遗漏 ⇒ 能力缺口）。
        // 语义与 DI 路径一致：0 = 不限制。
        sb.AppendLine("                    maxSuccessResponseBytes: options?.MaxSuccessResponseBytes ?? 0,");
        sb.AppendLine("                    captureRequestContent: options?.CaptureRequestContent ?? false,");
        // 接线敏感数据掩码器（无 DI 路径此前完全缺失脱敏能力，属安全缺口）
        sb.AppendLine("                    sensitiveDataMasker: options?.SensitiveDataMasker,");
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
        // P3-7：接线 options.AppManager（此前硬编码 null）。
        sb.AppendLine("                    appManager: options?.AppManager,");
        sb.AppendLine("                    appAuthorizer: options?.AppAccessAuthorizer,");
        sb.AppendLine("                    cacheProvider: options?.CacheProvider,");
        sb.AppendLine("                    resilienceResolver: options?.ResilienceResolver,");
        sb.AppendLine("                    contentSerializer: options?.ContentSerializer,");
        sb.AppendLine("                    logger: null);");
        sb.AppendLine("            });");
    }

    private void GenerateAddWebApiHttpClientMethod(StringBuilder codeBuilder, List<HttpClientApiInfo> apis, SourceProductionContext context)
    {
        GenerateDefaultRegistrationMethod(codeBuilder, apis, context);
        GenerateGroupedRegistrationMethods(codeBuilder, apis, context);
    }

    /// <summary>
    /// 生成默认注册函数（用于未分组的APIs）
    /// </summary>
    private void GenerateDefaultRegistrationMethod(StringBuilder codeBuilder, List<HttpClientApiInfo> apis, SourceProductionContext context)
    {
        GenerateDefaultRegistrationMethodInternal(codeBuilder, apis, HttpClientApiInfoBaseExtensions, "AddWebApiHttpClient", context);
    }

    /// <summary>
    /// 通用的默认注册函数生成方法
    /// </summary>
    private void GenerateDefaultRegistrationMethodInternal<T>(StringBuilder codeBuilder, List<T> apis, Action<StringBuilder, T, SourceProductionContext> registrationGenerator, string methodName, SourceProductionContext context) where T : HttpClientApiInfoBase
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
            registrationGenerator(codeBuilder, api, context);
        }

        codeBuilder.AppendLine("            return services;");
        codeBuilder.AppendLine("        }");
    }

    /// <summary>
    /// HttpClientApiInfo 注册生成的委托
    /// </summary>
    private void HttpClientApiInfoBaseExtensions(StringBuilder codeBuilder, HttpClientApiInfo api, SourceProductionContext context)
    {
        GenerateHttpClientRegistration(codeBuilder, api, context);
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
        Action<StringBuilder, T, SourceProductionContext> registrationGenerator,
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
        Action<StringBuilder, T, SourceProductionContext> registrationGenerator,
        string serviceType,
        string description,
        SourceProductionContext context) where T : HttpClientApiInfoBase
    {
        // 验证 RegistryGroupName 是否为合法的 C# 标识符
        if (!CSharpCodeValidator.IsValidCSharpIdentifier(groupName))
        {
            // P3-4：分组名来自接口特性，校验失败时携带首个归属接口的 Location 就地定位，
            // 而非退化到 Location.None（首个归属接口未携带 Location 时才回退）。
            // 注：标准管线中非法组名已在 ProcessInterface 入口被过滤并按接口 Location 上报，
            // 本分支为防御性兜底。
            CSharpCodeValidator.ValidateAndReportRegistryGroupName(
                context,
                apiInfos.Select(static a => a.Location).FirstOrDefault(static l => l is not null) ?? Location.None,
                groupName);
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
            registrationGenerator(codeBuilder, api, context);
        }

        codeBuilder.AppendLine("            return services;");
        codeBuilder.AppendLine("        }");
    }

    private void GenerateHttpClientRegistration(StringBuilder codeBuilder, HttpClientApiInfo api, SourceProductionContext context)
    {
        var fullyQualifiedInterface = $"global::{api.Namespace}.{IdentifierEscaper.Escape(api.InterfaceName)}";
        var fullyQualifiedImplementation = $"global::{api.Namespace}.{HttpClientGeneratorConstants.ImplementationNamespaceSuffix}.{api.ImplementationName}";

        if (!string.IsNullOrEmpty(api.HttpClientType))
        {
            codeBuilder.AppendLine($"            // 注册 {api.InterfaceName} 的 HttpClient 包装实现类（瞬时服务）");
            codeBuilder.AppendLine($"            // 注意：实现类构造函数依赖 {api.HttpClientType}，请确保已通过 services.AddMudHttpClient() 注册此服务及 IHttpRequestExecutor 等基础设施。");
            // 移除生成器对 IBaseHttpClient / IHttpRequestExecutor 的自动注册。
            // 原实现在此发射 TryAddTransient，早于宿主 AddMudHttpClient 内部的 TryAddSingleton，
            // 导致后者的 cacheProvider/resilienceResolver/exceptionRedactor/sensitiveDataMasker 全被抢占为 null。
            // 基础设施注册所有权收敛至 AddMudHttpClient（落地）。
        }
        else if (!string.IsNullOrEmpty(api.TokenManagerType))
        {
            codeBuilder.AppendLine($"            // 注册 {api.InterfaceName} 的 HttpClient 包装实现类（瞬时服务）");
            codeBuilder.AppendLine($"            // 注意：实现类构造函数依赖 {api.TokenManagerType}，请确保已注册此令牌管理器服务。");
            codeBuilder.AppendLine($"            // 必需依赖：ITokenProvider、IAppContextHolder");
            codeBuilder.AppendLine($"            // 可选依赖（需 UserId 时）：ICurrentUserContext");
            codeBuilder.AppendLine($"            // 可选依赖（缓存/弹性）：IHttpResponseCache、IResiliencePolicyResolver —— 请通过 services.AddMudHttpClient() 注册。");
            // TokenManager 模式同理不再自动注册基础设施服务。
        }
        else
        {
            codeBuilder.AppendLine($"            // 注册 {api.InterfaceName} 的 HttpClient 包装实现类（瞬时服务）");
            // A3：为默认模式接口自动注册 IAppManager<IMudAppContext> 工厂（TryAddSingleton，不覆盖宿主注册）。
            // 消除 UseApp/BeginScope 在缺少 IAppManager 注册时抛 InvalidOperationException 的静默降级。
            // 该自动注册的实例<b>未注册任何应用</b>，UseApp/GetDefaultApp 仍会失败，
            // 因此把失败信息从"未注册 IAppManager"改为"已注册但未注册应用"，避免误导性降级。
            codeBuilder.AppendLine("            services.TryAddSingleton<global::Mud.HttpUtils.IAppManager<global::Mud.HttpUtils.IMudAppContext>>(sp =>");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine("                var loggerFactory = sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();");
            codeBuilder.AppendLine("                loggerFactory?.CreateLogger(\"Mud.HttpUtils.AppManager\")?.LogWarning(\"源生成器自动注册了空的 DefaultAppManager<IMudAppContext>（未注册任何应用）。UseApp/BeginScope(appKey) 与 GetDefaultApp() 将失败，请显式注册应用管理器并调用 RegisterApp。\");");
            codeBuilder.AppendLine("                return new global::Mud.HttpUtils.DefaultAppManager<global::Mud.HttpUtils.IMudAppContext>();");
            codeBuilder.AppendLine("            });");
        }

        var httpClientName = $"{api.InterfaceName}_HttpClient";
        var timeoutSeconds = api.Timeout;

        // MT-14（BC-21）：原实现调用裸 services.AddHttpClient，绕过 AddMudHttpClient 的整套接线，
        // 导致：① 无 keyed IEnhancedHttpClient 注册；② CreateEnhancedClient 的配置覆盖
        // （AllowCustomBaseUrls / DefaultHeaders / BaseAddress）对生成客户端不生效；
        // ③ 不注册 TracingDelegatingHandler、IAppContextHolder 等基础设施。
        // 改用 AddMudHttpClient 后与宿主手动注册的命名客户端完全同构。
        // 以完全限定的静态调用形式发出，避免依赖消费方是否开启了 `using Mud.HttpUtils;`
        // （生成文件不发出任何 using 指令）。
        // G7-04a：命名客户端仅在该名称成为默认 IEnhancedHttpClient 时被实现类使用。
        // 实现类构造函数注入类型级 IEnhancedHttpClient/IHttpRequestExecutor（RegisterNamedClient 先注册者胜），
        // 因此多接口共存时各命名客户端的 Timeout/BaseAddress 不按命名隔离，请阅读 README「生成客户端命名」章节。
        codeBuilder.AppendLine("            // 注意：实现类当前通过类型级 IEnhancedHttpClient 解析；命名客户端（" + httpClientName + "）仅在该名称被注册为默认 IEnhancedHttpClient 时生效。");
        // P1-3：Timeout <= 0 不生成赋值 —— HttpClient.Timeout 不接受 Zero/负值（运行期 AOORE，
        // 发生在命名客户端创建期且消息不指向特性）。显式 0/负值的语义是「使用默认 100 秒」，
        // 与 README「大于 0 时」的表述对齐，并报 HTTPCLIENT042 指向特性位置。
        if (timeoutSeconds > 0)
        {
            codeBuilder.AppendLine($"            global::Mud.HttpUtils.HttpClientServiceCollectionExtensions.AddMudHttpClient(services, \"{httpClientName}\", client =>");
            codeBuilder.AppendLine($"            {{");
            codeBuilder.AppendLine($"                client.Timeout = global::System.TimeSpan.FromSeconds({timeoutSeconds});");
            // BaseAddress 应通过 AddMudHttpClient(clientName, baseAddress) 在运行时配置，此处不生成
            codeBuilder.AppendLine($"            }});");
        }
        else
        {
            codeBuilder.AppendLine($"            global::Mud.HttpUtils.HttpClientServiceCollectionExtensions.AddMudHttpClient(services, \"{httpClientName}\", null);");
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientApiTimeoutIgnored,
                api.Location ?? Location.None,
                api.InterfaceName,
                timeoutSeconds));
        }

        // F-04：默认模式（无 HttpClient / TokenManager）构造必需 IMudAppContext，裸 AddTransient<IFoo, Impl>
        // 在 DI 容器无法解析该参数（空容器或未注册 IMudAppContext 时激活即抛）。
        // 工厂化注册：以「DI 注入的默认应用」语义显式化 GetDefaultApp()（未注册默认应用时 fail-closed，
        // 异常消息指向注册应用），必需依赖 GetRequiredService（缺注册报错清晰）、可选依赖 GetService
        // （保持构造默认值语义）。TokenManager / HttpClient 模式构造参数均可由 DI 直接满足，维持裸注册。
        if (string.IsNullOrEmpty(api.HttpClientType) && string.IsNullOrEmpty(api.TokenManagerType))
        {
            codeBuilder.AppendLine($"            services.AddTransient<{fullyQualifiedInterface}>(sp =>");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine("                var appManager = sp.GetRequiredService<global::Mud.HttpUtils.IAppManager<global::Mud.HttpUtils.IMudAppContext>>();");
            // 解析期的「空管理器」失败必须自带根因与修复步骤。
            // 背景：TryAddSingleton 自动注册的 DefaultAppManager 不含任何应用，其 LogWarning 依赖
            // ILoggerFactory（AddMudHttpClient 不注册日志基础设施 ⇒ 裸容器下完全静默），
            // 而 GetDefaultApp() 抛出的原始消息只说明"未设置默认应用"，不指向真因。
            // 约束：try 块**只包住 GetDefaultApp() 单语句** —— DefaultAppManager.GetApp() 会先经
            // AppKeyValidator 抛 ArgumentException，包住其它调用会误吞并改变消息语义。
            codeBuilder.AppendLine("                global::Mud.HttpUtils.IMudAppContext __appContext;");
            codeBuilder.AppendLine("                try");
            codeBuilder.AppendLine("                {");
            codeBuilder.AppendLine("                    __appContext = appManager.GetDefaultApp();");
            codeBuilder.AppendLine("                }");
            codeBuilder.AppendLine("                catch (global::System.InvalidOperationException ex)");
            codeBuilder.AppendLine("                {");
            codeBuilder.AppendLine("                    throw new global::System.InvalidOperationException(");
            codeBuilder.AppendLine("                        \"源生成器已自动注册空的 IAppManager<IMudAppContext>（未注册任何应用），因此无法解析默认应用。请二选一：\" +");
            codeBuilder.AppendLine("                        \"① 注册自定义应用管理器，并在启动时调用 RegisterApp(appKey, context, isDefault: true)；\" +");
            codeBuilder.AppendLine("                        \"② 单应用场景请注册 IAppManager<IMudAppContext>（例如 DefaultAppManager<IMudAppContext>）并把唯一应用注册为默认。\",");
            codeBuilder.AppendLine("                        ex);");
            codeBuilder.AppendLine("                }");
            codeBuilder.AppendLine($"                return new {fullyQualifiedImplementation}(");
            codeBuilder.AppendLine("                    appContext: __appContext,");
            codeBuilder.AppendLine("                    appContextHolder: sp.GetRequiredService<global::Mud.HttpUtils.IAppContextHolder>(),");
            codeBuilder.AppendLine("                    executor: sp.GetRequiredService<global::Mud.HttpUtils.IHttpRequestExecutor>(),");
            codeBuilder.AppendLine("                    appManager: appManager,");
            codeBuilder.AppendLine("                    appAuthorizer: sp.GetService<global::Mud.HttpUtils.IAppAccessAuthorizer>(),");
            codeBuilder.AppendLine($"                    cacheProvider: {(api.HasCache ? "sp.GetRequiredService<global::Mud.HttpUtils.IHttpResponseCache>()" : "null")},");
            codeBuilder.AppendLine($"                    resilienceResolver: {(api.HasResilience ? "sp.GetRequiredService<global::Mud.HttpUtils.IResiliencePolicyResolver>()" : "null")},");
            codeBuilder.AppendLine("                    contentSerializer: sp.GetService<global::Mud.HttpUtils.IHttpContentSerializer>(),");
            codeBuilder.AppendLine("                    logger: sp.GetService<global::Microsoft.Extensions.Logging.ILogger>());");
            codeBuilder.AppendLine("            });");
        }
        else
        {
            codeBuilder.AppendLine($"            services.AddTransient<{fullyQualifiedInterface}, {fullyQualifiedImplementation}>();");
        }
    }
}
