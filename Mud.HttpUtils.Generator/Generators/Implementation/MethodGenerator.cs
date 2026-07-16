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
            var isHttpMethod = MethodAnalyzer.FindHttpMethodAttributeFromSymbol(methodSymbol) != null;
            if (!isHttpMethod)
                continue;

            // 一次性分析方法并缓存结果到 GeneratorContext.MethodAnalysisCache，避免 AnalyzeMethod 被重复调用
            var methodInfo = context.GetOrAnalyzeMethod(
                context.Compilation,
                methodSymbol,
                context.InterfaceDeclaration,
                context.SemanticModel);

            ValidatePathParameters(context, methodSymbol, methodInfo);

            GenerateMethodImplementation(codeBuilder, context, methodSymbol, methodInfo, isAbstractClass);

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
    /// 生成单个方法的实现代码
    /// </summary>
    private void GenerateMethodImplementation(StringBuilder codeBuilder, GeneratorContext context, IMethodSymbol methodSymbol, MethodAnalysisResult methodInfo, bool isVirtual = false)
    {
        if (!methodInfo.IsValid) return;

        if (!string.IsNullOrEmpty(methodInfo.UrlTemplate) &&
            !CSharpCodeValidator.IsValidUrlTemplate(methodInfo.UrlTemplate, out var urlError))
        {
            context.ProductionContext.ReportDiagnostic(
                Diagnostic.Create(
                    Diagnostics.HttpClientInvalidUrlTemplate,
                    context.InterfaceDeclaration.GetLocation(),
                    context.InterfaceDeclaration.Identifier.Text,
                    methodInfo.UrlTemplate,
                    urlError));
            return;
        }

        if (methodInfo.IgnoreGenerator) return;

        if (!ValidateHttpClientCompatibility(context, methodInfo))
            return;

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

        var hasTokenManager = !string.IsNullOrEmpty(context.Configuration.TokenManager);
        var hasHttpClient = !string.IsNullOrEmpty(context.Configuration.HttpClient);
        var needsTokenInjection = ShouldInjectToken(methodInfo, hasTokenManager, hasHttpClient);

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

        codeBuilder.AppendLine($"        public {virtualKeyword}{asyncKeyword}{returnType} {methodSymbol.Name}({TypeSymbolHelper.GetParameterList(methodSymbol)})");
        codeBuilder.AppendLine("        {");

        ParameterValidationHelper.GenerateParameterValidation(codeBuilder, methodInfo.Parameters);

        // 捕获应用上下文到局部变量，避免在异步执行过程中 _appContextHolder.Current 被其他线程修改导致 TOCTOU 竞态
        if (!hasHttpClient)
        {
            // TokenManager 模式：_appContextHolder.Current 为 null 时回退到默认应用，与 GetTokenAsync 中的回退逻辑保持一致。
            // Default 模式（无 TokenManager）：构造函数已初始化 _appContextHolder.Current = _defaultAppContext，正常情况下不会为 null，
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

            var scopesArg = scopes.Length > 0
                ? $"new[] {{ {string.Join(", ", scopes.Select(s => $"\"{s}\""))} }}"
                : "null";
            var userIdArg = requiresUserId ? "_currentUserContext.UserId" : "null";

            if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeApiKey)
            {
                var apiKeyName = methodInfo.InterfaceTokenName;
                if (!string.IsNullOrEmpty(apiKeyName))
                    codeBuilder.AppendLine($"            var access_token = await GetApiKeyAsync(\"{apiKeyName}\").ConfigureAwait(false);");
                else
                    codeBuilder.AppendLine($"            var access_token = await GetApiKeyAsync().ConfigureAwait(false);");
            }
            else if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeHmacSignature)
            {
                codeBuilder.AppendLine($"            await ApplyHmacSignatureAsync(__httpRequest).ConfigureAwait(false);");
            }
            else if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModeBasicAuth)
            {
                if (!string.IsNullOrEmpty(tokenParamName) && !tokenParamHasHeader)
                {
                    codeBuilder.AppendLine($"            var access_token = !string.IsNullOrWhiteSpace({tokenParamName}) ? {tokenParamName} : await GetTokenAsync(\"{tokenManagerKey}\", {userIdArg}, {scopesArg}).ConfigureAwait(false);");
                }
                else
                {
                    codeBuilder.AppendLine($"            var access_token = await GetTokenAsync(\"{tokenManagerKey}\", {userIdArg}, {scopesArg}).ConfigureAwait(false);");
                }
                codeBuilder.AppendLine($"            var __basicCredentials = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(access_token));");
            }
            else
            {
                if (!string.IsNullOrEmpty(tokenParamName) && !tokenParamHasHeader)
                {
                    codeBuilder.AppendLine($"            var access_token = !string.IsNullOrWhiteSpace({tokenParamName}) ? {tokenParamName} : await GetTokenAsync(\"{tokenManagerKey}\", {userIdArg}, {scopesArg}).ConfigureAwait(false);");
                }
                else
                {
                    codeBuilder.AppendLine($"            var access_token = await GetTokenAsync(\"{tokenManagerKey}\", {userIdArg}, {scopesArg}).ConfigureAwait(false);");
                }
            }
        }

        codeBuilder.AppendLine();

        // 统一调用执行器：Cache/Resilience 编排由运行时执行器处理，消除生成器中的三分支互斥逻辑
        GenerateExecutorCall(codeBuilder, context, methodInfo, hasHttpClient, needsTokenInjection, executor, httpClientExpr);

        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
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
            var isNullableByteArray = deserializeType.TrimEnd().EndsWith("?");

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

        // void 返回 — 使用非泛型 ExecuteAsync（支持 Cache/Resilience 编排）
        if (IsVoidType(deserializeType))
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
        sb.AppendLine($"                ResponseContentType = \"{methodInfo.ResponseContentType ?? ""}\",");
        sb.AppendLine($"                EnableDecrypt = {methodInfo.ResponseEnableDecrypt.ToString().ToLowerInvariant()},");

        var isVoid = IsVoidType(deserializeType);
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
        sb.AppendLine($"                       ResponseContentType = \"{methodInfo.ResponseContentType ?? ""}\",");
        sb.AppendLine($"                       EnableDecrypt = {methodInfo.ResponseEnableDecrypt.ToString().ToLowerInvariant()},");

        var isVoid = IsVoidType(deserializeType);
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
            if (!string.IsNullOrEmpty(methodInfo.CacheKeyTemplate))
                sb.AppendLine($"                       KeyTemplate = \"{methodInfo.CacheKeyTemplate}\",");
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

        keyBuilder.Append("\"");
        return keyBuilder.ToString();
    }

    /// <summary>
    /// 判断类型是否为 void。
    /// </summary>
    private static bool IsVoidType(string type)
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
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", access_token);");
            }
            else
            {
                codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"{escapedHeaderName}\", access_token);");
            }
        }
        else if (IsTokenBasicAuthMode(methodInfo))
        {
            codeBuilder.AppendLine($"{indent}__httpRequest.Headers.Add(\"Authorization\", $\"Basic {{__basicCredentials}}\");");
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
    /// </summary>
    private bool ShouldGenerateTokenRecoveryContext(GeneratorContext context, MethodAnalysisResult methodInfo)
    {
        var injectionMode = methodInfo.EffectiveTokenInjectionMode;

        // Path 和 HmacSignature 模式不被恢复处理器支持，生成上下文无意义
        if (injectionMode == HttpClientGeneratorConstants.TokenInjectionModePath ||
            injectionMode == HttpClientGeneratorConstants.TokenInjectionModeHmacSignature)
            return false;

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
        var escapedTokenScheme = StringEscapeHelper.EscapeString(injectionMode == "BasicAuth" ? "Basic" : "Bearer");

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

        // 校验加密兼容性：EnableEncrypt=true 时 HttpClient 必须实现 IEncryptableHttpClient
        if (methodInfo.BodyEnableEncrypt)
        {
            if (!HttpClientTypeSupportsInterface(context.Compilation, httpClientType!, "IEncryptableHttpClient", out var encryptTypeResolved))
            {
                context.ProductionContext.ReportDiagnostic(
                    Diagnostic.Create(
                        Diagnostics.HttpClientEncryptNotSupported,
                        context.InterfaceDeclaration.GetLocation(),
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
                        context.InterfaceDeclaration.GetLocation(),
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
                        context.InterfaceDeclaration.GetLocation(),
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
                        context.InterfaceDeclaration.GetLocation(),
                        context.InterfaceDeclaration.Identifier.Text,
                        methodInfo.MethodName ?? "Unknown",
                        httpClientType));
            }
        }

        return isValid;
    }

    /// <summary>
    /// 检查指定的 HttpClient 类型是否实现了给定的接口
    /// </summary>
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

        var templatePlaceholders = ExtractPathPlaceholders(urlTemplate);
        if (templatePlaceholders.Count == 0)
            return;

        var pathParams = new HashSet<string>(
            methodSymbol.Parameters
                .Where(p => p.GetAttributes().Any(attr =>
                    HttpClientGeneratorConstants.PathAttributes.Contains(attr.AttributeClass?.Name)))
                .Select(p => p.Name),
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

}
