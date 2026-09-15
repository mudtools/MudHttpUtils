// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// [Phase4 修复 5.1] CA1308（建议把 ToLowerInvariant 换成 ToUpperInvariant）在本文件内属**误报**：
// 生成产物需要小写形式——bool 字面量（C# 只接受 "true"/"false"）、
// ResponseDescriptor/ExecutionDescriptor 的枚举与标识符文本、camelCase 的 JSON 属性名。
// 大写形式会产出语义错误或不可编译的代码。故按方案 5.1 的「显式 #pragma + 理由」方式就地抑制。
#pragma warning disable CA1308

using Mud.HttpUtils.Analyzers;
using Mud.HttpUtils.Generators.Base;
using Mud.HttpUtils.Generators.Context;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 方法生成器，负责生成接口方法的实现代码
/// </summary>
internal class MethodGenerator : ICodeFragmentGenerator
{
    private readonly RequestBuilder _requestBuilder;

    public MethodGenerator()
    {
        _requestBuilder = new RequestBuilder();
    }

    public void Generate(StringBuilder codeBuilder, GeneratorContext context)
    {
        IEnumerable<IMethodSymbol> methodsToGenerate = GetMethodsToGenerate(context);
        var isAbstractClass = context.Configuration.IsAbstract;

        foreach (var methodSymbol in methodsToGenerate)
        {
            // 属性/事件访问器（PropertyGet/PropertySet/EventAdd/EventRemove 等）不是接口的独立方法成员：
            // 它们随属性/事件整体由 InterfaceContractCompletionGenerator（或 ConstructorGenerator 的
            // 接口属性发射）处理；若在此按独立方法发射，会与属性访问器同名冲突（CS0082）。
            // 说明：原先这些访问器在本循环中因「无 HTTP 方法特性」被静默跳过，此判定与其行为等价。
            if (methodSymbol.MethodKind != MethodKind.Ordinary)
                continue;

            // 仅接受已知的 HTTP 方法特性名（Get/Post/... 见 HttpClientGeneratorConstants.SupportedHttpMethods）：
            // 生成器由「特性名」推导 HTTP 动词并发射 HttpMethod.<Verb>，故继承 HttpMethodAttribute 的自定义特性
            // 无法映射到合法动词（会产出 CS0117）。该限制与 MUD001 分析器口径一致，
            // 报告给用户的是明确的 MUD001，而不是一段运行期才抛异常的占位实现。
            var isHttpMethod = MethodAnalyzer.FindHttpMethodAttributeFromAttributes(
                methodSymbol.GetAttributes()) != null;
            if (!isHttpMethod)
            {
                // 缺少 HTTP 方法特性：生成器无法生成 HTTP 调用实现。
                // 不得静默跳过 —— 否则实现类缺失接口成员会产生 CS0535，
                // 掩盖 MUD001（「缺少 HTTP 方法特性」）等真正的原因。
                EmitContractCompletionStub(codeBuilder, context, methodSymbol, "该成员缺少 HTTP 方法特性");
                continue;
            }

            // 一次性分析方法并缓存结果到 GeneratorContext.MethodAnalysisCache，避免 AnalyzeMethod 被重复调用
            var methodInfo = context.GetOrAnalyzeMethod(
                context.Compilation,
                methodSymbol,
                context.InterfaceDeclaration,
                context.SemanticModel);

            ValidatePathParameters(context, methodSymbol, methodInfo);

            var outcome = GenerateMethodImplementation(codeBuilder, context, methodSymbol, methodInfo, isAbstractClass);
            if (outcome != MethodGenerationOutcome.Generated)
            {
                // 未发射真实实现 → 补发占位成员，保证实现类满足接口契约（否则产生 CS0535）。
                // 占位成员运行期会抛 NotSupportedException，故必须编译期有诊断可见；
                // 该诊断（HTTPCLIENT024）由 EmitContractCompletionStub 在真正发射占位时报告。
                EmitContractCompletionStub(
                    codeBuilder, context, methodSymbol,
                    outcome == MethodGenerationOutcome.SkippedWithoutDiagnostic
                        ? "无法解析为有效的 HTTP 方法，请检查 URL 模板等配置"
                        : "生成器无法为该成员生成 HTTP 调用实现（原因见该成员上的其它诊断）");
            }

            if (HasCacheAttribute(methodSymbol))
            {
                context.HasCache = true;
            }

            if (HasResilienceAttribute(methodSymbol))
            {
                context.HasResilience = true;
            }

            if (HasQueryMapAttribute(methodSymbol) || HasComplexQueryParameter(methodInfo))
            {
                context.HasQueryMap = true;
            }

            // TrackXmlResponseType 已移除：PrecomputeXmlResponseTypes 已在 InterfaceImplementationGenerator 中完成相同工作
        }
    }

    /// <summary>
    /// 获取需要生成的方法列表
    /// - 有 InheritedFrom：获取当前接口及所有非基接口的方法（基接口方法由基类生成）
    /// - 无 InheritedFrom：获取当前接口及所有父接口的方法（需实现全部接口方法）
    /// 函数在哪个接口定义就在哪个生成类中生成，InheritedFrom场景下基接口方法由基类负责
    /// </summary>
    private IEnumerable<IMethodSymbol> GetMethodsToGenerate(GeneratorContext context)
    {
        return context.AllMethods;
    }

    /// <summary>
    /// 方法生成结果。
    /// </summary>
    private enum MethodGenerationOutcome
    {
        /// <summary>已发射真实实现。</summary>
        Generated,

        /// <summary>未发射实现，但该问题已有更具体的诊断说明（本生成器或分析器产出）。</summary>
        SkippedWithDiagnostic,

        /// <summary>未发射实现且无更具体的诊断说明 —— 占位诊断需给出通用原因，避免把编译期错误变成运行期故障。</summary>
        SkippedWithoutDiagnostic,
    }

    /// <summary>
    /// 生成单个方法的实现代码。
    /// </summary>
    /// <returns>
    /// 生成结果。<see cref="MethodGenerationOutcome.Generated"/> 表示已发射该方法成员；
    /// 其余取值表示未发射，调用方将按 <see cref="EmitContractCompletionStub"/> 补发契约占位实现
    /// （该补发内部对 <c>[IgnoreGenerator]</c> 与使用方已手写的方法自动让路）。
    /// </returns>
    private MethodGenerationOutcome GenerateMethodImplementation(StringBuilder codeBuilder, GeneratorContext context, IMethodSymbol methodSymbol, MethodAnalysisResult methodInfo, bool isVirtual = false)
    {
        if (!methodInfo.IsValid)
            return MethodGenerationOutcome.SkippedWithoutDiagnostic;

        // [F14 修复] 参数修饰符校验：存在 ref/out/in/params/指针参数时报告 HTTPCLIENT004（Error）
        // 并跳过该方法体生成，避免产出 CS0177/CS0269 等不可编译代码。
        var unsupportedParameter = methodInfo.Parameters.FirstOrDefault(p => p.UnsupportedReason != null);
        if (unsupportedParameter != null)
        {
            var methodSyntax = GetMethodSyntax(methodSymbol, context);
            context.ProductionContext.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientApiParameterError,
                methodSyntax?.GetLocation() ?? context.InterfaceDeclaration.GetLocation(),
                context.InterfaceDeclaration.Identifier.Text,
                $"{methodSymbol.Name}: {unsupportedParameter.UnsupportedReason}"));
            return MethodGenerationOutcome.SkippedWithDiagnostic;
        }

        if (!string.IsNullOrEmpty(methodInfo.UrlTemplate) &&
            !CSharpCodeValidator.IsValidUrlTemplate(methodInfo.UrlTemplate, out var urlError))
        {
            // [Phase1 修复 1.1/1.3] 诊断 Location 定位到 HTTP 方法特性的 URL 参数位置，
            // 使 CodeFix 能通过 FindToken(span.Start).Parent?.FirstAncestorOrSelf<AttributeSyntax>() 命中。
            var urlTemplateLocation = GetHttpMethodAttributeArgumentLocation(methodSymbol, context)
                ?? context.InterfaceDeclaration.GetLocation();

            context.ProductionContext.ReportDiagnostic(
                Diagnostic.Create(
                    Diagnostics.HttpClientInvalidUrlTemplate,
                    urlTemplateLocation,
                    context.InterfaceDeclaration.Identifier.Text,
                    methodInfo.UrlTemplate,
                    urlError));
            return MethodGenerationOutcome.SkippedWithDiagnostic;
        }

        if (methodInfo.IgnoreGenerator)
            return MethodGenerationOutcome.SkippedWithDiagnostic;

        if (!ValidateHttpClientCompatibility(context, methodInfo))
            return MethodGenerationOutcome.SkippedWithDiagnostic;

        if (methodInfo.CacheEnabled && TypeSymbolHelper.IsResponseType(
                methodInfo.IsAsyncMethod ? methodInfo.AsyncInnerReturnType : methodInfo.ReturnType))
        {
            var methodSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var location = methodSyntax?.GetLocation() ?? context.InterfaceDeclaration.GetLocation();
            context.ProductionContext.ReportDiagnostic(
                Diagnostic.Create(
                    Diagnostics.CacheWithResponseTypeWarning,
                    location,
                    context.InterfaceSymbol.Name,
                    methodSymbol.Name));
        }

        // R3：直达返回（HttpResponseMessage / Stream）绕过 _executor，Cache/Resilience 编排不会生效。
        // 与 HttpResponseMessage 的既有口径一致（用户选择直达返回即表明自管后续逻辑），
        // 但该"配置静默失效"必须编译期可见，否则用户会误以为 [Cache]/[Retry] 已生效。
        if ((methodInfo.CacheEnabled || methodInfo.RetryEnabled ||
             methodInfo.CircuitBreakerEnabled || methodInfo.MethodTimeoutEnabled) &&
            IsDirectReturnType(methodInfo))
        {
            var directReturnSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var directReturnLocation = directReturnSyntax?.GetLocation() ?? context.InterfaceDeclaration.GetLocation();
            context.ProductionContext.ReportDiagnostic(
                Diagnostic.Create(
                    Diagnostics.CacheWithDirectReturnTypeWarning,
                    directReturnLocation,
                    context.InterfaceSymbol.Name,
                    methodSymbol.Name,
                    methodInfo.ReturnType));
        }

        var hasTokenManager = !string.IsNullOrEmpty(context.Configuration.TokenManager);
        var hasHttpClient = !string.IsNullOrEmpty(context.Configuration.HttpClient);
        var needsTokenInjection = ShouldInjectToken(methodInfo, hasTokenManager, hasHttpClient);

        // P3.3（TK-18）：Path / HmacSignature 注入模式不被令牌恢复执行器支持，编译期以 Warning 提示。
        if (needsTokenInjection &&
            (methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModePath ||
             methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeHmacSignature))
        {
            var methodSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var location = methodSyntax?.GetLocation() ?? context.InterfaceDeclaration.GetLocation();
            context.ProductionContext.ReportDiagnostic(
                Diagnostic.Create(
                    Diagnostics.TokenRecoveryUnsupportedInjectionMode,
                    location,
                    context.InterfaceSymbol.Name,
                    methodSymbol.Name,
                    methodInfo.EffectiveTokenInjectionMode));
        }

        // 返回类型形态门控：生成器只为异步形态（Task/ValueTask/Task<T>/ValueTask<T>/IAsyncEnumerable<T>）
        // 发射 async 方法体，而方法体一律含 await。裸返回类型（byte[]/Stream/HttpResponseMessage/Response<T>/
        // string/void 等）会产出「非 async 方法体内含 await」的不可编译代码（实测 CS4032）。
        // 故此处不再发射方法体，改由调用方补发契约占位实现（抛 NotSupportedException）。
        // 诊断由 MUD002（Error，与生成器共用 ReturnTypeSupport.IsSupported 判定）给出，
        // 不在此重复报告 —— 见 ContractPlaceholder 的「避免重复报告」约定。
        if (!ReturnTypeSupport.IsSupported(methodSymbol.ReturnType))
            return MethodGenerationOutcome.SkippedWithDiagnostic;

        codeBuilder.AppendLine();
        codeBuilder.AppendLine($"        /// <summary>");
        codeBuilder.AppendLine($"        /// <inheritdoc />");
        codeBuilder.AppendLine($"        /// </summary>");
        codeBuilder.AppendLine($"        {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
        var asyncKeyword = (methodInfo.IsAsyncMethod || methodInfo.IsAsyncEnumerableReturn) ? "async " : "";
        var virtualKeyword = isVirtual ? "virtual " : "";
        var returnTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        var returnType = methodSymbol.ReturnType.ToDisplayString(returnTypeFormat);
        // [AOT v4 Phase 19.2 / D6/D14] 方法级 [UnconditionalSuppressMessage] 替代原类级压制：
        // 生成代码经执行器间接 JSON 序列化，通过注入的 IHttpContentSerializer（其 options 含消费方 JsonSerializerContext resolver）保证 AOT 安全，
        // AOT 分析器无法静态追踪 DI 数据流，对 IL2026/IL3050 产生已知误报。
        // 仅覆盖经执行器间接 JSON 序列化的生成方法（IAsyncEnumerable / byte[]+Cache/Resilience /
        // IsResponseType / 通用 ExecuteAsync< T >）；void / 文件下载 / byte[] 直下等未传 options 的路径无需压制。
        // 防御性：对所有生成方法统一注入（#if NET6_0_OR_GREATER 仅 AOT/trimming TFM 生效，其余 TFM 无害）。
        WriteMethodLevelSuppressMessage(codeBuilder);

        codeBuilder.AppendLine($"        public {virtualKeyword}{asyncKeyword}{returnType} {methodSymbol.Name}({ParameterSignatureBuilder.Build(methodSymbol)})");
        codeBuilder.AppendLine("        {");

        ParameterValidationHelper.GenerateParameterValidation(codeBuilder, methodInfo.Parameters);

        // 捕获应用上下文到局部变量，避免在异步执行过程中 _appContextHolder.Current 被其他线程修改导致 TOCTOU 竞态
        if (!hasHttpClient)
        {
            // TokenManager 模式：_appContextHolder.Current 为 null 时回退到默认应用，与 GetTokenAsync 中的回退逻辑保持一致。
            // Default 模式（无 TokenManager）：Current 由 UseApp/UseDefaultApp/SwitchTo 显式设置，不再由构造函数初始化。
            // 保留 throw 作为安全网，防止异常状态下静默使用错误上下文。
            if (hasTokenManager)
            {
                codeBuilder.AppendLine("            var __appContext = _appContextHolder.Current ?? _tokenManager.GetDefaultApp();");
            }
            else
            {
                codeBuilder.AppendLine("            var __appContext = _appContextHolder.Current ?? throw new InvalidOperationException(\"无法找到当前服务的应用上下文。\");");
            }
        }

        // 执行器统一使用 DI 注入的 _executor 字段（所有模式均通过构造函数注入，无状态设计）
        var executor = "_executor";
        // HttpClient 表达式：HttpClient 模式使用字段，TokenManager/AppContext 模式使用已捕获的应用上下文
        var httpClientExpr = hasHttpClient ? "_httpClient" : "__appContext.HttpClient";

        if (needsTokenInjection)
        {
            var injectionMode = methodInfo.EffectiveTokenInjectionMode;
            var tokenParamName = methodInfo.TokenParameterName;
            var tokenParamHasHeader = methodInfo.Parameters
                .FirstOrDefault(p => p.Name == tokenParamName)?
                .Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.HeaderAttribute) == true;

            var tokenManagerKey = TokenMethodHelper.GetMethodTokenManagerKey(context, methodInfo);
            var requiresUserId = TokenMethodHelper.MethodRequiresUserId(context, methodInfo);
            var effectiveScopes = methodInfo.MethodTokenScopes ?? methodInfo.InterfaceTokenScopes;
            var scopes = TokenHelper.ParseScopes(effectiveScopes);

            // [Phase2 修复 1.8] 对 scopes 元素转义，防止含 " \ 等特殊字符产出非法 C#。
            var scopesArg = scopes.Length > 0
                ? $"new[] {{ {string.Join(", ", scopes.Select(s => $"\"{StringEscapeHelper.EscapeString(s)}\""))} }}"
                : "null";
            var userIdArg = requiresUserId ? "_currentUserContext.UserId" : "null";

            if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeApiKey)
            {
                var apiKeyName = methodInfo.InterfaceTokenName;
                if (!string.IsNullOrEmpty(apiKeyName))
                    // [Phase2 修复 1.8] 对 apiKeyName 转义。
                    codeBuilder.AppendLine($"            var access_token = await GetApiKeyAsync(\"{StringEscapeHelper.EscapeString(apiKeyName!)}\").ConfigureAwait(false);");
                else
                    codeBuilder.AppendLine($"            var access_token = await GetApiKeyAsync().ConfigureAwait(false);");
            }
            else if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeHmacSignature)
            {
                codeBuilder.AppendLine($"            await ApplyHmacSignatureAsync(__httpRequest).ConfigureAwait(false);");
            }
            else if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeBasicAuth)
            {
                // [Phase2 修复 1.8] 对 tokenManagerKey 转义，同 TokenMethodHelper.cs:37 已有做法。
                var escapedTokenManagerKey = StringEscapeHelper.EscapeString(tokenManagerKey);
                if (!string.IsNullOrEmpty(tokenParamName) && !tokenParamHasHeader)
                {
                    codeBuilder.AppendLine($"            var access_token = !string.IsNullOrWhiteSpace({tokenParamName}) ? {tokenParamName} : await GetTokenAsync(\"{escapedTokenManagerKey}\", {userIdArg}, {scopesArg}).ConfigureAwait(false);");
                }
                else
                {
                    codeBuilder.AppendLine($"            var access_token = await GetTokenAsync(\"{escapedTokenManagerKey}\", {userIdArg}, {scopesArg}).ConfigureAwait(false);");
                }
                codeBuilder.AppendLine($"            var __basicCredentials = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(access_token));");
            }
            else
            {
                // [Phase2 修复 1.8] 对 tokenManagerKey 转义，同 TokenMethodHelper.cs:37 已有做法。
                var escapedTokenManagerKey = StringEscapeHelper.EscapeString(tokenManagerKey);
                if (!string.IsNullOrEmpty(tokenParamName) && !tokenParamHasHeader)
                {
                    codeBuilder.AppendLine($"            var access_token = !string.IsNullOrWhiteSpace({tokenParamName}) ? {tokenParamName} : await GetTokenAsync(\"{escapedTokenManagerKey}\", {userIdArg}, {scopesArg}).ConfigureAwait(false);");
                }
                else
                {
                    codeBuilder.AppendLine($"            var access_token = await GetTokenAsync(\"{escapedTokenManagerKey}\", {userIdArg}, {scopesArg}).ConfigureAwait(false);");
                }
            }
        }

        codeBuilder.AppendLine();

        // 统一调用执行器：Cache/Resilience 编排由运行时执行器处理，消除生成器中的三分支互斥逻辑
        GenerateExecutorCall(codeBuilder, context, methodInfo, hasHttpClient, needsTokenInjection, executor, httpClientExpr);

        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

        return MethodGenerationOutcome.Generated;
    }

    /// <summary>
    /// 为「生成器无法实现」的接口方法发射契约占位实现；<c>[IgnoreGenerator]</c> 方法与使用方已手写实现的方法除外。
    /// </summary>
    /// <param name="codeBuilder">代码缓冲区。</param>
    /// <param name="context">生成上下文。</param>
    /// <param name="methodSymbol">未生成实现的方法。</param>
    /// <param name="reason">占位原因（写入 HTTPCLIENT024 诊断消息）。</param>
    /// <remarks>
    /// <para>
    /// <c>[IgnoreGenerator]</c>（接口级/方法级）语义为「生成器完全跳过、由使用方自行实现」，
    /// 此时发射占位成员会与使用方的实现冲突，故必须保持不发射。
    /// </para>
    /// <para>
    /// 同理，使用方在 partial 实现类中手写该方法（占位实现落地前的可用写法）时也必须让路，
    /// 否则构成重复定义（CS0111）。见 <see cref="ContractPlaceholder.IsImplementedByUser"/>。
    /// </para>
    /// <para>
    /// 真正发射占位时同步报告 <c>HTTPCLIENT024</c>：占位成员运行期必抛异常，
    /// 故编译期必须始终可见 —— 不能依赖「该成员上的其它诊断」兜底，
    /// 因为其中的分析器诊断（MUD001/MUD002）在生成器报出「Error + NotConfigurable」诊断时会整体消失。
    /// </para>
    /// </remarks>
    private static void EmitContractCompletionStub(
        StringBuilder codeBuilder, GeneratorContext context, IMethodSymbol methodSymbol, string reason)
    {
        if (GeneratorAttributeFilters.HasIgnoreGenerator(methodSymbol))
            return;

        // 其它片段生成器（AppContext / 令牌辅助等）已按模式无条件发射同名成员时，不得重复发射。
        // 该登记表只含实例成员，故仅对实例方法生效。
        if (!methodSymbol.IsStatic && context.ProvidedMemberNames.Contains(methodSymbol.Name))
            return;

        if (ContractPlaceholder.IsImplementedByUser(context, methodSymbol))
            return;

        ContractPlaceholder.ReportUnsupportedMember(context, methodSymbol, reason);
        EmitUnsupportedMethodStub(codeBuilder, methodSymbol);
    }

    /// <summary>
    /// 发射「不受支持的方法」占位实现：成员体直接抛 <see cref="System.NotSupportedException"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>背景（缺陷修复）</b>：此前对无法生成的方法直接跳过（不发射成员），生成的实现类因而缺失接口成员
    /// → 编译报 <c>CS0535</c>。该错误既未说明根因，又会掩盖同编译中真正的诊断
    /// （典型：MUD001「缺少 HTTP 方法特性」—— 它本应是最直接的提示）。
    /// </para>
    /// <para>
    /// <b>修复方式</b>：始终发射占位成员，使实现类满足接口契约（不再产生 CS0535），
    /// 让 MUD001/MUD002/HTTPCLIENT004/005 等诊断正常呈现；若该成员在运行期被调用，
    /// 会以明确消息快速失败，而非产生难以定位的编译错误。
    /// </para>
    /// <para>
    /// <b>签名保真</b>：占位成员必须与接口签名逐项一致，否则编译器报 <c>CS0535</c>（更糟：报 <c>CS8767</c> 之类的隐式实现不匹配）。
    /// 因此需按需补齐修饰符：
    /// <list type="bullet">
    ///   <item><c>unsafe</c> —— 指针/函数指针签名（如 <c>Task&lt;string&gt; M(int* p)</c>），
    ///         缺失会报「指针不得在安全上下文中使用」；</item>
    ///   <item><c>static</c> —— 接口静态抽象成员（C# 11+，<c>static abstract</c>）由实现类的<b>静态</b>成员满足，
    ///         缺失会持续报 CS0535；</item>
    ///   <item><c>ref</c>/<c>ref readonly</c> 返回 —— <c>throw</c> 表达式不能作为 ref 返回值，
    ///         须改用语句体 <c>{ throw ...; }</c>。</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static void EmitUnsupportedMethodStub(StringBuilder codeBuilder, IMethodSymbol methodSymbol)
    {
        var typeFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        var isStatic = methodSymbol.IsStatic;
        var returnsByRefReadonly = methodSymbol.RefKind == RefKind.RefReadOnly;
        var returnsByRef = methodSymbol.RefKind != RefKind.None;
        var needsUnsafe = ContractPlaceholder.RequiresUnsafeContext(methodSymbol.ReturnType) ||
            methodSymbol.Parameters.Any(p => ContractPlaceholder.RequiresUnsafeContext(p.Type));

        var returnType = (returnsByRefReadonly ? "ref readonly " : returnsByRef ? "ref " : string.Empty)
            + methodSymbol.ReturnType.ToDisplayString(typeFormat);
        var typeParameters = methodSymbol.TypeParameters.Length == 0
            ? string.Empty
            : $"<{string.Join(", ", methodSymbol.TypeParameters.Select(tp => tp.Name))}>";
        var modifiers = (isStatic ? "static " : string.Empty)
            + (needsUnsafe ? "unsafe " : string.Empty);
        // 消息携带诊断 ID（HTTPCLIENT024）：占位成员运行期才暴露，线上日志需能直接关联规则与文档
        // （否则只有一句"未生成实现"，无法判断是"缺 HTTP 方法特性"还是"返回类型不受支持"）。
        var message =
            "HTTPCLIENT024: 接口成员未生成实现（占位实现），运行期不可用。" +
            $"方法 '{methodSymbol.Name}' 未生成 HTTP 调用实现：请检查接口方法的 HTTP 方法特性（[Get]/[Post] 等）、" +
            "参数修饰符、返回类型形态、URL 模板与 HttpClient 类型配置（详见编译诊断），" +
            "或为该方法标注 [IgnoreGenerator] 自行实现。";

        codeBuilder.AppendLine();
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// <inheritdoc />");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <remarks>");
        codeBuilder.AppendLine("        /// 占位实现：生成器无法为该接口方法生成 HTTP 调用实现。");
        codeBuilder.AppendLine("        /// 保留此成员以保证实现类满足接口契约（避免 CS0535 掩盖真正的编译诊断）。");
        codeBuilder.AppendLine("        /// 修复对应诊断后重新生成；若确需自行实现，请标注 [IgnoreGenerator]。");
        codeBuilder.AppendLine("        /// </remarks>");
        codeBuilder.AppendLine($"        {GeneratedCodeConsts.HttpGeneratedCodeAttribute}");
        codeBuilder.AppendLine($"        public {modifiers}{returnType} {methodSymbol.Name}{typeParameters}({ParameterSignatureBuilder.Build(methodSymbol)})");

        if (returnsByRef)
        {
            // ref 返回无法用 throw 表达式，改用语句体。
            codeBuilder.AppendLine("        {");
            codeBuilder.AppendLine($"            throw new global::System.NotSupportedException(\"{StringEscapeHelper.EscapeString(message)}\");");
            codeBuilder.AppendLine("        }");
        }
        else
        {
            codeBuilder.AppendLine($"            => throw new global::System.NotSupportedException(\"{StringEscapeHelper.EscapeString(message)}\");");
        }
    }

    /// <summary>
    /// 统一生成执行器调用代码：构建请求 + 构造 ExecutionDescriptor + 调用执行器。
    /// Cache/Resilience 的编排逻辑由运行时执行器（DefaultHttpRequestExecutor）处理。
    /// IAsyncEnumerable/byte[]/文件下载等特殊返回类型跳过编排，直接调用对应的执行器方法。
    /// </summary>
    private void GenerateExecutorCall(StringBuilder codeBuilder, GeneratorContext context,
        MethodAnalysisResult methodInfo, bool hasHttpClient, bool needsTokenInjection, string executor, string httpClientExpr)
    {
        var basePath = context.Configuration.BasePath;
        var urlCode = _requestBuilder.BuildUrlString(methodInfo, basePath);
        codeBuilder.AppendLine($"            {urlCode.TrimStart()}");

        _requestBuilder.GenerateQueryParameters(codeBuilder, methodInfo);
        _requestBuilder.GenerateRequestSetup(codeBuilder, methodInfo);
        _requestBuilder.GenerateHeaderParameters(codeBuilder, methodInfo);

        // 生成接口属性级 Header（动态值，运行时由属性提供）
        var hasTokenManager = !string.IsNullOrEmpty(context.Configuration.TokenManager);
        _requestBuilder.GenerateInterfaceHeaderProperties(codeBuilder, methodInfo, hasTokenManager);

        codeBuilder.AppendLine();
        _requestBuilder.GenerateBodyParameter(codeBuilder, methodInfo, hasHttpClient);

        GenerateTokenInjection(codeBuilder, context, methodInfo, needsTokenInjection, "            ");

        if (methodInfo.InterfaceHeaderAttributes?.Any() == true)
            GenerateInterfaceHeaders(codeBuilder, context, methodInfo);

        var cancellationTokenArg = GetCancellationTokenParams(methodInfo);
        var deserializeType = methodInfo.IsAsyncMethod ? methodInfo.AsyncInnerReturnType : methodInfo.ReturnType;

        // IAsyncEnumerable — 直接调用执行器流式方法（不经过 Cache/Resilience，与当前行为一致）
        // AOT 安全说明：生成代码经执行器间接 JSON 序列化，通过注入的 IHttpContentSerializer（其 options 含消费方 JsonSerializerContext resolver）保证 AOT 安全，
        // IL2026/IL3050 误报已由方法级 [UnconditionalSuppressMessage] 压制（见 WriteMethodLevelSuppressMessage）。
        // 消费方须确保 T（elementType）已在 JsonSerializerContext 中声明，否则 AOT 下反序列化返回 default。
        //
        // [v4 Phase 1] AOT 安全重载链路已就绪：
        //   - IBaseHttpClient.SendAsAsyncEnumerable<T>(HttpRequestMessage, JsonTypeInfo<T>, CT)  [NET8+]
        //   - IHttpRequestExecutor.SendAsAsyncEnumerable<T>(HttpRequestMessage, IBaseHttpClient, JsonTypeInfo<T>, CT)  [NET8+]
        //   - EnhancedHttpClient.SendAsAsyncEnumerable<T>(HttpRequestMessage, JsonTypeInfo<T>, CT)  [NET8+]
        //   - ResilientHttpClient.SendAsAsyncEnumerable<T>(HttpRequestMessage, JsonTypeInfo<T>, CT)  [NET8+]
        //   - AsyncEnumerableExtensions.SendAsAsyncEnumerable<T>(IBaseHttpClient, HttpRequestMessage, JsonTypeInfo<T>, CT)  [NET8+]
        // 源生成器无法自动注入消费方的 JsonSerializerContext 实例（生成器不可见消费方类型），
        // 故生成代码仍走 jsonSerializerOptions=null 路径。消费方可手动调用上述 AOT 安全重载。
        if (methodInfo.IsAsyncEnumerableReturn && !string.IsNullOrEmpty(methodInfo.AsyncEnumerableElementType))
        {
            var elementType = methodInfo.AsyncEnumerableElementType;
            var cancellationTokenParam = methodInfo.Parameters
                .FirstOrDefault(p => TypeDetectionHelper.IsCancellationToken(p.Type));
            var cancellationTokenName = cancellationTokenParam?.Name ?? "default";
            codeBuilder.AppendLine($"            await foreach (var __item in {executor}.SendAsAsyncEnumerable<{elementType}>(__httpRequest, {httpClientExpr}, null, {cancellationTokenName}))");
            codeBuilder.AppendLine("            {");
            codeBuilder.AppendLine("                yield return __item;");
            codeBuilder.AppendLine("            }");
            return;
        }

        // 文件路径下载 — 直接调用 DownloadLargeAsync（大文件下载不经过 Cache/Resilience）
        var filePathParam = methodInfo.Parameters
            .FirstOrDefault(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.FilePathAttribute));
        if (filePathParam != null)
        {
            // 从 [FilePath(BufferSize = ..., Overwrite = ...)] 读取配置
            var filePathAttr = filePathParam.Attributes.First(a => a.Name == HttpClientGeneratorConstants.FilePathAttribute);
            var bufferSize = 81920;
            if (filePathAttr.NamedArguments.TryGetValue("BufferSize", out var bsVal) && bsVal is int bs && bs > 0)
                bufferSize = bs;

            var overwrite = true;
            if (filePathAttr.NamedArguments.TryGetValue("Overwrite", out var owVal) && owVal is bool ow)
                overwrite = ow;

            // 检测方法签名中的 IProgress<T> 参数，用于下载进度报告
            var progressParam = methodInfo.Parameters
                .FirstOrDefault(p => TypeDetectionHelper.IsIProgressType(p.Type, out _));
            var progressArg = progressParam != null ? progressParam.Name : "null";

            // 构造 ResponseDescriptor 以支持 AllowAnyStatusCode
            codeBuilder.AppendLine($"            await {executor}.DownloadLargeAsync(__httpRequest, {httpClientExpr}, {filePathParam.Name}, {overwrite.ToString().ToLowerInvariant()}, {bufferSize},");
            codeBuilder.Append("                ");
            WriteResponseDescriptorCode(codeBuilder, methodInfo, deserializeType, indent: "                ");
            codeBuilder.AppendLine($", progress: {progressArg}{cancellationTokenArg}).ConfigureAwait(false);");
            return;
        }

        // byte[] 下载
        if (TypeDetectionHelper.IsByteArrayType(deserializeType))
        {
            var hasCacheOrResilience = methodInfo.CacheEnabled ||
                methodInfo.RetryEnabled || methodInfo.CircuitBreakerEnabled || methodInfo.MethodTimeoutEnabled;

            // 判断返回类型是否为可空 byte[]?（非可空时使用 ?? Array.Empty<byte>() 确保非空返回）
            // [Phase4 修复 5.1] 显式 StringComparison.Ordinal（CA1310）：判断的是 C# 类型文本结尾，
            // 区域敏感比较可能改变结果。
            var isNullableByteArray = deserializeType.TrimEnd().EndsWith("?", StringComparison.Ordinal);

            if (hasCacheOrResilience)
            {
                // 启用 Cache/Resilience 时通过 ExecuteAsync 编排（执行器内部对 byte[] 使用 DownloadAsync 而非反序列化）
                if (isNullableByteArray)
                {
                    codeBuilder.AppendLine($"            return await {executor}.ExecuteAsync<{deserializeType}>(");
                    codeBuilder.AppendLine("                __httpRequest,");
                    codeBuilder.AppendLine($"                {httpClientExpr},");
                    codeBuilder.Append("                ");
                    WriteExecutionDescriptorCode(codeBuilder, context, methodInfo, deserializeType, indent: "                ");
                    codeBuilder.AppendLine(",");
                    codeBuilder.AppendLine($"                null{cancellationTokenArg}).ConfigureAwait(false);");
                }
                else
                {
                    codeBuilder.AppendLine($"            return (await {executor}.ExecuteAsync<{deserializeType}>(");
                    codeBuilder.AppendLine("                __httpRequest,");
                    codeBuilder.AppendLine($"                {httpClientExpr},");
                    codeBuilder.Append("                ");
                    WriteExecutionDescriptorCode(codeBuilder, context, methodInfo, deserializeType, indent: "                ");
                    codeBuilder.AppendLine(",");
                    codeBuilder.AppendLine($"                null{cancellationTokenArg}).ConfigureAwait(false)) ?? System.Array.Empty<byte>();");
                }
            }
            else
            {
                // 未启用 Cache/Resilience 时直接调用 DownloadAsync，传递 ResponseDescriptor 以支持 AllowAnyStatusCode
                if (isNullableByteArray)
                {
                    codeBuilder.AppendLine($"            return await {executor}.DownloadAsync(__httpRequest, {httpClientExpr},");
                    codeBuilder.Append("                ");
                    WriteResponseDescriptorCode(codeBuilder, methodInfo, deserializeType, indent: "                ");
                    codeBuilder.AppendLine($"{cancellationTokenArg}).ConfigureAwait(false);");
                }
                else
                {
                    codeBuilder.AppendLine($"            return (await {executor}.DownloadAsync(__httpRequest, {httpClientExpr},");
                    codeBuilder.Append("                ");
                    WriteResponseDescriptorCode(codeBuilder, methodInfo, deserializeType, indent: "                ");
                    codeBuilder.AppendLine($"{cancellationTokenArg}).ConfigureAwait(false)) ?? System.Array.Empty<byte>();");
                }
            }
            return;
        }

        // [v2.4 §2.4] HttpResponseMessage 直达返回 — 架构红线例外
        // 绕过 _executor，直接调用 IBaseHttpClient.SendRawAsync。
        // 理由：用户选择 HttpResponseMessage 返回即表明自管错误处理/反序列化/缓存/弹性等全部后续逻辑。
        if (IsHttpResponseMessageType(deserializeType))
        {
            codeBuilder.AppendLine($"            return await {httpClientExpr}.SendRawAsync(__httpRequest{cancellationTokenArg}).ConfigureAwait(false);");
            return;
        }

        // [v2.4 §2.4 同构] Stream 直达返回 —— 与 HttpResponseMessage 同属"用户自管"例外：
        // 用户选择 Stream 返回即表明自管读取/释放；不支持 Cache/Resilience/Response<T> 包装
        // （组合会由 HTTPCLIENT025 在编译期提示）。走 IBaseHttpClient.SendStreamAsync（响应流所有权归调用方）。
        //
        // 修复背景：此前 Task<Stream> 落入下方通用分支，生成
        // `return await _executor.ExecuteAsync<System.IO.Stream>(...)` —— 编译通过（有 async + await），
        // 但执行器会把响应体按 JSON 反序列化为 Stream，运行期必然失败；
        // 而 MUD002 与 README 均把 Stream 列为受支持 ⇒「分析器沉默 + 生成语义错误的代码」的伪支持。
        if (IsStreamType(deserializeType))
        {
            codeBuilder.AppendLine($"            return await {httpClientExpr}.SendStreamAsync(__httpRequest{cancellationTokenArg}).ConfigureAwait(false);");
            return;
        }

        // void 返回 — 使用非泛型 ExecuteAsync（支持 Cache/Resilience 编排）
        if (IsVoidInnerReturnType(deserializeType))
        {
            codeBuilder.AppendLine($"            await {executor}.ExecuteAsync(");
            codeBuilder.AppendLine("                __httpRequest,");
            codeBuilder.AppendLine($"                {httpClientExpr},");
            codeBuilder.Append("                ");
            WriteExecutionDescriptorCode(codeBuilder, context, methodInfo, deserializeType, indent: "                ");
            codeBuilder.AppendLine($"{cancellationTokenArg}).ConfigureAwait(false);");
            return;
        }

        if (IsResponseType(deserializeType, out var innerType))
        {
            codeBuilder.AppendLine($"            return await {executor}.ExecuteAsResponseAsync<{innerType}>(");
            codeBuilder.AppendLine("                __httpRequest,");
            codeBuilder.AppendLine($"                {httpClientExpr},");
            codeBuilder.Append("                ");
            WriteExecutionDescriptorCode(codeBuilder, context, methodInfo, deserializeType, indent: "                ");
            codeBuilder.AppendLine(",");
            codeBuilder.AppendLine($"                null{cancellationTokenArg}).ConfigureAwait(false);");
        }
        else
        {
            codeBuilder.AppendLine($"            return await {executor}.ExecuteAsync<{deserializeType}>(");
            codeBuilder.AppendLine("                __httpRequest,");
            codeBuilder.AppendLine($"                {httpClientExpr},");
            codeBuilder.Append("                ");
            WriteExecutionDescriptorCode(codeBuilder, context, methodInfo, deserializeType, indent: "                ");
            codeBuilder.AppendLine(",");
            codeBuilder.AppendLine($"                null{cancellationTokenArg}).ConfigureAwait(false);");
        }
    }

    /// <summary>
    /// 写入方法级 [UnconditionalSuppressMessage]，压制生成代码中经执行器间接 JSON 序列化的
    /// IL2026/IL3050 误报。仅在有 UnconditionalSuppressMessageAttribute 的 TFM（NET6_0_OR_GREATER）上生成。
    /// </summary>
    /// <remarks>
    /// 原类级压制（ClassStructureGenerator）已移除，改为方法级精准覆盖所有经执行器间接 JSON 序列化的生成方法
    /// （[审查修订 D6/D14]：响应反序列化 + IAsyncEnumerable 流式 + byte[] 下载带 Cache/Resilience 路径）。
    /// </remarks>
    private static void WriteMethodLevelSuppressMessage(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("#if NET6_0_OR_GREATER");
        codeBuilder.AppendLine("        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"ReflectionAnalysis\", \"IL2026\",");
        codeBuilder.AppendLine("            Justification = \"生成的 JSON 序列化/反序列化通过注入的 IHttpContentSerializer（其 options 含消费方 JsonSerializerContext resolver）保证 AOT 安全，T 的类型元数据已由消费方的 JsonSerializerContext 保留.\")]");
        codeBuilder.AppendLine("        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"AotAnalysis\", \"IL3050\",");
        codeBuilder.AppendLine("            Justification = \"生成的 JSON 序列化/反序列化通过注入的 IHttpContentSerializer（其 options 含消费方 JsonSerializerContext resolver）保证 AOT 安全，T 的类型元数据已由消费方的 JsonSerializerContext 保留.\")]");
        codeBuilder.AppendLine("#endif");
    }

    /// <summary>
    /// 将 ResponseDescriptor 代码直接写入 <paramref name="sb"/>（用于下载场景，不包含 Cache/Resilience 配置）。
    /// </summary>
    private void WriteResponseDescriptorCode(StringBuilder sb, MethodAnalysisResult methodInfo, string deserializeType, string indent)
    {
        sb.AppendLine("new ResponseDescriptor");
        sb.AppendLine("            {");

        var isResponseType = IsResponseType(deserializeType, out var responseInnerType);
        sb.AppendLine($"                AllowAnyStatusCode = {methodInfo.AllowAnyStatusCode.ToString().ToLowerInvariant()},");
        sb.AppendLine($"                IsResponseType = {isResponseType.ToString().ToLowerInvariant()},");
        // [F13 修复] 在写入点转义 ResponseContentType 字面量。
        sb.AppendLine($"                ResponseContentType = \"{StringEscapeHelper.EscapeString(methodInfo.ResponseContentType ?? "")}\",");
        sb.AppendLine($"                EnableDecrypt = {methodInfo.ResponseEnableDecrypt.ToString().ToLowerInvariant()},");

        var isVoid = IsVoidInnerReturnType(deserializeType);
        sb.AppendLine($"                IsVoidReturn = {isVoid.ToString().ToLowerInvariant()},");

        // XML 序列化器引用
        var isXml = ContentTypeHelper.IsXmlContentType(methodInfo.ResponseContentType);
        if (isXml && !isVoid)
        {
            var xmlTargetType = isResponseType ? responseInnerType : deserializeType;
            var xmlFieldRef = RequestBuilder.GetXmlSerializerFieldReference(xmlTargetType);
            sb.AppendLine($"                XmlSerializer = {xmlFieldRef},");
        }

        sb.Append("            }");
    }

    /// <summary>
    /// 将 ExecutionDescriptor 代码直接写入 <paramref name="sb"/>，包含 ResponseDescriptor、CacheOptions、ResilienceExecutionOptions 和 CacheKey。
    /// 当方法未启用 Cache/Resilience 时，对应字段为 null，执行器走直接执行路径。
    /// </summary>
    /// <param name="sb">代码缓冲区。</param>
    /// <param name="context">生成上下文。</param>
    /// <param name="methodInfo">方法分析结果（缓存/弹性/响应描述来源）。</param>
    /// <param name="deserializeType">反序列化目标类型文本。</param>
    /// <param name="indent">每行前缀缩进（与调用点的代码缩进对齐）。</param>
    private void WriteExecutionDescriptorCode(StringBuilder sb, GeneratorContext context, MethodAnalysisResult methodInfo, string deserializeType, string indent)
    {
        sb.AppendLine("new ExecutionDescriptor");
        sb.AppendLine("               {");
        sb.AppendLine("                   Response = new ResponseDescriptor");
        sb.AppendLine("                   {");

        var isResponseType = IsResponseType(deserializeType, out var responseInnerType);
        sb.AppendLine($"                       AllowAnyStatusCode = {methodInfo.AllowAnyStatusCode.ToString().ToLowerInvariant()},");
        sb.AppendLine($"                       IsResponseType = {isResponseType.ToString().ToLowerInvariant()},");
        // [F13 修复] 在写入点转义 ResponseContentType 字面量（用户可配置，含 " \ 时直拼产出非法 C#）。
        sb.AppendLine($"                       ResponseContentType = \"{StringEscapeHelper.EscapeString(methodInfo.ResponseContentType ?? "")}\",");
        sb.AppendLine($"                       EnableDecrypt = {methodInfo.ResponseEnableDecrypt.ToString().ToLowerInvariant()},");

        var isVoid = IsVoidInnerReturnType(deserializeType);
        sb.AppendLine($"                       IsVoidReturn = {isVoid.ToString().ToLowerInvariant()},");

        // XML 序列化器引用
        var isXml = ContentTypeHelper.IsXmlContentType(methodInfo.ResponseContentType);
        if (isXml && !isVoid)
        {
            var xmlTargetType = isResponseType ? responseInnerType : deserializeType;
            var xmlFieldRef = RequestBuilder.GetXmlSerializerFieldReference(xmlTargetType);
            sb.AppendLine($"                       XmlSerializer = {xmlFieldRef},");
        }

        sb.AppendLine("                   },");

        // Cache 配置
        if (methodInfo.CacheEnabled)
        {
            var cacheKeyExpression = GenerateCacheKeyExpression(context, methodInfo);
            sb.AppendLine("                   Cache = new CacheOptions");
            sb.AppendLine("                   {");
            sb.AppendLine($"                       DurationSeconds = {methodInfo.CacheDurationSeconds},");
            sb.AppendLine($"                       VaryByUser = {methodInfo.CacheVaryByUser.ToString().ToLowerInvariant()},");
            // 滑动过期语义下沉到 CacheOptions，运行时经 GetOrFetchAsync 透传至缓存层
            sb.AppendLine($"                       UseSlidingExpiration = {methodInfo.CacheUseSlidingExpiration.ToString().ToLowerInvariant()},");
            if (!string.IsNullOrEmpty(methodInfo.CacheKeyTemplate))
                // [F13 修复] 在写入点转义 KeyTemplate 字面量（用户可配置模板，含 " \ 时直拼产出非法 C#）。
                sb.AppendLine($"                       KeyTemplate = \"{StringEscapeHelper.EscapeString(methodInfo.CacheKeyTemplate!)}\",");
            sb.AppendLine("                   },");
            sb.AppendLine($"                       CacheKey = {cacheKeyExpression},");
        }

        // Resilience 配置
        var hasResilience = methodInfo.RetryEnabled || methodInfo.CircuitBreakerEnabled || methodInfo.MethodTimeoutEnabled;
        if (hasResilience)
        {
            sb.AppendLine("                   Resilience = new ResilienceExecutionOptions");
            sb.AppendLine("                   {");
            sb.AppendLine($"                       RetryEnabled = {methodInfo.RetryEnabled.ToString().ToLowerInvariant()},");
            sb.AppendLine($"                       MaxRetries = {methodInfo.RetryMaxRetries},");
            sb.AppendLine($"                       DelayMilliseconds = {methodInfo.RetryDelayMilliseconds},");
            sb.AppendLine($"                       UseExponentialBackoff = {methodInfo.RetryUseExponentialBackoff.ToString().ToLowerInvariant()},");
            sb.AppendLine($"                       CircuitBreakerEnabled = {methodInfo.CircuitBreakerEnabled.ToString().ToLowerInvariant()},");
            sb.AppendLine($"                       FailureThreshold = {methodInfo.CircuitBreakerFailureThreshold},");
            sb.AppendLine($"                       BreakDurationSeconds = {methodInfo.CircuitBreakerBreakDurationSeconds},");
            sb.AppendLine($"                       SamplingDurationSeconds = {methodInfo.CircuitBreakerSamplingDurationSeconds},");
            sb.AppendLine($"                       MinimumThroughput = {methodInfo.CircuitBreakerMinimumThroughput},");
            sb.AppendLine($"                       TimeoutEnabled = {methodInfo.MethodTimeoutEnabled.ToString().ToLowerInvariant()},");
            sb.AppendLine($"                       TimeoutMilliseconds = {methodInfo.MethodTimeoutMilliseconds},");
            sb.AppendLine("                   },");
        }

        sb.Append("               }");
    }

    /// <summary>
    /// 生成缓存键表达式（仅当 Cache 启用时调用）。
    /// </summary>
    private string GenerateCacheKeyExpression(GeneratorContext context, MethodAnalysisResult methodInfo)
    {
        var userIdExpression = context.Configuration.AnyMethodRequiresUserId
            ? "_currentUserContext.UserId"
            : "CurrentUserId";

        var varyPrefix = methodInfo.CacheVaryByUser
            ? $"\"user:\" + ({userIdExpression} ?? \"anonymous\") + \"|\" + "
            : "";

        if (!string.IsNullOrEmpty(methodInfo.CacheKeyTemplate))
        {
            // 将模板中的位置占位符 {0}, {1}, ... 替换为实际参数名
            // 按 [Path] 参数顺序映射，若无 Path 参数则按非 CancellationToken 参数顺序映射
            var orderedParams = methodInfo.Parameters
                .Where(p => p.Attributes.Any(attr => HttpClientGeneratorConstants.PathAttributes.Contains(attr.Name)))
                .ToList();
            if (orderedParams.Count == 0)
            {
                orderedParams = methodInfo.Parameters
                    .Where(p => !TypeDetectionHelper.IsCancellationToken(p.Type))
                    .ToList();
            }

            // 先转义模板字面量部分（\ 和 "），占位符 {0}、{1} 不含这些字符，转义不影响后续替换
            var resolvedTemplate = StringEscapeHelper.EscapeString(methodInfo.CacheKeyTemplate!);
            for (var i = 0; i < orderedParams.Count; i++)
            {
                resolvedTemplate = resolvedTemplate.Replace($"{{{i}}}", $"{{{orderedParams[i].Name}}}");
            }

            return $"{varyPrefix}$\"{resolvedTemplate}\"";
        }

        var keyBuilder = new StringBuilder();
        keyBuilder.Append($"{varyPrefix}$\"{methodInfo.MethodName}");

        foreach (var param in methodInfo.Parameters)
        {
            if (!TypeDetectionHelper.IsCancellationToken(param.Type))
            {
                keyBuilder.Append($"|{{{param.Name}}}");
            }
        }

        keyBuilder.Append('"');
        return keyBuilder.ToString();
    }

    /// <summary>
    /// 判断<b>异步形态的内部返回类型</b>是否为 void（即非泛型 <c>Task</c>/<c>ValueTask</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>注意：此处不要"清理"为字面 void 判定</b>。字面 <c>void</c> 返回类型已被返回类型门禁
    /// （<c>ReturnTypeSupport.IsSupported</c>）拒绝，不会进入方法体生成；本分支服务的是
    /// <c>MethodAnalysisResult.AsyncInnerReturnType</c> —— 非泛型 <c>Task</c>/<c>ValueTask</c>
    /// 经 <c>TypeSymbolHelper.ExtractAsyncInnerType</c> 解析得到的字面 <c>"void"</c>。
    /// 删除或收紧该分支会破坏非泛型 <c>Task</c> 方法的生成（会误走泛型 <c>ExecuteAsync&lt;void&gt;</c>）。
    /// </para>
    /// <para>名称中的 "Inner" 即强调它判断的是异步包装内的返回类型，而非方法签名的返回类型。</para>
    /// </remarks>
    private static bool IsVoidInnerReturnType(string type)
    {
        return type == "void" || type == "System.Void";
    }

    /// <summary>
    /// [v2.4 §2.4] 判断类型是否为 HttpResponseMessage（直达返回路径）。
    /// 支持简写和全限定名。
    /// </summary>
    private static bool IsHttpResponseMessageType(string type)
    {
        return type == "HttpResponseMessage" ||
        type == "System.Net.Http.HttpResponseMessage";
    }

    /// <summary>
    /// [v2.4 §2.4 同构] 判断类型是否为 <c>Stream</c>（直达返回路径，走 SendStreamAsync）。
    /// 支持简写、全限定名与可空注记（<c>Stream?</c>）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="IsHttpResponseMessageType"/> 同构；额外容忍 <c>?</c> 后缀是因为
    /// <c>TypeSymbolHelper.GetTypeFullName</c> 会把 <c>Task&lt;Stream?&gt;</c> 的内层类型
    /// 输出为 <c>System.IO.Stream?</c>，若不剥离会漏判并回退到会运行期失败的
    /// <c>ExecuteAsync&lt;Stream&gt;</c>（JSON 反序列化为 Stream）。
    /// </remarks>
    private static bool IsStreamType(string type)
    {
        var normalized = type.Trim();
        if (normalized.EndsWith("?", StringComparison.Ordinal))
            normalized = normalized.Substring(0, normalized.Length - 1).TrimEnd();

        return normalized == "Stream" || normalized == "System.IO.Stream";
    }

    /// <summary>
    /// 判断方法是否为「直达返回」：绕过请求执行器、直接调用客户端原始 API。
    /// </summary>
    /// <remarks>
    /// 当前直达返回类型为 <c>HttpResponseMessage</c>（SendRawAsync）与 <c>Stream</c>（SendStreamAsync）。
    /// 该路径不参与 Cache/Resilience/Response&lt;T&gt; 编排，故与编排配置组合时报告
    /// <c>HTTPCLIENT025</c>（Warning），避免配置静默失效。
    /// </remarks>
    private static bool IsDirectReturnType(MethodAnalysisResult methodInfo)
    {
        var innerReturnType = methodInfo.IsAsyncMethod ? methodInfo.AsyncInnerReturnType : methodInfo.ReturnType;
        return IsHttpResponseMessageType(innerReturnType) || IsStreamType(innerReturnType);
    }

    /// <summary>
    /// 检测返回类型是否为 Response&lt;T&gt;，并提取内部类型 T。
    /// </summary>
    private static bool IsResponseType(string type, out string innerType)
    {
        innerType = string.Empty;

        if (type.StartsWith("Response<", StringComparison.Ordinal) ||
            type.StartsWith("Mud.HttpUtils.Response<", StringComparison.Ordinal) ||
            type.StartsWith("Mud.HttpUtils.HttpClient.Response<", StringComparison.Ordinal))
        {
            var startIdx = type.IndexOf('<');
            var endIdx = type.LastIndexOf('>');
            if (startIdx >= 0 && endIdx > startIdx)
            {
                innerType = type.Substring(startIdx + 1, endIdx - startIdx - 1);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取 CancellationToken 参数
    /// </summary>
    private string GetCancellationTokenParams(MethodAnalysisResult methodInfo)
    {
        var cancellationTokenParam = methodInfo.Parameters.FirstOrDefault(
            p => TypeDetectionHelper.IsCancellationToken(p.Type));

        if (cancellationTokenParam != null)
            return $", cancellationToken: {cancellationTokenParam.Name}";

        return ", cancellationToken: default";
    }

    /// <summary>
    /// 生成接口定义的Header代码
    /// </summary>
    private void GenerateInterfaceHeaders(StringBuilder codeBuilder, GeneratorContext context, MethodAnalysisResult methodInfo)
    {
        var hasTokenManager = !string.IsNullOrEmpty(context.Configuration.TokenManager);
        var hasAuthorizationHeader = TypeSymbolHelper.HasPropertyAttribute(
            context.InterfaceSymbol!, "Header", "Authorization");

        foreach (var interfaceHeader in methodInfo.InterfaceHeaderAttributes)
        {
            if (string.IsNullOrEmpty(interfaceHeader.Name))
                continue;

            if (hasTokenManager && hasAuthorizationHeader &&
                interfaceHeader.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (interfaceHeader.Value == null)
            {
                continue;
            }

            var headerValue = interfaceHeader.Value?.ToString() ?? "null";
            var escapedHeaderName = StringEscapeHelper.EscapeString(interfaceHeader.Name);
            var escapedHeaderValue = StringEscapeHelper.EscapeString(headerValue);

            if (interfaceHeader.Replace)
            {
                codeBuilder.AppendLine($"            // 替换接口定义的Header: {interfaceHeader.Name}");
                codeBuilder.AppendLine($"            if (__httpRequest.Headers.Contains(\"{escapedHeaderName}\"))");
                codeBuilder.AppendLine($"                __httpRequest.Headers.Remove(\"{escapedHeaderName}\");");
                codeBuilder.AppendLine($"            __httpRequest.Headers.Add(\"{escapedHeaderName}\", \"{escapedHeaderValue}\");");
            }
            else
            {
                codeBuilder.AppendLine($"            // 添加接口定义的Header: {interfaceHeader.Name}");
                // 避免对不允许重复的 Header（如 Authorization）调用 Add 抛出 ArgumentException
                codeBuilder.AppendLine($"            if (!__httpRequest.Headers.Contains(\"{escapedHeaderName}\"))");
                codeBuilder.AppendLine($"                __httpRequest.Headers.Add(\"{escapedHeaderName}\", \"{escapedHeaderValue}\");");
            }
        }
    }

    private bool ShouldInjectToken(MethodAnalysisResult methodInfo, bool hasTokenManager, bool hasHttpClient)
    {
        // HttpClient 模式下不注入 Token
        if (hasHttpClient)
            return false;

        if (!hasTokenManager)
            return false;

        if (!string.IsNullOrEmpty(methodInfo.MethodTokenInjectionMode) ||
            !string.IsNullOrEmpty(methodInfo.InterfaceTokenInjectionMode))
            return true;

        return methodInfo.InterfaceAttributes?.Any(attr =>
            attr.StartsWith("Header:", StringComparison.Ordinal) ||
            attr.StartsWith("Query:", StringComparison.Ordinal)) == true;
    }

    private bool IsTokenHeaderMode(MethodAnalysisResult methodInfo)
    {
        // 使用 EffectiveTokenInjectionMode 确保方法级 InjectionMode 优先于接口级
        return methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeHeader;
    }

    private bool IsTokenApiKeyMode(MethodAnalysisResult methodInfo)
    {
        return methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeApiKey;
    }

    private bool IsTokenBasicAuthMode(MethodAnalysisResult methodInfo)
    {
        return methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeBasicAuth;
    }

    private bool IsTokenCookieMode(MethodAnalysisResult methodInfo)
    {
        return methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModeCookie;
    }

    private string GetTokenHeaderName(MethodAnalysisResult methodInfo)
    {
        return _requestBuilder.GetTokenHeaderName(methodInfo) ?? "Authorization";
    }

    private void GenerateTokenInjection(StringBuilder codeBuilder, GeneratorContext context, MethodAnalysisResult methodInfo, bool needsTokenInjection, string indent)
    {
        if (!needsTokenInjection)
            return;

        if (IsTokenHeaderMode(methodInfo) || IsTokenApiKeyMode(methodInfo))
        {
            var headerName = GetTokenHeaderName(methodInfo);
            var escapedHeaderName = StringEscapeHelper.EscapeString(headerName);

            // 当 Header 模式使用标准 Authorization 头时，通过 AuthenticationHeaderValue 注入 "Bearer" 方案前缀，
            // 确保请求头格式为 "Authorization: Bearer <token>" 而非 "Authorization: <token>"。
            // 这与 TokenRecoveryExecutor.ApplyTokenToRequest 中的恢复行为保持一致。
            // ApiKey 模式或自定义 Header 名称仍使用 Headers.Add 直接注入原始令牌值。
            if (IsTokenHeaderMode(methodInfo) && headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                // P2.6（TK-21）：使用令牌方案（Scheme）而非硬编码 "Bearer"。
                var scheme = StringEscapeHelper.EscapeString(methodInfo.EffectiveTokenScheme);
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"{scheme}\", access_token);");
            }
            else
            {
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{escapedHeaderName}\", access_token);");
            }
        }
        else if (IsTokenBasicAuthMode(methodInfo))
        {
            // P2.6（TK-21）：BasicAuth 使用令牌方案（Scheme），默认 "Basic"。
            var scheme = StringEscapeHelper.EscapeString(methodInfo.EffectiveTokenScheme);
            codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"Authorization\", $\"{scheme} {{__basicCredentials}}\");");
        }
        else if (IsTokenCookieMode(methodInfo))
        {
            var cookieName = !string.IsNullOrEmpty(methodInfo.InterfaceTokenName) ? methodInfo.InterfaceTokenName : "access_token";
            var escapedCookieName = StringEscapeHelper.EscapeString(cookieName);
            codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"Cookie\", \"{escapedCookieName}=\" + access_token);");
        }

        if (ShouldGenerateTokenRecoveryContext(context, methodInfo))
            GenerateTokenRecoveryContext(codeBuilder, context, methodInfo, indent);
    }

    /// <summary>
    /// 判断是否需要生成 TokenRecoveryContext。
    /// 仅在非默认场景下生成：恢复处理器的 null 回退已覆盖默认 Header+Authorization+Bearer 场景。
    /// P2.5（TK-07）：当显式指定了 TokenManagerKey 时也必须生成上下文，以便 TokenManagerKey 能
    /// 携带到恢复执行器，作为管理器定位的查询键与可观测性维度写入。
    /// </summary>
    private bool ShouldGenerateTokenRecoveryContext(GeneratorContext context, MethodAnalysisResult methodInfo)
    {
        var injectionMode = methodInfo.EffectiveTokenInjectionMode;

        // Path 和 HmacSignature 模式不被恢复处理器支持，生成上下文无意义
        if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModePath ||
            injectionMode == HttpClientGeneratorConstants.TokenInjectionModeHmacSignature)
            return false;

        // 显式指定 TokenManagerKey（方法级或接口级）时生成上下文，使 key 贯通到恢复执行器
        if (!string.IsNullOrEmpty(methodInfo.MethodTokenManagerKey) ||
            !string.IsNullOrEmpty(context.Configuration.TokenManagerKey))
            return true;

        // 用户级令牌需要 UserId 才能正确恢复
        if (TokenMethodHelper.MethodRequiresUserId(context, methodInfo))
            return true;

        // 非默认注入模式（Query/Cookie/ApiKey/BasicAuth）需要上下文才能正确恢复
        if (injectionMode != HttpClientGeneratorConstants.TokenInjectionModeHeader)
            return true;

        // Header 模式但使用了自定义 Header 名称，需要上下文
        var headerName = GetTokenHeaderName(methodInfo);
        if (headerName != "Authorization")
            return true;

        // 默认场景：Header 模式 + Authorization 头 + 无用户级令牌
        // 恢复处理器的 null 回退行为与此一致，无需生成上下文
        return false;
    }

    private void GenerateTokenRecoveryContext(StringBuilder codeBuilder, GeneratorContext context, MethodAnalysisResult methodInfo, string indent)
    {
        var injectionMode = methodInfo.EffectiveTokenInjectionMode;
        var headerName = GetTokenHeaderName(methodInfo);
        var cookieName = !string.IsNullOrEmpty(methodInfo.InterfaceTokenName) ? methodInfo.InterfaceTokenName : "access_token";
        var requiresUserId = TokenMethodHelper.MethodRequiresUserId(context, methodInfo);
        var userIdExpr = requiresUserId ? "_currentUserContext.UserId" : "null";

        // 对所有插入字符串字面量的用户输入进行转义，防止生成代码编译失败
        var escapedHeaderName = StringEscapeHelper.EscapeString(headerName);
        var escapedCookieName = StringEscapeHelper.EscapeString(cookieName);
        var escapedTokenScheme = StringEscapeHelper.EscapeString(methodInfo.EffectiveTokenScheme);

        var injectionModeValue = injectionMode switch
        {
            "Header" => "TokenInjectionMode.Header",
            "Query" => "TokenInjectionMode.Query",
            "Path" => "TokenInjectionMode.Path",
            "ApiKey" => "TokenInjectionMode.ApiKey",
            "HmacSignature" => "TokenInjectionMode.HmacSignature",
            "BasicAuth" => "TokenInjectionMode.BasicAuth",
            "Cookie" => "TokenInjectionMode.Cookie",
            _ => "TokenInjectionMode.Header"
        };

        // P2.5（TK-07）：将 TokenManagerKey 写入恢复上下文，使恢复执行器能据此定位管理器并标识可观测维度。
        // GetMethodTokenManagerKey 始终返回非空（含默认回退），因此直接转义后写死；仅当有跟踪值时也保持简单性。
        var tokenManagerKey = TokenMethodHelper.GetMethodTokenManagerKey(context, methodInfo);
        var escapedTokenManagerKey = StringEscapeHelper.EscapeString(tokenManagerKey);

        // Query 模式需要 QueryParameterName 才能在恢复时重新注入查询参数
        string? queryParamName = null;
        string? escapedQueryParamName = null;
        if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeQuery)
        {
            queryParamName = _requestBuilder.GetTokenQueryName(methodInfo);
            if (queryParamName != null)
                escapedQueryParamName = StringEscapeHelper.EscapeString(queryParamName);
        }

        codeBuilder.AppendLine($"{indent}#if NETSTANDARD2_0");
        codeBuilder.AppendLine($"{indent}__httpRequest.Properties[\"__Mud_HttpUtils_TokenRecoveryContext\"] = new Mud.HttpUtils.TokenRecoveryContext");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    InjectionMode = {injectionModeValue},");
        codeBuilder.AppendLine($"{indent}    HeaderName = \"{escapedHeaderName}\",");
        codeBuilder.AppendLine($"{indent}    TokenScheme = \"{escapedTokenScheme}\",");
        codeBuilder.AppendLine($"{indent}    CookieName = \"{escapedCookieName}\",");
        if (escapedQueryParamName != null)
            codeBuilder.AppendLine($"{indent}    QueryParameterName = \"{escapedQueryParamName}\",");
        codeBuilder.AppendLine($"{indent}    TokenManagerKey = \"{escapedTokenManagerKey}\",");
        codeBuilder.AppendLine($"{indent}    UserId = {userIdExpr}");
        codeBuilder.AppendLine($"{indent}}};");
        codeBuilder.AppendLine($"{indent}#else");
        codeBuilder.AppendLine($"{indent}__httpRequest.Options.TryAdd(\"__Mud_HttpUtils_TokenRecoveryContext\", new Mud.HttpUtils.TokenRecoveryContext");
        codeBuilder.AppendLine($"{indent}{{");
        codeBuilder.AppendLine($"{indent}    InjectionMode = {injectionModeValue},");
        codeBuilder.AppendLine($"{indent}    HeaderName = \"{escapedHeaderName}\",");
        codeBuilder.AppendLine($"{indent}    TokenScheme = \"{escapedTokenScheme}\",");
        codeBuilder.AppendLine($"{indent}    CookieName = \"{escapedCookieName}\",");
        if (escapedQueryParamName != null)
            codeBuilder.AppendLine($"{indent}    QueryParameterName = \"{escapedQueryParamName}\",");
        codeBuilder.AppendLine($"{indent}    TokenManagerKey = \"{escapedTokenManagerKey}\",");
        codeBuilder.AppendLine($"{indent}    UserId = {userIdExpr}");
        codeBuilder.AppendLine($"{indent}}});");
        codeBuilder.AppendLine($"{indent}#endif");
    }

    /// <summary>
    /// 校验 HttpClient 类型与方法调用的兼容性（加密、XML）
    /// </summary>
    private bool ValidateHttpClientCompatibility(GeneratorContext context, MethodAnalysisResult methodInfo)
    {
        var httpClientType = context.Configuration.HttpClient;
        if (string.IsNullOrEmpty(httpClientType))
            return true;

        var isValid = true;

        // [E-4 修复] 诊断定位到方法本身（而非整个接口声明），便于 IDE 快速定位与 #pragma 抑制。
        var methodLocation = GetMethodLocation(context, methodInfo);

        // 校验加密兼容性：EnableEncrypt=true 时 HttpClient 必须实现 IEncryptableHttpClient
        if (methodInfo.BodyEnableEncrypt)
        {
            if (!HttpClientTypeSupportsInterface(context.Compilation, httpClientType!, "IEncryptableHttpClient", out var encryptTypeResolved))
            {
                context.ProductionContext.ReportDiagnostic(
                    Diagnostic.Create(
                        Diagnostics.HttpClientEncryptNotSupported,
                        methodLocation,
                        context.InterfaceDeclaration.Identifier.Text,
                        methodInfo.MethodName ?? "Unknown",
                        httpClientType));
                isValid = false;
            }
            else if (!encryptTypeResolved)
            {
                context.ProductionContext.ReportDiagnostic(
                    Diagnostic.Create(
                        Diagnostics.HttpClientTypeUnresolved,
                        methodLocation,
                        context.InterfaceDeclaration.Identifier.Text,
                        methodInfo.MethodName ?? "Unknown",
                        httpClientType));
            }
        }

        // 校验 XML 兼容性：XML 请求/响应时 HttpClient 必须实现 IXmlHttpClient
        var isXmlRequest = ContentTypeHelper.IsXmlContentType(methodInfo.BodyContentType);
        var isXmlResponse = ContentTypeHelper.IsXmlContentType(methodInfo.ResponseContentType);
        if (isXmlRequest || isXmlResponse)
        {
            if (!HttpClientTypeSupportsInterface(context.Compilation, httpClientType!, "IXmlHttpClient", out var xmlTypeResolved))
            {
                context.ProductionContext.ReportDiagnostic(
                    Diagnostic.Create(
                        Diagnostics.HttpClientXmlNotSupported,
                        methodLocation,
                        context.InterfaceDeclaration.Identifier.Text,
                        methodInfo.MethodName ?? "Unknown",
                        httpClientType));
                // XML 不兼容是警告级别，不阻止生成
            }
            else if (!xmlTypeResolved)
            {
                context.ProductionContext.ReportDiagnostic(
                    Diagnostic.Create(
                        Diagnostics.HttpClientTypeUnresolved,
                        methodLocation,
                        context.InterfaceDeclaration.Identifier.Text,
                        methodInfo.MethodName ?? "Unknown",
                        httpClientType));
            }
        }

        return isValid;
    }

    /// <summary>
    /// 获取方法声明位置（E-4）：优先方法语法节点，回退接口声明。
    /// </summary>
    private static Location GetMethodLocation(GeneratorContext context, MethodAnalysisResult methodInfo)
    {
        // FindMethodSyntax 需要 IMethodSymbol；methodInfo 不含符号，回退到按名称在接口内查找。
        var methodSyntax = context.InterfaceDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => string.Equals(m.Identifier.Text, methodInfo.MethodName, StringComparison.Ordinal));
        return methodSyntax?.GetLocation() ?? context.InterfaceDeclaration.GetLocation();
    }

    /// <summary>
    /// 检查指定的 HttpClient 类型是否实现了给定的接口
    /// </summary>
    /// <param name="compilation">编译单元（用于解析自定义 HttpClient 类型）。</param>
    /// <param name="httpClientType">[HttpClientApi] 指定的 HttpClient 类型名。</param>
    /// <param name="interfaceName">要求实现的接口名（如 <c>IXmlHttpClient</c> / <c>IEncryptableHttpClient</c>）。</param>
    /// <param name="typeResolved">返回 true 表示类型已解析并完成了实际校验；false 表示类型无法解析，结果为保守放行。</param>
    private static bool HttpClientTypeSupportsInterface(Compilation compilation, string httpClientType, string interfaceName, out bool typeResolved)
    {
        // IEnhancedHttpClient 继承了 IJsonHttpClient 和 IXmlHttpClient，同时 EnhancedHttpClient 实现了 IEncryptableHttpClient
        if (httpClientType == "IEnhancedHttpClient")
        {
            typeResolved = true;
            return true;
        }

        // IBaseHttpClient 不支持 XML 和加密
        if (httpClientType == "IBaseHttpClient")
        {
            typeResolved = true;
            return interfaceName == "IJsonHttpClient" || interfaceName == "IBaseHttpClient";
        }

        // 对于其他自定义类型，尝试从编译中解析类型并检查接口
        var typeSymbol = compilation.GetTypeByMetadataName(httpClientType)
            ?? compilation.GetTypesByMetadataName(httpClientType).FirstOrDefault();

        if (typeSymbol != null)
        {
            typeResolved = true;
            return typeSymbol.AllInterfaces.Any(i => i.Name == interfaceName);
        }

        // 无法解析类型时，默认通过（避免误报），但标记为未解析以便调用方输出警告
        typeResolved = false;
        return true;
    }

    private static bool HasCacheAttribute(IMethodSymbol methodSymbol)
    {
        return methodSymbol.GetAttributes()
            .Any(attr => HttpClientGeneratorConstants.CacheAttributeNames.Contains(attr.AttributeClass?.Name));
    }

    private static bool HasResilienceAttribute(IMethodSymbol methodSymbol)
    {
        return methodSymbol.GetAttributes()
            .Any(attr =>
                HttpClientGeneratorConstants.RetryAttributeNames.Contains(attr.AttributeClass?.Name) ||
                HttpClientGeneratorConstants.CircuitBreakerAttributeNames.Contains(attr.AttributeClass?.Name) ||
                HttpClientGeneratorConstants.TimeoutAttributeNames.Contains(attr.AttributeClass?.Name));
    }

    private static bool HasQueryMapAttribute(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Parameters
            .Any(p => p.GetAttributes()
                .Any(attr => attr.AttributeClass?.Name == HttpClientGeneratorConstants.QueryMapAttribute));
    }

    private static bool HasComplexQueryParameter(MethodAnalysisResult methodInfo)
    {
        // 基于 methodInfo.Parameters 检查，其中包含实际特性及合成的默认 [Query] 特性，
        // 确保无属性标注的复杂类型参数也能正确触发 FlattenObjectToQueryParams 辅助方法的生成。
        return methodInfo.Parameters
            .Any(p => p.Attributes.Any(attr => attr.Name == HttpClientGeneratorConstants.QueryAttribute) &&
                !TypeDetectionHelper.IsSimpleType(p.Type));
    }

    private static void ValidatePathParameters(GeneratorContext context, IMethodSymbol methodSymbol, MethodAnalysisResult methodInfo)
    {
        var urlTemplate = methodInfo.UrlTemplate;
        if (string.IsNullOrEmpty(urlTemplate))
            return;

        // CFG-18：接口标记 [AllowUnmatchedRouteParameters] 时，URL 模板中的未匹配 {token} 占位符
        // 有意保留为字面量（交由 DelegatingHandler/拦截器在运行时重写），跳过 HTTPCLIENT013 校验。
        if (AttributeDataHelper.HasAttribute(
                context.InterfaceSymbol!, HttpClientGeneratorConstants.AllowUnmatchedRouteParametersAttributeNames))
            return;

        var templatePlaceholders = ExtractPathPlaceholders(urlTemplate);
        if (templatePlaceholders.Count == 0)
            return;

        var pathParams = new HashSet<string>(
            // [F3 修复] 单一事实源：与生成阶段（RequestBuilder.GetPathParameterName）共用相同的
            // 占位符名解析，[Path(Name = "userId")] int id 不再被误报为占位符缺失。
            // methodInfo.Parameters 已由 ParameterAnalyzer 解析为 ParameterAttributeInfo（含 Name/Arguments/NamedArguments），
            // 直接复用而非对 IMethodSymbol.Parameters 重新 GetAttributes。
            methodInfo.Parameters
                .Where(p => p.Attributes.Any(attr => HttpClientGeneratorConstants.PathAttributes.Contains(attr.Name)))
                .Select(p => RequestBuilder.GetEffectivePathName(
                    p.Attributes.First(attr => HttpClientGeneratorConstants.PathAttributes.Contains(attr.Name)),
                    p.Name)),
            StringComparer.OrdinalIgnoreCase);

        // 当 Token 使用 Path 注入模式时，URL 模板中的 Token 占位符应由 Token 注入机制替换，
        // 不需要对应的 [Path] 参数，因此将 Token 占位符从缺失列表中排除
        // 使用已缓存的 methodInfo，避免重复调用 AnalyzeMethod
        var tokenPathPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (methodInfo.IsValid && methodInfo.EffectiveTokenInjectionMode == HttpClientGeneratorConstants.TokenInjectionModePath
            && !string.IsNullOrEmpty(methodInfo.InterfaceTokenName))
        {
            tokenPathPlaceholders.Add(methodInfo.InterfaceTokenName);
        }

        var missingInMethod = templatePlaceholders
            .Where(p => !pathParams.Contains(p) && !tokenPathPlaceholders.Contains(p))
            .ToList();

        var extraInMethod = pathParams
            .Where(p => !templatePlaceholders.Contains(p))
            .ToList();

        if (missingInMethod.Count > 0 || extraInMethod.Count > 0)
        {
            var details = new List<string>();
            if (missingInMethod.Count > 0)
                details.Add($"URL 模板中的占位符 {{{string.Join("}, {", missingInMethod)}}} 在方法参数中找不到对应的 [Path] 参数");
            if (extraInMethod.Count > 0)
                details.Add($"方法参数中的 [Path] 参数 {string.Join(", ", extraInMethod)} 在 URL 模板中找不到对应的占位符");

            var methodSyntax = MethodAnalyzer.FindMethodSyntax(
                context.Compilation, methodSymbol, context.InterfaceDeclaration, context.SemanticModel);

            context.ProductionContext.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HttpClientPathParameterMismatch,
                methodSyntax?.GetLocation() ?? context.InterfaceDeclaration.GetLocation(),
                context.InterfaceDeclaration.Identifier.Text,
                methodSymbol.Name,
                urlTemplate,
                string.Join("；", details)));
        }
    }

    private static HashSet<string> ExtractPathPlaceholders(string urlTemplate)
    {
        var placeholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var span = urlTemplate.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '{')
            {
                var end = span.Slice(i).IndexOf('}');
                if (end > 1)
                {
                    // 先检查 span 是否为空或空白，避免对 "{}" 或 "{ }" 场景分配字符串
                    var placeholderSpan = span.Slice(i + 1, end - 1);
                    if (!placeholderSpan.IsEmpty && !placeholderSpan.IsWhiteSpace())
                    {
                        placeholders.Add(placeholderSpan.ToString());
                    }
                    i += end;
                }
            }
        }
        return placeholders;
    }

    /// <summary>
    /// 获取方法声明语法节点（用于诊断 Location 精细化）。
    /// </summary>
    private static MethodDeclarationSyntax? GetMethodSyntax(IMethodSymbol methodSymbol, GeneratorContext context)
        => MethodAnalyzer.FindMethodSyntax(
            context.Compilation, methodSymbol, context.InterfaceDeclaration, context.SemanticModel);

    /// <summary>
    /// [Phase1 修复 1.1] 定位 HTTP 方法特性的第一个参数（URL 字面量）的语法位置。
    /// 使 CodeFix 能通过 FindToken(span.Start).Parent?.FirstAncestorOrSelf&lt;AttributeSyntax&gt;() 命中。
    /// </summary>
    private static Location? GetHttpMethodAttributeArgumentLocation(IMethodSymbol methodSymbol, GeneratorContext context)
    {
        var methodAttr = MethodAnalyzer.FindHttpMethodAttributeFromAttributes(methodSymbol.GetAttributes());
        if (methodAttr == null)
            return null;

        var attrSyntax = methodAttr.ApplicationSyntaxReference?.GetSyntax();
        if (attrSyntax == null)
            return null;

        // 定位到第一个特性参数（URL 模板字面量），与 CodeFix 的查找逻辑对齐：
        // CodeFix 用 root.FindToken(span.Start).Parent?.FirstAncestorOrSelf<AttributeSyntax>() 查找特性，
        // 再取 attribute.ArgumentList.Arguments[0].Expression as LiteralExpressionSyntax。
        var firstArg = attrSyntax.DescendantNodes()
            .OfType<AttributeArgumentSyntax>()
            .FirstOrDefault();

        return firstArg?.GetLocation() ?? attrSyntax.GetLocation();
    }

}
