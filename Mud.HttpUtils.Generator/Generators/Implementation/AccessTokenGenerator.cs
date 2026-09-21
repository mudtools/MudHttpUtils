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
/// 访问令牌生成器，用于生成令牌相关的属性和方法
/// </summary>
internal class AccessTokenGenerator : ICodeFragmentGenerator
{
    private readonly GeneratorContext _context;

    public AccessTokenGenerator(GeneratorContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 生成令牌相关的属性和方法
    /// </summary>
    public void Generate(StringBuilder codeBuilder, GeneratorContext context)
    {
        if (_context.HasHttpClient)
            return;

        GenerateGetTokenAsyncMethod(codeBuilder);
        GenerateGetTokenAsyncNoParamMethod(codeBuilder);
        GenerateTokenIdentityResolvers(codeBuilder);

        // F-02 连带缺陷（T-01 编译断言检出）：ApplyHmacSignatureAsync 内部调用 GetApiKeyAsync("HmacSecretKey")，
        // 但该成员此前仅在 HasApiKeyInjection 时生成——HmacSignature-only 接口编译报 CS0103
        //（此前被 CS0841 掩盖，从未有 HMAC 全接口编译验证）。HMAC 签名依赖 ApiKey 通道获取签名密钥，二者取并集。
        if (_context.HasApiKeyInjection || _context.HasHmacSignatureInjection)
            GenerateGetApiKeyAsyncMethod(codeBuilder);

        if (_context.HasHmacSignatureInjection)
            GenerateApplyHmacSignatureAsyncMethod(codeBuilder);
    }

    /// <summary>
    /// 生成令牌身份解析接缝（[F-Identity]）：<c>ResolveTokenManagerKey</c> / <c>ResolveTokenUserId</c>。
    /// </summary>
    /// <remarks>
    /// 接口级令牌键与用户标识的方法体调用统一经此接缝解析：独立类与基类的默认实现为恒等透传（行为不变）；
    /// 继承模式（InheritedFrom）下派生类覆盖接缝后，从基接口继承的方法在派生类实例上
    /// 即以派生身份（如用户态接口的 UserAccessToken + 当前用户）取令牌，
    /// 修复"用户态接口继承方法实际使用基类租户令牌"的身份漂移缺陷。
    /// 方法级显式 [Token] 键不经过接缝（显式选择始终优先）。
    /// </remarks>
    private void GenerateTokenIdentityResolvers(StringBuilder codeBuilder)
    {
        var accessibility = _context.GetTokenAsyncAccessibility;
        // GetTokenAsyncAccessibility 的值域为 "public override" / "public virtual"，须以 Contains 判定重写形态。
        var isOverride = accessibility.Contains("override");
        // 仅当派生类显式配置了令牌身份（TokenType / TokenManagerKey）时才覆盖接缝，
        // 避免未显式配置身份的派生类意外改变从基接口继承方法的令牌身份（向后兼容）。
        var hasExplicitTokenIdentity = !string.IsNullOrEmpty(_context.Configuration.TokenManagerKey)
            || !string.IsNullOrEmpty(_context.Configuration.TokenType);
        // 用户标识覆盖要求派生类存在 _currentUserContext 字段（AnyMethodRequiresUserId 保证其已发射）。
        var hasCurrentUserContext = _context.Configuration.AnyMethodRequiresUserId;

        if (isOverride && hasExplicitTokenIdentity)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 解析本次请求的令牌管理器查找键（[F-Identity] 令牌身份接缝）。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine("        /// <param name=\"defaultTokenManagerKey\">由声明接口解析出的默认查找键。</param>");
            codeBuilder.AppendLine("        /// <returns>实际用于定位令牌管理器的查找键。</returns>");
            codeBuilder.AppendLine($"        {accessibility} string ResolveTokenManagerKey(string defaultTokenManagerKey) => _tokenManagerKey;");
            codeBuilder.AppendLine();
        }
        else if (!isOverride)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 解析本次请求的令牌管理器查找键（[F-Identity] 令牌身份接缝）。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine("        /// <param name=\"defaultTokenManagerKey\">由声明接口解析出的默认查找键。</param>");
            codeBuilder.AppendLine("        /// <returns>实际用于定位令牌管理器的查找键。</returns>");
            codeBuilder.AppendLine($"        {accessibility} string ResolveTokenManagerKey(string defaultTokenManagerKey) => defaultTokenManagerKey;");
            codeBuilder.AppendLine();
        }
        // isOverride 但未显式配置身份时不发射覆盖：继承基类恒等透传，与基类行为保持一致。

        if (isOverride && hasExplicitTokenIdentity && hasCurrentUserContext)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 解析本次请求的用户标识（[F-Identity] 令牌身份接缝）。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine("        /// <param name=\"defaultUserId\">由声明接口解析出的默认用户标识。</param>");
            codeBuilder.AppendLine("        /// <returns>实际用于获取用户令牌的用户标识。</returns>");
            codeBuilder.AppendLine($"        {accessibility} string? ResolveTokenUserId(string? defaultUserId) => _currentUserContext.UserId;");
            codeBuilder.AppendLine();
        }
        else if (!isOverride)
        {
            codeBuilder.AppendLine("        /// <summary>");
            codeBuilder.AppendLine("        /// 解析本次请求的用户标识（[F-Identity] 令牌身份接缝）。");
            codeBuilder.AppendLine("        /// </summary>");
            codeBuilder.AppendLine("        /// <param name=\"defaultUserId\">由声明接口解析出的默认用户标识。</param>");
            codeBuilder.AppendLine("        /// <returns>实际用于获取用户令牌的用户标识。</returns>");
            codeBuilder.AppendLine($"        {accessibility} string? ResolveTokenUserId(string? defaultUserId) => defaultUserId;");
            codeBuilder.AppendLine();
        }
    }

    /// <summary>
    /// 生成统一的 GetTokenAsync 方法，通过 ITokenProvider 获取令牌
    /// </summary>
    private void GenerateGetTokenAsyncMethod(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取访问令牌。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"tokenManagerKey\">令牌管理器查找键。</param>");
        codeBuilder.AppendLine("        /// <param name=\"userId\">用户ID（可选）。</param>");
        codeBuilder.AppendLine("        /// <param name=\"scopes\">令牌作用域数组（可选）。</param>");
        codeBuilder.AppendLine("        /// <param name=\"cancellationToken\">取消令牌。</param>");
        codeBuilder.AppendLine("        /// <returns>返回访问令牌</returns>");
        codeBuilder.AppendLine("        private async Task<string> GetTokenAsync(string tokenManagerKey, string? userId = null, string[]? scopes = null, CancellationToken cancellationToken = default)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            // 查询方法不应有写入副作用。移除 _appContextHolder.Current = appContext 赋值，");
        codeBuilder.AppendLine("            // 改为只读回退到默认应用。上下文切换由构造函数初始化或显式 UseApp 调用负责。");
        codeBuilder.AppendLine("            var appContext = _appContextHolder.Current ?? _tokenManager.GetDefaultApp();");
        codeBuilder.AppendLine("            var request = new TokenRequest");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                TokenManagerKey = tokenManagerKey,");
        codeBuilder.AppendLine("                UserId = userId,");
        codeBuilder.AppendLine("                Scopes = scopes");
        codeBuilder.AppendLine("            };");
        codeBuilder.AppendLine("            return await _tokenProvider.GetTokenAsync(appContext, request, cancellationToken).ConfigureAwait(false);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }

    /// <summary>
    /// 生成 GetTokenAsync() 的无参实现，委托给带参的 GetTokenAsync
    /// </summary>
    private void GenerateGetTokenAsyncNoParamMethod(StringBuilder codeBuilder)
    {
        var tokenManagerKey = !string.IsNullOrEmpty(_context.Configuration.TokenManagerKey)
            ? _context.Configuration.TokenManagerKey
            : !string.IsNullOrEmpty(_context.Configuration.TokenType)
                ? _context.Configuration.TokenType
                : TokenHelper.GetDefaultTokenType();

        var requiresUserId = _context.Configuration.RequiresUserId ?? _context.Configuration.IsUserAccessToken;
        var userIdArg = requiresUserId
            ? "_currentUserContext.UserId"
            : "null";

        var accessibility = _context.GetTokenAsyncAccessibility;

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 异步获取当前应用上下文的访问令牌。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>包含访问令牌的字符串任务。</returns>");
        codeBuilder.AppendLine($"        {accessibility} async Task<string> GetTokenAsync()");
        codeBuilder.AppendLine("        {");
        // [F-Identity] 无参便捷入口同样经身份接缝解析，避免派生类（用户态）仍以基接口字面量键取令牌。
        codeBuilder.AppendLine($"            return await GetTokenAsync(ResolveTokenManagerKey(\"{StringEscapeHelper.EscapeString(tokenManagerKey)}\"), ResolveTokenUserId({userIdArg})).ConfigureAwait(false);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }

    private void GenerateGetApiKeyAsyncMethod(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取 API Key。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"keyName\">API Key 名称（可选）。</param>");
        codeBuilder.AppendLine("        /// <returns>返回 API Key</returns>");
        codeBuilder.AppendLine("        private async Task<string> GetApiKeyAsync(string? keyName = null)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            if (keyName != null && string.IsNullOrWhiteSpace(keyName))");
        codeBuilder.AppendLine("                throw new System.ArgumentException(\"API Key name cannot be whitespace.\", nameof(keyName));");
        codeBuilder.AppendLine("            // 查询方法不应有写入副作用。移除 _appContextHolder.Current = appContext 赋值，");
        codeBuilder.AppendLine("            // 改为只读回退到默认应用。上下文切换由构造函数初始化或显式 UseApp 调用负责。");
        codeBuilder.AppendLine("            var appContext = _appContextHolder.Current ?? _tokenManager.GetDefaultApp();");
        codeBuilder.AppendLine("            var apiKeyProvider = appContext.GetService<IApiKeyProvider>();");
        codeBuilder.AppendLine("            if(apiKeyProvider == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException(\"无法找到 IApiKeyProvider 服务，请先注册 ApiKey 提供器。\");");
        codeBuilder.AppendLine("            return await apiKeyProvider.GetApiKeyAsync(keyName).ConfigureAwait(false);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }

    private void GenerateApplyHmacSignatureAsyncMethod(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 为 HTTP 请求应用 HMAC 签名。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <param name=\"request\">HTTP 请求消息。</param>");
        codeBuilder.AppendLine("        private async Task ApplyHmacSignatureAsync(HttpRequestMessage request)");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            // 查询方法不应有写入副作用。移除 _appContextHolder.Current = appContext 赋值，");
        codeBuilder.AppendLine("            // 改为只读回退到默认应用。上下文切换由构造函数初始化或显式 UseApp 调用负责。");
        codeBuilder.AppendLine("            var appContext = _appContextHolder.Current ?? _tokenManager.GetDefaultApp();");
        codeBuilder.AppendLine("            var hmacProvider = appContext.GetService<IHmacSignatureProvider>();");
        codeBuilder.AppendLine("            if(hmacProvider == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException(\"无法找到 IHmacSignatureProvider 服务，请先注册 HMAC 签名提供器。\");");
        codeBuilder.AppendLine("            var secretKey = await GetApiKeyAsync(\"HmacSecretKey\").ConfigureAwait(false);");
        codeBuilder.AppendLine("            var signature = await hmacProvider.GenerateSignatureAsync(request, secretKey).ConfigureAwait(false);");
        codeBuilder.AppendLine("            request.Headers.Add(\"X-Hmac-Signature\", signature);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }
}
