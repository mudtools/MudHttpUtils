// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Diagnostics;
using Mud.HttpUtils.Analyzers;
using Mud.HttpUtils.Generator.Consts;

namespace Mud.HttpUtils.Generators.Context;

/// <summary>
/// 生成上下文
/// </summary>
[DebuggerDisplay("{InterfaceSymbol?.Name} (Methods={AllMethods.Count}, HasToken={HasTokenManager}, HasCache={HasCache})")]
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
    /// 消费方项目是否启用 AOT（build_property.IsAotCompatible=true 或 build_property.PublishAot=true）。
    /// 由 <see cref="HttpInvokeClassSourceGenerator"/> 从 AnalyzerConfigOptions.GlobalOptions 读取后逐层传入。
    /// 用于 AOT 下 XML 静态字段的条件化生成（见 ConstructorGenerator）。
    /// </summary>
    public bool IsAotEnabled { get; }

    /// <summary>
    /// [v2.4 §3.4 D-03 修复] 是否在生成代码头部发射 #nullable enable。
    /// 由 HttpInvokeClassSourceGenerator 从 build_property.Nullable 读取后逐层传入。
    /// 默认 true（向后兼容）。消费项目 Nullable=disable 时不发射，避免冗余告警。
    /// </summary>
    public bool EmitNullableEnable { get; }

    /// <summary>
    /// [D-06 修复] 是否在生成代码上标注 [GeneratedCode] 特性。
    /// 由 HttpInvokeClassSourceGenerator 从 build_property.MudEmitGeneratedCodeMarkers 读取后逐层传入。
    /// 默认 true（向后兼容）。设为 false 时不标注，便于调试生成代码中的警告。
    /// </summary>
    public bool EmitGeneratedCodeMarkers { get; }

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

    public IReadOnlyList<InterfacePropertyInfo> InterfaceProperties { get; set; } = [];

    /// <summary>
    /// 已由各片段生成器发射的成员名集合（方法/属性/事件名，含重载）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用途：契约补全（<see cref="Mud.HttpUtils.Generators.Implementation.InterfaceContractCompletionGenerator"/>
    /// 与 <c>MethodGenerator</c> 的占位实现）必须避开生成器<b>按模式无条件发射</b>的成员，
    /// 否则会产生重复成员（CS0111/CS0102）。
    /// </para>
    /// <para>
    /// 典型无条件成员：AppContext 模式的 <c>Current</c>/<c>BeginScope</c>/<c>UseApp</c>/<c>UseDefaultApp</c>/
    /// <c>UseDefaultAppScope</c>/<c>CurrentUserId</c>，令牌模式的 <c>GetTokenAsync</c>/<c>GetApiKeyAsync</c>/
    /// <c>GetTokenManagerKey</c> 等。这些成员与「接口是否声明」无关，故无法由符号侧推导。
    /// </para>
    /// <para>
    /// 约定：新增发射点时须同步 <see cref="MarkMemberProvided"/>，否则契约补全可能发射同名占位成员。
    /// </para>
    /// </remarks>
    public HashSet<string> ProvidedMemberNames { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 登记一个已发射的成员名（供契约补全避让）。见 <see cref="ProvidedMemberNames"/>。
    /// </summary>
    public void MarkMemberProvided(string memberName) => ProvidedMemberNames.Add(memberName);

    /// <summary>
    /// [SW-01] 接口（含<b>间接</b>继承）是否已声明应用切换契约（<c>IAppContextSwitcher</c> 或 <c>IAppScopeSwitcher</c>）。
    /// </summary>
    /// <remarks>
    /// 生成器属 analyzer，<b>不引用</b> Mud.HttpUtils.Abstractions 程序集，故按<b>完全限定显示名</b>（含 <c>global::</c> 前缀）比对，
    /// 避免与其它命名空间下的同名接口误匹配。判定含 <c>AllInterfaces</c>（间接继承），与 <c>SW-09</c>/令牌链路的既有口径一致。
    /// </remarks>
    public bool HasAppSwitchContract =>
        HasInterfaceNamed(GlobalLegacyAppContextSwitcher) || HasAppScopeSwitcherContract;

    /// <summary>
    /// [SW-01] 接口（含间接继承）是否已声明作用域面契约 <c>IAppScopeSwitcher</c>。
    /// </summary>
    /// <remarks>
    /// 为 <c>true</c> 时生成类已通过接口<b>传递</b>获得该契约，无需在继承列表重复追加（避免冗余接口项）。
    /// </remarks>
    public bool HasAppScopeSwitcherContract => HasInterfaceNamed(GlobalAppScopeSwitcher);

    /// <summary>
    /// [SW-08] 接口（含间接继承）是否继承了<b>旧</b>切换契约 <c>IAppContextSwitcher</c>。
    /// </summary>
    /// <remarks>
    /// 该契约含 <c>GetTokenAsync</c>，<b>仅</b> TokenManager 模式能完整生成；Default / HttpClient 模式下缺失成员会落入契约补全（<c>HTTPCLIENT024</c>）。
    /// 用于生成"请改继承 <c>IAppScopeSwitcher</c>"的场景化失败指引。
    /// </remarks>
    public bool HasLegacyAppContextSwitcherContract => HasInterfaceNamed(GlobalLegacyAppContextSwitcher);

    /// <summary><c>global::Mud.HttpUtils.IAppContextSwitcher</c> 的完全限定显示名常量。</summary>
    private const string GlobalLegacyAppContextSwitcher = "global::Mud.HttpUtils.IAppContextSwitcher";

    /// <summary><c>global::Mud.HttpUtils.IAppScopeSwitcher</c> 的完全限定显示名常量。</summary>
    private const string GlobalAppScopeSwitcher = "global::Mud.HttpUtils.IAppScopeSwitcher";

    /// <summary>
    /// 判定接口（含间接继承）是否继承指定完全限定名的接口。
    /// </summary>
    /// <param name="fullyQualifiedName">接口的完全限定显示名（含 <c>global::</c> 前缀）。</param>
    /// <returns>存在该基接口时返回 <c>true</c>。</returns>
    private bool HasInterfaceNamed(string fullyQualifiedName)
    {
        foreach (var candidate in InterfaceSymbol.AllInterfaces)
        {
            if (candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fullyQualifiedName)
                return true;
        }

        return false;
    }

    /// <summary>
    /// [BC-27] 接口（自身或任一基接口）是否声明了 <c>UseApp(string appKey)</c>。
    /// </summary>
    /// <remarks>
    /// <b>用途</b>：<c>BC-27</c> 移除了三个旧切换入口的<b>默认发射</b>；但当使用方接口<b>自行声明</b>同名成员时仍须发射实现 ——
    /// 否则该成员会落入契约补全（<c>NotSupportedException</c> 占位 + <c>HTTPCLIENT024</c>（Error）），
    /// 使原本可编译的代码变成编译失败（与 <c>SW-10</c> 被移出本轮是同一类风险）。
    /// </remarks>
    public bool DeclaresUseAppMember =>
        DeclaresMethod(AppSwitchMemberNames.UseApp, IsSingleStringParameter);

    /// <summary>
    /// [BC-27] 接口（自身或任一基接口）是否声明了 <c>UseDefaultApp()</c>。见 <see cref="DeclaresUseAppMember"/>。
    /// </summary>
    public bool DeclaresUseDefaultAppMember =>
        DeclaresMethod(AppSwitchMemberNames.UseDefaultApp, IsParameterless);

    /// <summary>
    /// [BC-27] 接口（自身或任一基接口）是否声明了 <c>BeginScope(string appKey)</c>。见 <see cref="DeclaresUseAppMember"/>。
    /// </summary>
    /// <remarks>
    /// ⚠️ 必须按<b>签名</b>而非仅按名字判定：<see cref="IAppContextHolder.BeginScope"/> 的实例重载
    /// （<c>BeginScope(IMudAppContext)</c>）属 Holder 面且<b>继续无条件发射</b>；
    /// 若只按名字判定，「接口继承 <c>IAppContextHolder</c>」会被误判为「声明了 <c>BeginScope(string)</c>」，
    /// 从而让退化的旧入口悄悄复活。
    /// </remarks>
    public bool DeclaresBeginScopeStringMember =>
        DeclaresMethod(AppSwitchMemberNames.BeginScope, IsSingleStringParameter);

    /// <summary>无参方法判定。</summary>
    private static bool IsParameterless(IMethodSymbol method) => method.Parameters.Length == 0;

    /// <summary>单 <see cref="string"/> 参数方法判定。</summary>
    private static bool IsSingleStringParameter(IMethodSymbol method)
        => method.Parameters.Length == 1
           && method.Parameters[0].Type.SpecialType == SpecialType.System_String;

    /// <summary>
    /// 判定接口（含全部基接口）是否声明了满足条件的同名方法（按<b>名字 + 签名谓词</b>匹配）。
    /// </summary>
    /// <param name="methodName">方法名。</param>
    /// <param name="predicate">签名判定谓词。</param>
    /// <returns>存在匹配方法时返回 <c>true</c>。</returns>
    private bool DeclaresMethod(string methodName, Func<IMethodSymbol, bool> predicate)
    {
        foreach (var candidate in InterfaceSymbol.GetMembers(methodName).OfType<IMethodSymbol>())
        {
            if (predicate(candidate))
                return true;
        }

        foreach (var baseInterface in InterfaceSymbol.AllInterfaces)
        {
            foreach (var candidate in baseInterface.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (predicate(candidate))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 接口符号的特性列表，在构造函数中一次性计算并缓存。
    /// 避免在 <see cref="GetOrAnalyzeMethod"/> 和 <see cref="DetectFeatures"/> 中重复调用
    /// <c>INamedTypeSymbol.GetAttributes()</c> 产生多次分配。
    /// </summary>
    public ImmutableArray<AttributeData> InterfaceAttributes { get; }

    /// <summary>
    /// 方法分析结果缓存，避免同一方法被 AnalyzeMethod 重复分析。
    /// 使用 SymbolEqualityComparer.Default 确保符号比较的正确性。
    /// 注意：此 Dictionary 仅在单线程生成上下文中使用（每个 GeneratorContext 实例对应一个接口的生成），
    /// 不可跨实例共享或在多线程环境下并发读写。
    /// </summary>
    public Dictionary<IMethodSymbol, MethodAnalysisResult> MethodAnalysisCache { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// FIX-13: 方法级特性缓存，避免同一方法的 GetAttributes() 被多次调用产生额外分配。
    /// InterfaceImplementationGenerator 的 ReportRetryNonIdempotentWithoutAllow / ReportMethodTimeoutConflicts /
    /// ReportResilienceAttributeValueRangeViolations 以及 PrecomputeXmlResponseTypes 各自遍历方法特性，
    /// 不缓存时每个方法最多 4 次 GetAttributes() 调用。
    /// </summary>
    private readonly Dictionary<IMethodSymbol, ImmutableArray<AttributeData>> _methodAttributes =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    /// FIX-13: 获取方法特性（带缓存），避免重复分配。
    /// </summary>
    public ImmutableArray<AttributeData> GetMethodAttributes(IMethodSymbol method) =>
        _methodAttributes.TryGetValue(method, out var cached)
            ? cached
            : (_methodAttributes[method] = method.GetAttributes());

    /// <summary>
    /// 当前接口（含父接口）的所有方法列表，在构造函数中一次性计算并缓存。
    /// 避免 MethodGenerator、InterfaceImplementationGenerator 等多处重复调用 TypeSymbolHelper.GetAllMethods。
    /// </summary>
    public IReadOnlyList<IMethodSymbol> AllMethods { get; private set; } = [];

    /// <summary>
    /// 获取或缓存方法分析结果。若缓存命中则复用，否则调用 AnalyzeMethod 并缓存结果。
    /// 传入预计算的 <see cref="InterfaceProperties"/> 以避免 AnalyzeMethod 内部重复扫描基接口属性。
    /// </summary>
    public MethodAnalysisResult GetOrAnalyzeMethod(
        Compilation compilation,
        IMethodSymbol methodSymbol,
        InterfaceDeclarationSyntax interfaceDeclaration,
        SemanticModel semanticModel)
    {
        if (MethodAnalysisCache.TryGetValue(methodSymbol, out var cached))
            return cached;

        var result = MethodAnalyzer.AnalyzeMethod(
                compilation, methodSymbol, interfaceDeclaration, semanticModel,
                cachedInterfaceProperties: InterfaceProperties,
                cachedInterfaceAttributes: InterfaceAttributes);
        MethodAnalysisCache[methodSymbol] = result;
        return result;
    }

    /// <summary>
    /// 接口是否有继承其他接口
    /// </summary>
    public bool HasBaseInterfaces => InterfaceSymbol.Interfaces.Length > 0;

    /// <summary>
    /// 根据 InheritedFrom 和 BaseHasTokenManager 属性值获取 GetTokenAsync 方法的访问修饰符
    /// - 继承自指定类且基类有 TokenManager：public override
    /// - 其他情况：public virtual
    /// </summary>
    public string GetTokenAsyncAccessibility => (HasInheritedFrom && Configuration.BaseHasTokenManager)
        ? "public override"
        : "public virtual";

    public string FieldAccessibility { get; }

    public GeneratorContext(
        Compilation compilation,
        INamedTypeSymbol interfaceSymbol,
        InterfaceDeclarationSyntax interfaceDeclaration,
        SemanticModel semanticModel,
        SourceProductionContext productionContext,
        GenerationConfiguration configuration,
        bool isAotEnabled = false,
        bool emitNullableEnable = true,
        bool emitGeneratedCodeMarkers = true)
    {
        Compilation = compilation;
        InterfaceSymbol = interfaceSymbol;
        InterfaceDeclaration = interfaceDeclaration;
        SemanticModel = semanticModel;
        ProductionContext = productionContext;
        Configuration = configuration;
        IsAotEnabled = isAotEnabled;
        EmitNullableEnable = emitNullableEnable;
        EmitGeneratedCodeMarkers = emitGeneratedCodeMarkers;

        ClassName = TypeSymbolHelper.GetImplementationClassName(interfaceSymbol.Name);
        NamespaceName = SyntaxHelper.GetNamespaceName(interfaceDeclaration, HttpClientGeneratorConstants.ImplementationNamespaceSuffix);
        FieldAccessibility = configuration.IsAbstract ? "protected " : "private ";

        // 一次性获取所有方法，避免 5 个 Detect 方法各自独立遍历接口方法树
        List<IMethodSymbol> allMethods;
        try
        {
            // 当继承自基类时，只检测当前接口自身定义的方法以及非基接口的方法（父接口方法由基类负责）
            if (!string.IsNullOrEmpty(configuration.InheritedFrom))
            {
                if (!string.IsNullOrEmpty(configuration.InheritedFromInterfaceName))
                    allMethods = TypeSymbolHelper.GetAllMethods(interfaceSymbol, true, [configuration.InheritedFromInterfaceName!]).ToList();
                else
                    allMethods = interfaceSymbol.GetMembers().OfType<IMethodSymbol>().ToList();
            }
            else
                allMethods = TypeSymbolHelper.GetAllMethods(interfaceSymbol, true).ToList();
        }
        catch (Exception ex)
        {
            GeneratorDebugLogger.LogError("GeneratorContext.GetAllMethods", ex);
            // 向用户报告诊断，确保 IDE 错误列表中可见
            productionContext.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientApiGenerationError,
                interfaceDeclaration.GetLocation(),
                interfaceSymbol.Name,
                $"解析接口方法时发生异常，已回退为仅生成当前接口方法: {GeneratorDebugLogger.FormatExceptionMessage(ex)}"));
            // 回退为仅当前接口自身定义的方法，与原 MethodGenerator.GetMethodsToGenerate 的容错行为一致
            allMethods = interfaceSymbol.GetMembers().OfType<IMethodSymbol>().ToList();
        }

        AllMethods = allMethods;

        // 预计算接口属性列表（含基接口的 [Query]/[Path] 属性），后续 GetOrAnalyzeMethod 会将其传入
        // AnalyzeMethod 作为 cachedInterfaceProperties，避免对每个方法重复扫描基接口属性树（O(N×M) -> O(M)）
        try
        {
            // 继承模式下解析 InheritedFrom 指向的基接口符号，用于标记「基类已实现、派生类不得重复声明」的属性
            ITypeSymbol? inheritedBaseInterface = null;
            if (!string.IsNullOrEmpty(configuration.InheritedFromInterfaceName))
            {
                inheritedBaseInterface = interfaceSymbol.AllInterfaces.FirstOrDefault(i =>
                    string.Equals(i.Name, configuration.InheritedFromInterfaceName, StringComparison.Ordinal));
            }

            InterfaceProperties = MethodAnalyzer.AnalyzeInterfaceProperties(
                interfaceDeclaration, compilation, semanticModel, inheritedBaseInterface);
        }
        catch (Exception ex)
        {
            GeneratorDebugLogger.LogError("GeneratorContext.AnalyzeInterfaceProperties", ex);
            // 回退为空列表，AnalyzeMethod 内部会在 cachedInterfaceProperties 为 null 时重新计算
            // 但此处传入空列表表示已知无接口属性，避免重复计算
            InterfaceProperties = [];
        }

        // 预计算接口特性列表，后续 GetOrAnalyzeMethod 和 DetectFeatures 共享，避免重复调用 GetAttributes()
        InterfaceAttributes = interfaceSymbol.GetAttributes();

        // 单次遍历检测全部 5 个特性标志，避免 5 个独立 Detect 方法各自遍历 allMethods
        // 并各自调用 method.GetAttributes() 产生重复分配
        DetectFeatures(allMethods);
    }

    /// <summary>
    /// 单次遍历所有方法，一次性检测 Cache、CacheVaryByUser、Resilience、ApiKeyInjection、HmacSignatureInjection。
    /// 接口级 Token 特性在循环前检查；方法级特性在单次循环中合并检测，尽早短路退出。
    /// </summary>
    private void DetectFeatures(IReadOnlyList<IMethodSymbol> allMethods)
    {
        try
        {
            // 接口级 Token 特性检查（单次调用，不在方法循环内重复）
            // 使用构造函数中预计算的 InterfaceAttributes，避免重复调用 GetAttributes()
            var interfaceTokenAttr = InterfaceAttributes
                .FirstOrDefault(attr => HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.AttributeClass?.Name));
            if (interfaceTokenAttr != null)
            {
                HasApiKeyInjection = IsInjectionMode(interfaceTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeApiKey);
                HasHmacSignatureInjection = IsInjectionMode(interfaceTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeHmacSignature);
            }

            // 全部已检出时可短路退出
            if (HasCacheVaryByUser && HasResilience && HasApiKeyInjection && HasHmacSignatureInjection)
                return;

            foreach (var method in allMethods)
            {
                var methodAttrs = GetMethodAttributes(method);

                // Cache 检测
                if (!HasCacheVaryByUser)
                {
                    var cacheAttr = methodAttrs.FirstOrDefault(attr =>
                        HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name));
                    if (cacheAttr != null)
                    {
                        HasCache = true;
                        if (AttributeDataHelper.GetBoolValueFromAttribute(cacheAttr, HttpClientGeneratorConstants.CacheVaryByUserProperty, false))
                            HasCacheVaryByUser = true;
                    }
                }
                else if (!HasCache)
                {
                    // VaryByUser 已检出但 HasCache 尚未标记（理论上 VaryByUser 蕴含 HasCache）
                    HasCache = methodAttrs.Any(attr =>
                        HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name));
                }

                // Resilience 检测
                if (!HasResilience)
                {
                    HasResilience = methodAttrs.Any(attr =>
                        HttpClientGeneratorConstants.RetryAttributeNames.Contains(attr.AttributeClass?.Name) ||
                        HttpClientGeneratorConstants.CircuitBreakerAttributeNames.Contains(attr.AttributeClass?.Name) ||
                        HttpClientGeneratorConstants.TimeoutAttributeNames.Contains(attr.AttributeClass?.Name));
                }

                // Token 注入模式检测
                if (!HasApiKeyInjection || !HasHmacSignatureInjection)
                {
                    var methodTokenAttr = methodAttrs.FirstOrDefault(attr =>
                        HttpClientGeneratorConstants.TokenAttributeNames.Contains(attr.AttributeClass?.Name));
                    if (methodTokenAttr != null)
                    {
                        if (!HasApiKeyInjection && IsInjectionMode(methodTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeApiKey))
                            HasApiKeyInjection = true;
                        if (!HasHmacSignatureInjection && IsInjectionMode(methodTokenAttr, HttpClientGeneratorConstants.TokenInjectionModeHmacSignature))
                            HasHmacSignatureInjection = true;
                    }
                }

                // 全部检出后短路退出
                if (HasCacheVaryByUser && HasResilience && HasApiKeyInjection && HasHmacSignatureInjection)
                    return;
            }
        }
        catch (Exception ex)
        {
            GeneratorDebugLogger.LogError("DetectFeatures", ex);
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
                var modeName = GetTokenInjectionModeName(namedArg.Value);
                return modeName == targetMode;
            }
        }
        // 未指定 InjectionMode 时默认为 Header，不匹配 ApiKey/HmacSignature
        return false;
    }

    /// <summary>
    /// 从 <see cref="TypedConstant"/>（TokenInjectionMode 枚举参数）获取注入模式字符串。
    /// 委托至 TokenHelper.GetTokenInjectionModeName 统一实现，覆盖全部 7 种注入模式。
    /// [D-3 选项 1] 现在把整个 <see cref="TypedConstant"/> 传入（而非剥离后的底层整数值）。
    /// </summary>
    private static string GetTokenInjectionModeName(TypedConstant value)
    {
        return TokenHelper.GetTokenInjectionModeName(value);
    }
}
