// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generators.Implementation;

using Mud.HttpUtils.Generators.Context;

internal static class TokenMethodHelper
{
    public static bool ShouldGenerateTokenMethods(GeneratorContext context)
    {
        return !context.HasHttpClient && context.HasTokenManager;
    }

    public static void GenerateGetTokenTypeFieldAndMethod(StringBuilder codeBuilder, GeneratorContext context)
    {
        if (!ShouldGenerateTokenMethods(context))
            return;

        var tokenManagerKey = !string.IsNullOrEmpty(context.Configuration.TokenManagerKey)
            ? context.Configuration.TokenManagerKey
            : !string.IsNullOrEmpty(context.Configuration.TokenType)
                ? context.Configuration.TokenType
                : TokenHelper.GetDefaultTokenType();

        codeBuilder.AppendLine($"        private readonly string _tokenManagerKey = \"{tokenManagerKey}\";");
        codeBuilder.AppendLine();

        string accessibility = context.Configuration.IsAbstract ? "virtual" : "override";
        if (!context.HasInheritedFrom && !context.Configuration.IsAbstract)
            accessibility = "virtual";

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取用于远程API访问的Token令牌管理器查找键。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine("        /// <returns>返回Token令牌管理器查找键。</returns>");
        codeBuilder.AppendLine($"        protected {accessibility} string GetTokenManagerKey() => _tokenManagerKey;");
        codeBuilder.AppendLine();
    }

    /// <summary>
    /// 判断方法是否需要 UserId。
    /// 优先使用方法级 RequiresUserId，其次使用接口级 RequiresUserId，
    /// 最后根据 IsUserAccessToken 自动推断。
    /// </summary>
    public static bool MethodRequiresUserId(GeneratorContext context, MethodAnalysisResult methodInfo)
    {
        if (methodInfo.MethodRequiresUserId.HasValue)
            return methodInfo.MethodRequiresUserId.Value;

        if (context.Configuration.RequiresUserId.HasValue)
            return context.Configuration.RequiresUserId.Value;

        return context.Configuration.IsUserAccessToken;
    }

    /// <summary>
    /// 获取方法的 TokenManagerKey。
    /// 优先使用方法级 TokenManagerKey，其次使用接口级 TokenManagerKey，
    /// 最后使用 TokenType。
    /// </summary>
    public static string GetMethodTokenManagerKey(GeneratorContext context, MethodAnalysisResult methodInfo)
    {
        if (!string.IsNullOrEmpty(methodInfo.MethodTokenManagerKey))
            return methodInfo.MethodTokenManagerKey!;

        if (!string.IsNullOrEmpty(context.Configuration.TokenManagerKey))
            return context.Configuration.TokenManagerKey!;

        return !string.IsNullOrEmpty(context.Configuration.TokenType)
            ? context.Configuration.TokenType!
            : TokenHelper.GetDefaultTokenType();
    }
}
