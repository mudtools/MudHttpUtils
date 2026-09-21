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

    /// <summary>
    /// 解析「令牌头名」（G8-20 · 单一事实源）。
    /// </summary>
    /// <param name="methodInfo">方法分析结果。</param>
    /// <returns>令牌头名；无法解析时返回 <c>null</c>（由调用方按场景回退 <c>Authorization</c>）。</returns>
    /// <remarks>
    /// <para>
    /// <b>规则（与令牌注入路径完全同源）</b>：
    /// ① 仅 <c>Header</c> / <c>ApiKey</c> 模式消费 <c>Name</c>（其余模式的 <c>Name</c> 语义不是头名：
    /// <c>Query</c> 是查询参数名、<c>Cookie</c> 是 Cookie 名、<c>BasicAuth</c> 由 <c>Scheme</c> 决定、
    /// <c>Path</c> 是 URL 占位符名）；
    /// ② 名称取<b>有效级</b>（方法级 <c>[Token(Name)]</c> &gt; 接口级）；
    /// ③ 兼容遗留标记：接口级 <c>[Header("Authorization")]</c> 会被
    /// <c>MethodAnalyzer.AnalyzeInterfaceAttributes</c> 记为伪属性 <c>"Header:{name}"</c>，作为回退来源。
    /// </para>
    /// <para>
    /// <b>为何必须共享</b>：本规则此前在 <c>RequestBuilder</c>（令牌注入）与 <c>HeaderParameterBinder</c>
    /// （参数绑定）各有一份，后者还额外把 <c>Name</c> 与 Scheme 字面量（<c>"Bearer"</c>/<c>"Basic"</c>）比对，
    /// 导致分支不可达、头名落回参数名（服务端取不到令牌 ⇒ 401）。抽出本方法后两处不可能再漂移，
    /// 并由 <c>EffectiveTokenFieldUsageGuardTests</c> 阻止「绕过本方法直读接口级字段」的回归。
    /// </para>
    /// </remarks>
    public static string? GetTokenHeaderName(MethodAnalysisResult methodInfo)
    {
        var mode = methodInfo.EffectiveTokenInjectionMode;
        var tokenName = methodInfo.EffectiveTokenName;
        if ((mode == HttpClientGeneratorConstants.TokenInjectionModeHeader ||
             mode == HttpClientGeneratorConstants.TokenInjectionModeApiKey) &&
            !string.IsNullOrEmpty(tokenName))
        {
            return tokenName;
        }

        var headerAttr = methodInfo.InterfaceAttributes?
            .FirstOrDefault(attr => attr.StartsWith("Header:", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(headerAttr))
            return headerAttr!.Substring("Header:".Length);

        return null;
    }

    public static void GenerateTokenManagerKeyFieldAndMethod(StringBuilder codeBuilder, GeneratorContext context)
    {
        if (!ShouldGenerateTokenMethods(context))
            return;

        var tokenManagerKey = !string.IsNullOrEmpty(context.Configuration.TokenManagerKey)
            ? context.Configuration.TokenManagerKey
            : !string.IsNullOrEmpty(context.Configuration.TokenType)
                ? context.Configuration.TokenType
                : TokenHelper.GetDefaultTokenType();

        // 从 TokenType 自动推断 TokenManagerKey 是正常且推荐的行为，无需发出 #warning。
        // TokenManagerKey 仅在需要将多个不同 TokenType 映射到同一令牌管理器时才需显式指定。

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 令牌管理器查找键。保留供子类覆盖使用。");
        codeBuilder.AppendLine("        /// 新代码应使用 GetTokenAsync(tokenManagerKey, ...) 方法指定查找键。");
        codeBuilder.AppendLine("        /// </summary>");
        codeBuilder.AppendLine($"        private readonly string _tokenManagerKey = \"{StringEscapeHelper.EscapeString(tokenManagerKey)}\";");
        codeBuilder.AppendLine();

        // 基类有 TokenManager 时使用 override，否则使用 virtual
        string accessibility;
        if (context.HasInheritedFrom && context.Configuration.BaseHasTokenManager)
            accessibility = "override";
        else
            accessibility = "virtual";

        codeBuilder.AppendLine("        /// <summary>");
        codeBuilder.AppendLine("        /// 获取用于远程API访问的Token令牌管理器查找键。");
        codeBuilder.AppendLine("        /// 保留供子类覆盖使用。新代码应使用 GetTokenAsync(tokenManagerKey, ...) 方法指定查找键。");
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
