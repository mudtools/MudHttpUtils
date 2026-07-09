// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Generators.Base;
using Mud.HttpUtils.Generators.Context;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 构造函数生成器，负责生成类的字段和构造函数
/// </summary>
internal class ConstructorGenerator : ICodeFragmentGenerator
{
    private readonly GeneratorContext _context;

    public ConstructorGenerator(GeneratorContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 生成类字段和构造函数
    /// </summary>
    public void Generate(StringBuilder codeBuilder, GeneratorContext context)
    {
        var className = context.ClassName;
        GenerateClassFields(codeBuilder);
        GenerateInterfaceProperties(codeBuilder);
        GenerateConstructorDocumentation(codeBuilder, className);
        GenerateConstructorSignature(codeBuilder, className);
        GenerateConstructorBody(codeBuilder);
        GenerateHelperMethods(codeBuilder);
    }

    /// <summary>
    /// 生成类字段
    /// </summary>
    private void GenerateClassFields(StringBuilder codeBuilder)
    {
        if (_context.HasInheritedFrom)
        {
            // 当基类没有 TokenManager 但派生类有时，需要生成自己的令牌相关字段
            if (_context.HasTokenManager && !_context.Configuration.BaseHasTokenManager)
            {
                codeBuilder.AppendLine("        /// <summary>");
                codeBuilder.AppendLine($"        /// 用于HttpClient客户端操作操作使用的的<see cref = \"{_context.Configuration.TokenManagerType}\"/> 令牌管理实例。");
                codeBuilder.AppendLine("        /// </summary>");
                codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly {_context.Configuration.TokenManagerType} _appManager;");

                codeBuilder.AppendLine("        /// <summary>");
                codeBuilder.AppendLine("        /// 令牌提供器，用于获取访问令牌。");
                codeBuilder.AppendLine("        /// </summary>");
                codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly ITokenProvider _tokenProvider;");
            }

            if (_context.HasTokenManager && _context.Configuration.AnyMethodRequiresUserId)
            {
                codeBuilder.AppendLine("        /// <summary>");
                codeBuilder.AppendLine("        /// 当前用户上下文，用于获取当前用户ID。");
                codeBuilder.AppendLine("        /// </summary>");
                codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly ICurrentUserContext _currentUserContext;");
            }

            // 当派生类有新的缓存/弹性策略方法但基类没有时，需要生成自己的字段
            if (_context.HasCache && !_context.Configuration.BaseHasCache)
            {
                codeBuilder.AppendLine("        /// <summary>");
                codeBuilder.AppendLine("        /// HTTP响应缓存提供器，用于缓存接口方法的响应结果。");
                codeBuilder.AppendLine("        /// </summary>");
                codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IHttpResponseCache _cacheProvider;");
            }

            if (_context.HasResilience && !_context.Configuration.BaseHasResilience)
            {
                codeBuilder.AppendLine("        /// <summary>");
                codeBuilder.AppendLine("        /// 弹性策略解析器，用于方法级重试、烕断、超时等弹性策略的运行时编排。");
                codeBuilder.AppendLine("        /// </summary>");
                codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IResiliencePolicyResolver _resilienceResolver;");
            }

            if (_context.ImplementsICurrentUserId && _context.Configuration.AnyMethodRequiresUserId)
            {
                codeBuilder.AppendLine();
                codeBuilder.AppendLine("        /// <summary>");
                codeBuilder.AppendLine("        /// 当前用户ID（ICurrentUserId 实现），委托给 ICurrentUserContext。");
                codeBuilder.AppendLine("        /// </summary>");
                codeBuilder.AppendLine("        public string? CurrentUserId");
                codeBuilder.AppendLine("        {");
                codeBuilder.AppendLine("            get => _currentUserContext.UserId;");
                codeBuilder.AppendLine("        }");
            }
            else if (_context.Configuration.AnyMethodRequiresUserId)
            {
                codeBuilder.AppendLine();
                codeBuilder.AppendLine("        /// <summary>");
                codeBuilder.AppendLine("        /// 当前用户ID，委托给 ICurrentUserContext.UserId。");
                codeBuilder.AppendLine("        /// </summary>");
                codeBuilder.AppendLine("        public string? CurrentUserId => _currentUserContext.UserId;");
            }

            codeBuilder.AppendLine();
            return;
        }

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 用于JSON内容序列化与反序列化操作的<see cref = \"JsonSerializerOptions\"/> 参数实例。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly JsonSerializerOptions _jsonSerializerOptions;");

        if (_context.HasTokenManager)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 应用上下文持有器，用于获取、设置和切换当前应用上下文。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IAppContextHolder _appContextHolder;");

            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine($"        /// 用于HttpClient客户端操作操作使用的的<see cref = \"{_context.Configuration.TokenManagerType}\"/> 令牌管理实例。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly {_context.Configuration.TokenManagerType} _appManager;");

            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 令牌提供器，用于获取访问令牌。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly ITokenProvider _tokenProvider;");

            if (_context.Configuration.AnyMethodRequiresUserId)
            {
                codeBuilder.AppendLine("        /// <summary>");
                codeBuilder.AppendLine("        /// 当前用户上下文，用于获取当前用户ID。");
                codeBuilder.AppendLine("        /// </summary>");
                codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly ICurrentUserContext _currentUserContext;");
            }
        }
        else if (_context.HasHttpClient)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine($"        /// 用于HttpClient客户端操作的<see cref = \"{_context.Configuration.HttpClient}\"/> 实例。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly {_context.Configuration.HttpClient} _httpClient;");

            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// HTTP 请求执行器，统一处理响应反序列化和错误处理。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IHttpRequestExecutor _executor;");
        }
        else
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 应用上下文持有器，用于获取、设置和切换当前应用上下文。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IAppContextHolder _appContextHolder;");
        }

        codeBuilder.AppendLine("#pragma warning disable CS0414");
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 用于HttpClient客户端操作的内容类型。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly string _defaultContentType = \"{StringEscapeHelper.EscapeString(_context.Configuration.DefaultContentType)}\";");
        codeBuilder.AppendLine("#pragma warning restore CS0414");

        // 始终生成缓存/弹性策略字段：声明特性时为非空必选，未声明时为可空可选（由 DI 注入）
        if (_context.HasCache)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// HTTP响应缓存提供器，用于缓存接口方法的响应结果。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IHttpResponseCache _cacheProvider;");
        }
        else
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// HTTP响应缓存提供器（可选，未声明 [Cache] 特性时由 DI 注入）。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IHttpResponseCache? _cacheProvider;");
        }

        if (_context.HasResilience)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 弹性策略解析器，用于方法级重试、熔断、超时等弹性策略的运行时编排。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IResiliencePolicyResolver _resilienceResolver;");
        }
        else
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 弹性策略解析器（可选，未声明 [Retry]/[CircuitBreaker]/[Timeout] 特性时由 DI 注入）。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IResiliencePolicyResolver? _resilienceResolver;");
        }

        if (_context.ImplementsICurrentUserId && _context.Configuration.AnyMethodRequiresUserId)
        {
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 当前用户ID（ICurrentUserId 实现），委托给 ICurrentUserContext。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine("        public string? CurrentUserId");
            codeBuilder.AppendLine("        {");
            codeBuilder.AppendLine("            get => _currentUserContext.UserId;");
            codeBuilder.AppendLine("            set => _currentUserContext.SetUserId(value);");
            codeBuilder.AppendLine("        }");
        }
        else if (_context.Configuration.AnyMethodRequiresUserId)
        {
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 当前用户ID，委托给 ICurrentUserContext.UserId。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine("        public string? CurrentUserId => _currentUserContext.UserId;");
        }
        else if (_context.HasCacheVaryByUser)
        {
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 当前用户ID，用于缓存键的用户隔离。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine("        public string? CurrentUserId { get; set; }");
        }

        if (_context.HasXmlResponse && _context.XmlResponseTypes.Count > 0)
        {
            codeBuilder.AppendLine();
            foreach (var xmlType in _context.XmlResponseTypes.OrderBy(t => t))
            {
                var safeFieldName = GetXmlSerializerFieldName(xmlType);
                codeBuilder.AppendLine($"        private static readonly System.Xml.Serialization.XmlSerializer {safeFieldName} = new System.Xml.Serialization.XmlSerializer(typeof({xmlType}));");
            }
        }

        codeBuilder.AppendLine();
    }

    private void GenerateInterfaceProperties(StringBuilder codeBuilder)
    {
        var properties = _context.InterfaceProperties;
        if (properties.Count == 0)
            return;

        codeBuilder.AppendLine();
        codeBuilder.AppendLine("        #region Interface Properties");
        codeBuilder.AppendLine();

        foreach (var property in properties)
        {
            var propLine = $"        public {property.Type} {property.Name} {{ get; set; }}";
            if (!string.IsNullOrEmpty(property.DefaultValue))
            {
                propLine += $" = {property.DefaultValue};";
            }
            else if (property.AttributeType == "Path" && (property.Type == "string" || property.Type == "String"))
            {
                propLine += " = string.Empty;";
            }
            codeBuilder.AppendLine(propLine);
        }

        codeBuilder.AppendLine("        #endregion");
        codeBuilder.AppendLine();
    }

    /// <summary>
    /// 生成构造函数文档注释
    /// </summary>
    private void GenerateConstructorDocumentation(StringBuilder codeBuilder, string className)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine($"        /// 构建 <see cref = \"{className}\"/> 类的实例。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"option\">Json序列化参数</param>");

        if (_context.HasTokenManager)
        {
            codeBuilder.AppendLine("        /// <param name=\"appManager\">应用令牌管理器</param>");
            codeBuilder.AppendLine("        /// <param name=\"appContextHolder\">应用上下文持有器</param>");
            codeBuilder.AppendLine("        /// <param name=\"tokenProvider\">令牌提供器</param>");
            if (_context.Configuration.AnyMethodRequiresUserId)
            {
                codeBuilder.AppendLine("        /// <param name=\"currentUserContext\">当前用户上下文</param>");
            }
        }
        else if (_context.HasHttpClient)
        {
            codeBuilder.AppendLine($"        /// <param name=\"httpClient\">HttpClient实例</param>");
            if (!_context.HasInheritedFrom)
            {
                codeBuilder.AppendLine("        /// <param name=\"executor\">HTTP请求执行器，统一处理响应反序列化和错误处理（由 DI 注入，可替换为自定义实现）</param>");
            }
        }
        else
        {
            codeBuilder.AppendLine("        /// <param name=\"appContext\">应用上下文</param>");
            codeBuilder.AppendLine("        /// <param name=\"appContextHolder\">应用上下文持有器</param>");
        }

        if (_context.HasCache)
        {
            codeBuilder.AppendLine("        /// <param name=\"cacheProvider\">HTTP响应缓存提供器</param>");
        }
        else if (_context.HasInheritedFrom && _context.Configuration.BaseHasCache)
        {
            codeBuilder.AppendLine("        /// <param name=\"cacheProvider\">HTTP响应缓存提供器（传递给基类）</param>");
        }
        else if (!_context.HasInheritedFrom)
        {
            codeBuilder.AppendLine("        /// <param name=\"cacheProvider\">HTTP响应缓存提供器（可选，未声明 [Cache] 特性时由 DI 注入）</param>");
        }

        if (_context.HasResilience)
        {
            codeBuilder.AppendLine("        /// <param name=\"resilienceResolver\">弹性策略解析器</param>");
        }
        else if (_context.HasInheritedFrom && _context.Configuration.BaseHasResilience)
        {
            codeBuilder.AppendLine("        /// <param name=\"resilienceResolver\">弹性策略解析器（传递给基类）</param>");
        }
        else if (!_context.HasInheritedFrom)
        {
            codeBuilder.AppendLine("        /// <param name=\"resilienceResolver\">弹性策略解析器（可选，未声明 [Retry]/[CircuitBreaker]/[Timeout] 特性时由 DI 注入）</param>");
        }
    }

    /// <summary>
    /// 生成构造函数签名
    /// </summary>
    private void GenerateConstructorSignature(StringBuilder codeBuilder, string className)
    {
        var parameters = new List<string>
        {
            "IOptions<JsonSerializerOptions> option"
        };

        if (_context.HasTokenManager)
        {
            parameters.Add($"{_context.Configuration.TokenManagerType} appManager");
            parameters.Add("IAppContextHolder appContextHolder");
            parameters.Add("ITokenProvider tokenProvider");
            if (_context.Configuration.AnyMethodRequiresUserId)
            {
                parameters.Add("ICurrentUserContext currentUserContext");
            }
        }
        else if (_context.HasHttpClient)
        {
            parameters.Add($"{_context.Configuration.HttpClient} httpClient");
            if (!_context.HasInheritedFrom)
            {
                parameters.Add("IHttpRequestExecutor executor");
            }
        }
        else
        {
            parameters.Add("IMudAppContext appContext");
            parameters.Add("IAppContextHolder appContextHolder");
        }

        // 构造函数需要接受 cacheProvider/resilienceResolver 如果派生类自己需要或基类需要
        var needsCacheParam = _context.HasCache || (_context.HasInheritedFrom && _context.Configuration.BaseHasCache);
        var needsResilienceParam = _context.HasResilience || (_context.HasInheritedFrom && _context.Configuration.BaseHasResilience);

        if (needsCacheParam)
        {
            parameters.Add("IHttpResponseCache cacheProvider");
        }
        else if (!_context.HasInheritedFrom)
        {
            // 所有非继承模式下始终接受可选的 cacheProvider，允许 DI 注入的全局缓存服务传递给执行器
            parameters.Add("IHttpResponseCache? cacheProvider = null");
        }

        if (needsResilienceParam)
        {
            parameters.Add("IResiliencePolicyResolver resilienceResolver");
        }
        else if (!_context.HasInheritedFrom)
        {
            // 所有非继承模式下始终接受可选的 resilienceResolver，允许 DI 注入的全局弹性策略服务传递给执行器
            parameters.Add("IResiliencePolicyResolver? resilienceResolver = null");
        }

        var signature = $"        public {className}({string.Join(", ", parameters)})";
        codeBuilder.Append(signature);

        if (_context.HasInheritedFrom)
        {
            var baseParameters = new List<string>
            {
                "option",
            };
            if (_context.HasTokenManager)
            {
                if (_context.Configuration.BaseHasTokenManager)
                {
                    // 基类有 TokenManager，直接传递令牌参数
                    baseParameters.Add("appManager");
                    baseParameters.Add("appContextHolder");
                    baseParameters.Add("tokenProvider");
                }
                else
                {
                    // 基类没有 TokenManager，传递 appContext 而非 appManager
                    baseParameters.Add("appManager.GetDefaultApp()");
                    baseParameters.Add("appContextHolder");
                }
            }
            else if (_context.HasHttpClient)
            {
                baseParameters.Add("httpClient");
                if (!_context.Configuration.BaseHasTokenManager)
                {
                    baseParameters.Add("executor");
                }
            }
            else
            {
                baseParameters.Add("appContext");
                baseParameters.Add("appContextHolder");
            }
            // 只传递基类构造函数能接受的参数
            if (_context.Configuration.BaseHasCache)
            {
                baseParameters.Add("cacheProvider");
            }
            if (_context.Configuration.BaseHasResilience)
            {
                baseParameters.Add("resilienceResolver");
            }
            codeBuilder.AppendLine($" : base({string.Join(", ", baseParameters)})");
        }
        else
        {
            codeBuilder.AppendLine();
        }
    }

    /// <summary>
    /// 生成构造函数体
    /// </summary>
    private void GenerateConstructorBody(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        {");

        if (!_context.HasInheritedFrom)
        {
            codeBuilder.AppendLine("            if (option == null)");
            codeBuilder.AppendLine("                throw new ArgumentNullException(nameof(option));");
            codeBuilder.AppendLine("            _jsonSerializerOptions = option.Value ?? throw new InvalidOperationException(\"JsonSerializerOptions 选项值不能为 null。\");");

            if (_context.HasTokenManager)
            {
                codeBuilder.AppendLine("            _appManager = appManager ?? throw new ArgumentNullException(nameof(appManager));");
                codeBuilder.AppendLine("            _appContextHolder = appContextHolder ?? throw new ArgumentNullException(nameof(appContextHolder));");
                codeBuilder.AppendLine("            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));");
                if (_context.Configuration.AnyMethodRequiresUserId)
                {
                    codeBuilder.AppendLine("            _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));");
                }
            }
            else if (_context.HasHttpClient)
            {
                codeBuilder.AppendLine("            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));");
            }
            else
            {
                codeBuilder.AppendLine("            _appContextHolder = appContextHolder ?? throw new ArgumentNullException(nameof(appContextHolder));");
                codeBuilder.AppendLine("            _appContextHolder.Current = appContext ?? throw new ArgumentNullException(nameof(appContext));");
            }

            if (_context.HasCache)
            {
                codeBuilder.AppendLine("            _cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));");
            }
            else
            {
                codeBuilder.AppendLine("            _cacheProvider = cacheProvider;");
            }

            if (_context.HasResilience)
            {
                codeBuilder.AppendLine("            _resilienceResolver = resilienceResolver ?? throw new ArgumentNullException(nameof(resilienceResolver));");
            }
            else
            {
                codeBuilder.AppendLine("            _resilienceResolver = resilienceResolver;");
            }

            // HttpClient 模式下，通过 DI 注入执行器实例，允许替换为自定义实现
            if (_context.HasHttpClient)
            {
                codeBuilder.AppendLine("            _executor = executor ?? throw new ArgumentNullException(nameof(executor));");
            }
        }
        else
        {
            if (_context.HasTokenManager && _context.Configuration.AnyMethodRequiresUserId)
            {
                codeBuilder.AppendLine("            _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));");
            }
            // 当基类没有 TokenManager 但派生类有时，初始化自己的令牌字段
            if (_context.HasTokenManager && !_context.Configuration.BaseHasTokenManager)
            {
                codeBuilder.AppendLine("            _appManager = appManager ?? throw new ArgumentNullException(nameof(appManager));");
                codeBuilder.AppendLine("            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));");
            }
            // 初始化派生类自己的缓存/弹性策略字段（基类没有的部分）
            if (_context.HasCache && !_context.Configuration.BaseHasCache)
            {
                codeBuilder.AppendLine("            _cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));");
            }
            if (_context.HasResilience && !_context.Configuration.BaseHasResilience)
            {
                codeBuilder.AppendLine("            _resilienceResolver = resilienceResolver ?? throw new ArgumentNullException(nameof(resilienceResolver));");
            }
        }

        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }

    /// <summary>
    /// 生成辅助方法
    /// </summary>
    private void GenerateHelperMethods(StringBuilder codeBuilder)
    {
        GenerateGetTokenManagerKeyMethod(codeBuilder);

        if (_context.HasInheritedFrom)
        {
            // 当基类没有 TokenManager 但派生类有时，需要生成令牌相关的辅助方法
            if (_context.HasTokenManager && !_context.Configuration.BaseHasTokenManager)
            {
                GenerateUseAppMethod(codeBuilder);
            }
            return;
        }

        GenerateAppContextMembers(codeBuilder);
        GenerateUseAppMethod(codeBuilder);
    }

    private void GenerateGetTokenManagerKeyMethod(StringBuilder codeBuilder)
    {
        if (!TokenMethodHelper.ShouldGenerateTokenMethods(_context))
            return;

        TokenMethodHelper.GenerateTokenManagerKeyFieldAndMethod(codeBuilder, _context);
    }

    private void GenerateAppContextMembers(StringBuilder codeBuilder)
    {
        if (_context.HasHttpClient)
            return;

        if (!_context.HasTokenManager)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 获取或设置当前的应用上下文。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine("        public IMudAppContext? Current");
            codeBuilder.AppendLine("        {");
            codeBuilder.AppendLine("            get => _appContextHolder.Current;");
            codeBuilder.AppendLine("            set => _appContextHolder.Current = value;");
            codeBuilder.AppendLine("        }");
            codeBuilder.AppendLine();

            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 创建一个应用上下文作用域，切换到指定的应用上下文，并在作用域结束时自动恢复之前的上下文。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine("        /// <param name=\"context\">要切换到的应用上下文实例。</param>");
            codeBuilder.AppendLine("        /// <returns>一个 IDisposable 对象，释放时恢复之前的上下文。</returns>");
            codeBuilder.AppendLine("        public IDisposable BeginScope(IMudAppContext context)");
            codeBuilder.AppendLine("        {");
            codeBuilder.AppendLine("            return _appContextHolder.BeginScope(context);");
            codeBuilder.AppendLine("        }");
            codeBuilder.AppendLine();
            return;
        }

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取或设置当前的应用上下文。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        public IMudAppContext? Current");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            get => _appContextHolder.Current;");
        codeBuilder.AppendLine("            set => _appContextHolder.Current = value;");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 创建一个应用上下文作用域，切换到指定的应用上下文，并在作用域结束时自动恢复之前的上下文。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"context\">要切换到的应用上下文实例。</param>");
        codeBuilder.AppendLine("        /// <returns>一个 IDisposable 对象，释放时恢复之前的上下文。</returns>");
        codeBuilder.AppendLine("        public IDisposable BeginScope(IMudAppContext context)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            return _appContextHolder.BeginScope(context);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }

    private void GenerateUseAppMethod(StringBuilder codeBuilder)
    {
        if (_context.HasHttpClient)
            return;

        if (!_context.HasTokenManager)
            return;

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 切换到指定的应用上下文。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回切换后的应用上下文。</returns>");
        codeBuilder.AppendLine("        [Obsolete(\"推荐使用 BeginScope(string) 以确保上下文自动恢复。\")]\n");
        codeBuilder.AppendLine($"        public IMudAppContext UseApp(string appKey)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            var context = _appManager.GetApp(appKey);");
        codeBuilder.AppendLine("            if(context == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到指定的应用上下文，AppKey: {appKey}\");");
        codeBuilder.AppendLine("            _appContextHolder.Current = context;");
        codeBuilder.AppendLine("            return context;");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 切换到默认的应用上下文。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回默认的应用上下文。</returns>");
        codeBuilder.AppendLine("        [Obsolete(\"推荐使用 UseDefaultAppScope() 以确保上下文自动恢复。\")]\n");
        codeBuilder.AppendLine($"        public IMudAppContext UseDefaultApp()");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            var context = _appManager.GetDefaultApp();");
        codeBuilder.AppendLine("            if(context == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到默认的应用上下文。\");");
        codeBuilder.AppendLine("            _appContextHolder.Current = context;");
        codeBuilder.AppendLine("            return context;");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

        // 新增 UseDefaultAppScope 方法
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 切换到默认应用上下文，并返回作用域以在结束时自动恢复。");
        codeBuilder.AppendLine("        /// 推荐使用此方法替代 UseDefaultApp()，确保上下文自动恢复。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>一个 IDisposable 对象，释放时恢复之前的上下文。</returns>");
        codeBuilder.AppendLine($"        public IDisposable UseDefaultAppScope()");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            var context = _appManager.GetDefaultApp();");
        codeBuilder.AppendLine("            if(context == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到默认的应用上下文。\");");
        codeBuilder.AppendLine("            return _appContextHolder.BeginScope(context);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

        GenerateBeginScopeMethod(codeBuilder);
    }

    private void GenerateBeginScopeMethod(StringBuilder codeBuilder)
    {
        if (_context.HasHttpClient)
            return;

        if (!_context.HasTokenManager)
            return;

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 创建一个应用上下文作用域，切换到指定的应用上下文，并在作用域结束时自动恢复之前的上下文。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"appKey\">应用的唯一标识符。</param>");
        codeBuilder.AppendLine("        /// <returns>一个 IDisposable 对象，释放时恢复之前的上下文。</returns>");
        codeBuilder.AppendLine("        public IDisposable BeginScope(string appKey)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            var context = _appManager.GetApp(appKey);");
        codeBuilder.AppendLine("            if(context == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到指定的应用上下文，AppKey: {appKey}\");");
        codeBuilder.AppendLine("            return _appContextHolder.BeginScope(context);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }

    private static string GetXmlSerializerFieldName(string typeName)
    {
        var safeName = typeName
            .Replace("<", "_")
            .Replace(">", "_")
            .Replace(",", "_")
            .Replace(" ", "")
            .Replace(".", "_")
            .Replace("[", "_")
            .Replace("]", "_");
        return $"_xmlSerializer_{safeName}";
    }
}
