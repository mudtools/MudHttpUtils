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
        GenerateIAppContextSwitcherGetTokenAsyncMethod(codeBuilder);

        if (_context.HasApiKeyInjection)
            GenerateGetApiKeyAsyncMethod(codeBuilder);

        if (_context.HasHmacSignatureInjection)
            GenerateApplyHmacSignatureAsyncMethod(codeBuilder);
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
        codeBuilder.AppendLine("            var appContext = _appContextSwitcher.Current;");
        codeBuilder.AppendLine("            if(appContext == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到当前服务的应用上下文。\");");
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
    /// 生成 IAppContextSwitcher.GetTokenAsync() 的无参实现，委托给带参的 GetTokenAsync
    /// </summary>
    private void GenerateIAppContextSwitcherGetTokenAsyncMethod(StringBuilder codeBuilder)
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
        codeBuilder.AppendLine("        /// 异步获取当前应用上下文的访问令牌（IAppContextSwitcher 实现）。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>包含访问令牌的字符串任务。</returns>");
        codeBuilder.AppendLine($"        {accessibility} async Task<string> GetTokenAsync()");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine($"            return await GetTokenAsync(\"{tokenManagerKey}\", {userIdArg}).ConfigureAwait(false);");
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
        codeBuilder.AppendLine("            var appContext = _appContextSwitcher.Current;");
        codeBuilder.AppendLine("            if(appContext == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到当前服务的应用上下文。\");");
        codeBuilder.AppendLine("            var apiKeyProvider = appContext.GetService<IApiKeyProvider>();");
        codeBuilder.AppendLine("            if(apiKeyProvider == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到 IApiKeyProvider 服务，请先注册 ApiKey 提供器。\");");
        codeBuilder.AppendLine("            return await apiKeyProvider.GetApiKeyAsync(keyName);");
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
        codeBuilder.AppendLine("            var appContext = _appContextSwitcher.Current;");
        codeBuilder.AppendLine("            if(appContext == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到当前服务的应用上下文。\");");
        codeBuilder.AppendLine("            var hmacProvider = appContext.GetService<IHmacSignatureProvider>();");
        codeBuilder.AppendLine("            if(hmacProvider == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到 IHmacSignatureProvider 服务，请先注册 HMAC 签名提供器。\");");
        codeBuilder.AppendLine("            var secretKey = await GetApiKeyAsync(\"HmacSecretKey\");");
        codeBuilder.AppendLine("            var signature = await hmacProvider.GenerateSignatureAsync(request, secretKey);");
        codeBuilder.AppendLine("            request.Headers.Add(\"X-Hmac-Signature\", signature);");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }
}
