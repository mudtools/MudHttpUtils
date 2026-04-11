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
/// 访问令牌生成器，用于生成用户令牌相关的属性和方法
/// </summary>
internal class AccessTokenGenerator : ICodeFragmentGenerator
{
    private readonly GeneratorContext _context;

    public AccessTokenGenerator(GeneratorContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 生成用户访问令牌相关的属性和方法
    /// </summary>
    public void Generate(StringBuilder codeBuilder, GeneratorContext context)
    {
        // HttpClient 模式下不生成任何 Token 相关代码
        if (_context.HasHttpClient)
            return;

        // 生成 CurrentUserId 属性
        GenerateCurrentUserIdProperty(codeBuilder);

        // 生成 GetTokenAsync 方法
        GenerateGetTokenAsyncMethod(codeBuilder);
    }

    /// <summary>
    /// 生成 CurrentUserId 公共属性
    /// </summary>
    private void GenerateCurrentUserIdProperty(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 当前用户ID，用于用户令牌认证");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        public string? CurrentUserId { get; set; }");
        codeBuilder.AppendLine();
    }

    /// <summary>
    /// 生成 GetTokenAsync 方法（用户令牌版本）
    /// </summary>
    private void GenerateGetTokenAsyncMethod(StringBuilder codeBuilder)
    {
        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取当前用户的访问令牌。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回用户访问令牌</returns>");
        codeBuilder.AppendLine($"        {_context.GetTokenAsyncAccessibility} async Task<string> GetTokenAsync()");
        codeBuilder.AppendLine("        {");
        codeBuilder.AppendLine("            if(_appContext == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到当前服务的应用上下文。\");");
        codeBuilder.AppendLine("            var tokenType = GetTokenType();");
        codeBuilder.AppendLine("            var tokenManager = _appContext.GetTokenManager(tokenType);");
        codeBuilder.AppendLine("            if(tokenManager == null)");
        codeBuilder.AppendLine("                throw new InvalidOperationException($\"无法找到当前服务的令牌管理器，TokenType: {tokenType}\");");
        codeBuilder.AppendLine("            if(string.IsNullOrEmpty(CurrentUserId))");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                var token = await tokenManager.GetTokenAsync();");
        codeBuilder.AppendLine("                if(string.IsNullOrEmpty(token))");
        codeBuilder.AppendLine("                    throw new InvalidOperationException($\"无法获取到有效的访问令牌，TokenType: {tokenType}\");");
        codeBuilder.AppendLine("                return token!;");
        codeBuilder.AppendLine("            }");
        codeBuilder.AppendLine("            if(tokenManager is IUserTokenManager userTokenManager)");
        codeBuilder.AppendLine("            {");
        codeBuilder.AppendLine("                var token = await userTokenManager.GetTokenAsync(CurrentUserId);");
        codeBuilder.AppendLine("                if(string.IsNullOrEmpty(token))");
        codeBuilder.AppendLine("                    throw new InvalidOperationException($\"无法获取到有效的访问令牌，TokenType: {tokenType}\");");
        codeBuilder.AppendLine("                return token!;");
        codeBuilder.AppendLine("            }");
        codeBuilder.AppendLine("            throw new InvalidOperationException($\"当前令牌管理器不是IUserTokenManager，获取用户令牌失败。\");");
        codeBuilder.AppendLine("        }");
        codeBuilder.AppendLine();
    }
}
