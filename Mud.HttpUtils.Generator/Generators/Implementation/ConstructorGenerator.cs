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
        // HttpClient 模式下不生成 Token 相关字段
        if (!_context.HasHttpClient && (!string.IsNullOrEmpty(_context.Configuration.TokenType) || _context.HasTokenManager))
        {
            var tokenType = string.IsNullOrEmpty(_context.Configuration.TokenType)
                ? TokenHelper.GetDefaultTokenType()
                : _context.Configuration.TokenType;
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// Token类型，用于标识使用的Token类型。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        private readonly string _tokenType = \"{tokenType}\";");
        }

        if (_context.HasInheritedFrom) return;


        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 用于JSON内容序列化与反序列化操作的<see cref = \"JsonSerializerOptions\"/> 参数实例。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly JsonSerializerOptions _jsonSerializerOptions;");

        if (_context.HasTokenManager)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 应用上下文，用于获取HttpClient和Token管理器。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}IMudAppContext _appContext;");

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
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine($"        {_context.FieldAccessibility}IMudAppContext _appContext;");
        }


        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 用于HttpClient客户端操作的内容类型。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}readonly string _defaultContentType = \"{_context.Configuration.DefaultContentType}\";");
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
            codeBuilder.AppendLine("            _jsonSerializerOptions = option.Value;");

            if (_context.HasTokenManager)
            {
                codeBuilder.AppendLine("            _appManager = appManager ?? throw new ArgumentNullException(nameof(appManager));");
                codeBuilder.AppendLine("            _appContext = appManager.GetDefaultApp();");
            }
            else if (_context.HasHttpClient)
            {
                codeBuilder.AppendLine("            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));");
            }
            else
            {
                codeBuilder.AppendLine("            _appContext = appContext ?? throw new ArgumentNullException(nameof(appContext));");
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

        GenerateGetMediaTypeMethod(codeBuilder);
        GenerateUseAppMethod(codeBuilder);
    }

    /// <summary>
    /// 生成 GetMediaType 方法
    /// </summary>
    private void GenerateGetMediaTypeMethod(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 从Content-Type字符串中提取媒体类型部分，去除字符集信息。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"contentType\">完整的Content-Type字符串</param>");
        codeBuilder.AppendLine("        /// <returns>媒体类型部分</returns>");
        codeBuilder.AppendLine($"        {_context.FieldAccessibility}string GetMediaType(string contentType)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            if (string.IsNullOrEmpty(contentType))");
        codeBuilder.AppendLine("                return _defaultContentType;");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("            // Content-Type可能包含字符集信息，如 \"application/json; charset=utf-8\"");
        codeBuilder.AppendLine("            // 需要分号前的媒体类型部分");
        codeBuilder.AppendLine("            var semicolonIndex = contentType.IndexOf(';');");
        codeBuilder.AppendLine("            if (semicolonIndex >= 0)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                return contentType.Substring(0, semicolonIndex).Trim();");
        codeBuilder.AppendLine("            }");
        codeBuilder.AppendLine();
        codeBuilder.AppendLine("            return contentType.Trim();");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }


    private void GenerateGetTokenTypeMethod(StringBuilder codeBuilder)
    {
        // HttpClient 模式下不生成 Token 相关方法
        if (_context.HasHttpClient)
            return;

        if (string.IsNullOrEmpty(_context.Configuration.TokenType) && string.IsNullOrEmpty(_context.Configuration.TokenManager))
            return;

        string accessibility = _context.Configuration.IsAbstract ? "virtual" : "override";
        if (!_context.HasInheritedFrom && !_context.Configuration.IsAbstract)
            accessibility = "virtual";

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取用于远程API访问的Token令牌类型。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回Token令牌类型。</returns>");
        codeBuilder.AppendLine($"        protected {accessibility} string GetTokenType() => _tokenType;");
        codeBuilder.AppendLine();
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
        codeBuilder.AppendLine("            _appContext = _appManager.GetApp(appKey);");
        codeBuilder.AppendLine("            if(_appContext == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到指定的应用上下文，AppKey: {appKey}\");");
        codeBuilder.AppendLine("            return _appContext;");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 切换到默认的应用上下文。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回默认的应用上下文。</returns>");
        codeBuilder.AppendLine($"        public IMudAppContext UseDefaultApp()");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            _appContext = _appManager.GetDefaultApp();");
        codeBuilder.AppendLine("            if(_appContext == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到默认的应用上下文。\");");
        codeBuilder.AppendLine("            return _appContext;");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();

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
        // HttpClient 模式下不生成任何 Token 相关方法
        if (_context.HasHttpClient)
            return;

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取当前应用的访问令牌。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回当前应用的访问令牌。</returns>");
        codeBuilder.AppendLine($"        {_context.GetTokenAsyncAccessibility} async Task<string> GetTokenAsync()");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            if(_appContext == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到当前服务的应用上下文。\");");
        codeBuilder.AppendLine("            var tokenType = GetTokenType();");
        codeBuilder.AppendLine("            var tokenManager = _appContext.GetTokenManager(tokenType);");
        codeBuilder.AppendLine("            if(tokenManager == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到当前服务的令牌管理器，TokenType: {tokenType}\");");
        codeBuilder.AppendLine("            var token = await tokenManager.GetTokenAsync();");
        codeBuilder.AppendLine("            if(string.IsNullOrEmpty(token))");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法获取到有效的访问令牌，TokenType: {tokenType}\");");
        codeBuilder.AppendLine("            return token;");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }
}
