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
        if (_context.HasInheritedFrom) return;


        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 用于JSON内容序列化与反序列化操作的<see cref = \"JsonSerializerOptions\"/> 参数实例。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly JsonSerializerOptions _jsonSerializerOptions;");

        if (_context.HasTokenManager)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 应用上下文，用于获取HttpClient和Token管理器。");
            codeBuilder.AppendLine("        /// 使用 AsyncLocal 确保异步上下文中的线程安全。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly AsyncLocal<IMudAppContext?> _appContext = new();");

            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine($"        /// 用于HttpClient客户端操作操作使用的的<see cref = \"{_context.Configuration.TokenManagerType}\"/> 令牌管理实例。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly {_context.Configuration.TokenManagerType} _appManager;");
        }
        else if (_context.HasHttpClient)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine($"        /// 用于HttpClient客户端操作的<see cref = \"{_context.Configuration.HttpClient}\"/> 实例。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly {_context.Configuration.HttpClient} _httpClient;");
        }
        else
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 应用上下文，用于获取HttpClient。");
            codeBuilder.AppendLine("        /// 使用 AsyncLocal 确保异步上下文中的线程安全。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly AsyncLocal<IMudAppContext?> _appContext = new();");
        }


        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 用于HttpClient客户端操作的内容类型。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly string _defaultContentType = \"{_context.Configuration.DefaultContentType}\";");

        if (_context.HasCache)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// HTTP响应缓存提供器，用于缓存接口方法的响应结果。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly IHttpResponseCache _cacheProvider;");

            if (!_context.Configuration.IsUserAccessToken)
            {
                codeBuilder.AppendLine();
                codeBuilder.AppendLine("        /// <summary>");
                codeBuilder.AppendLine("        /// 当前用户ID，用于缓存键的用户隔离。");
                codeBuilder.AppendLine("        /// </summary>");
                codeBuilder.AppendLine("        public string? CurrentUserId { get; set; }");
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
                propLine += $" = {property.DefaultValue}";
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
        }
        else if (_context.HasHttpClient)
        {
            codeBuilder.AppendLine($"        /// <param name=\"httpClient\">HttpClient实例</param>");
        }
        else
        {
            codeBuilder.AppendLine("        /// <param name=\"appContext\">应用上下文</param>");
        }

        if (_context.HasCache)
        {
            codeBuilder.AppendLine("        /// <param name=\"cacheProvider\">HTTP响应缓存提供器</param>");
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
        }
        else if (_context.HasHttpClient)
        {
            parameters.Add($"{_context.Configuration.HttpClient} httpClient");
        }
        else
        {
            parameters.Add("IMudAppContext appContext");
        }

        if (_context.HasCache)
        {
            parameters.Add("IHttpResponseCache cacheProvider");
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
                baseParameters.Add("appManager");
            }
            else if (_context.HasHttpClient)
            {
                baseParameters.Add("httpClient");
            }
            else
            {
                baseParameters.Add("appContext");
            }
            if (_context.HasCache)
            {
                baseParameters.Add("cacheProvider");
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
            codeBuilder.AppendLine("            _jsonSerializerOptions = option.Value ?? throw new ArgumentNullException(nameof(option));");

            if (_context.HasTokenManager)
            {
                codeBuilder.AppendLine("            _appManager = appManager ?? throw new ArgumentNullException(nameof(appManager));");
                codeBuilder.AppendLine("            _appContext.Value = appManager.GetDefaultApp();");
            }
            else if (_context.HasHttpClient)
            {
                codeBuilder.AppendLine("            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));");
            }
            else
            {
                codeBuilder.AppendLine("            _appContext.Value = appContext ?? throw new ArgumentNullException(nameof(appContext));");
            }

            if (_context.HasCache)
            {
                codeBuilder.AppendLine("            _cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));");
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
        GenerateGetTokenTypeMethod(codeBuilder);

        if (_context.HasInheritedFrom) return;

        GenerateUseAppMethod(codeBuilder);
    }



    private void GenerateGetTokenTypeMethod(StringBuilder codeBuilder)
    {
        if (!TokenMethodHelper.ShouldGenerateTokenMethods(_context))
            return;

        TokenMethodHelper.GenerateGetTokenTypeFieldAndMethod(codeBuilder, _context);
    }

    private void GenerateUseAppMethod(StringBuilder codeBuilder)
    {
        // HttpClient 模式下不生成任何 Token 相关方法
        if (_context.HasHttpClient)
            return;

        if (!_context.HasTokenManager)
            return;

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 切换到指定的应用上下文。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回切换后的应用上下文。</returns>");
        codeBuilder.AppendLine($"        public IMudAppContext UseApp(string appKey)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            var context = _appManager.GetApp(appKey);");
        codeBuilder.AppendLine("            if(context == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到指定的应用上下文，AppKey: {appKey}\");");
        codeBuilder.AppendLine("            _appContext.Value = context;");
        codeBuilder.AppendLine("            return context;");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 切换到默认的应用上下文。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回默认的应用上下文。</returns>");
        codeBuilder.AppendLine($"        public IMudAppContext UseDefaultApp()");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            var context = _appManager.GetDefaultApp();");
        codeBuilder.AppendLine("            if(context == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到默认的应用上下文。\");");
        codeBuilder.AppendLine("            _appContext.Value = context;");
        codeBuilder.AppendLine("            return context;");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

        GenerateBeginScopeMethod(codeBuilder);

        // 用户令牌由 AccessTokenGenerator 生成，其他令牌在这里生成通用版本
        if (_context.Configuration.IsUserAccessToken)
            return;

        GenerateGetTokenAsyncMethod(codeBuilder);
    }

    /// <summary>
    /// 生成通用的 GetTokenAsync 方法（租户令牌、应用令牌）
    /// </summary>
    private void GenerateGetTokenAsyncMethod(StringBuilder codeBuilder)
    {
        if (_context.HasHttpClient)
            return;

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取当前应用的访问令牌。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回当前应用的访问令牌。</returns>");
        TokenMethodHelper.GenerateGetTokenPreamble(codeBuilder, _context.GetTokenAsyncAccessibility);
        codeBuilder.AppendLine("            var token = await tokenManager.GetTokenAsync();");
        codeBuilder.AppendLine("            if(string.IsNullOrEmpty(token))");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法获取到有效的访问令牌，TokenType: {tokenType}\");");
        codeBuilder.AppendLine("            return token!;");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
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
        codeBuilder.AppendLine("            var previous = _appContext.Value;");
        codeBuilder.AppendLine("            UseApp(appKey);");
        codeBuilder.AppendLine("            return new AppContextScope(previous, _appContext);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 应用上下文作用域，释放时恢复之前的上下文。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        private sealed class AppContextScope : IDisposable");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            private readonly IMudAppContext? _previous;");
        codeBuilder.AppendLine("            private readonly AsyncLocal<IMudAppContext?> _context;");
        codeBuilder.AppendLine("            private volatile bool _disposed;");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("            public AppContextScope(IMudAppContext? previous, AsyncLocal<IMudAppContext?> context)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                _previous = previous;");
        codeBuilder.AppendLine("                _context = context;");
        codeBuilder.AppendLine("            }");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("            public void Dispose()");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                if (_disposed) return;");
        codeBuilder.AppendLine("                _disposed = true;");
        codeBuilder.AppendLine("                _context.Value = _previous;");
        codeBuilder.AppendLine("            }");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }
}
