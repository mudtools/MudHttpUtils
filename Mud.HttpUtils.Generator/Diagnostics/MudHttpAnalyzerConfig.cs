// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils;

/// <summary>
/// F6(b)：诊断的<b>一等豁免面</b>——把"项目级 / 目录级"的显式豁免从
/// <c>NoWarn</c>（整体静音，不可审计）升级为"可声明、可审计、默认仍报"的配置开关。
/// </summary>
/// <remarks>
/// <para>
/// <b>支持的两种声明方式</b>（任一命中即豁免）：
/// <list type="bullet">
/// <item><b>MSBuild 属性</b>（经 <c>build_property.*</c> 通道传入分析器）：
/// <c>&lt;MudHttpSuppressQueryTokenInjection&gt;true&lt;/MudHttpSuppressQueryTokenInjection&gt;</c>、
/// <c>&lt;MudHttpSuppressTokenManagerKeyInference&gt;true&lt;/…&gt;</c>。</item>
/// <item><b>.editorconfig 键</b>（可按目录粒度生效）：
/// <c>mud_suppress_query_token_injection = true</c>、
/// <c>mud_suppress_token_manager_key_inference = true</c>。</item>
/// </list>
/// 取值 <c>true</c> / <c>allow</c> / <c>1</c>（大小写不敏感）视为豁免；其余视为未开启。
/// </para>
/// <para>
/// <b>设计约束（不得违反）</b>：豁免必须<b>显式</b>声明，且<b>不改变默认行为</b>——
/// 未声明时诊断照常报出（"默认仍报 Warning"是安全告警的底线）。
/// 成员级豁免请优先使用 <c>[Token(Justification = "…")]</c>（理由随代码可审计）。
/// </para>
/// </remarks>
public static class MudHttpAnalyzerConfig
{
    /// <summary>MSBuild 属性：豁免 MUD005（Query/Path 令牌注入）。</summary>
    public const string QueryTokenInjectionMsBuildProperty = "MudHttpSuppressQueryTokenInjection";

    /// <summary>.editorconfig 键：豁免 MUD005（Query/Path 令牌注入）。</summary>
    public const string QueryTokenInjectionEditorConfigKey = "mud_suppress_query_token_injection";

    /// <summary>MSBuild 属性：豁免 HTTPCLIENT018（TokenManagerKey 默认推断）。</summary>
    public const string TokenManagerKeyInferenceMsBuildProperty = "MudHttpSuppressTokenManagerKeyInference";

    /// <summary>.editorconfig 键：豁免 HTTPCLIENT018（TokenManagerKey 默认推断）。</summary>
    public const string TokenManagerKeyInferenceEditorConfigKey = "mud_suppress_token_manager_key_inference";

    /// <summary>
    /// 是否豁免 <b>MUD005</b>（URL 承载令牌告警）。
    /// </summary>
    /// <param name="options">分析器配置（<c>context.Options.AnalyzerConfigOptionsProvider.GlobalOptions</c>）。</param>
    /// <returns>已显式声明豁免返回 <c>true</c>；配置不可用时返回 <c>false</c>（保守：仍告警）。</returns>
    public static bool IsQueryTokenInjectionSuppressed(AnalyzerConfigOptions options)
        => IsSuppressed(options, QueryTokenInjectionMsBuildProperty, QueryTokenInjectionEditorConfigKey);

    /// <summary>
    /// 是否豁免 <b>HTTPCLIENT018</b>（TokenManagerKey 使用默认推断值）。
    /// </summary>
    /// <param name="options">分析器配置。</param>
    /// <returns>已显式声明豁免返回 <c>true</c>；配置不可用时返回 <c>false</c>。</returns>
    public static bool IsTokenManagerKeyInferenceSuppressed(AnalyzerConfigOptions options)
        => IsSuppressed(options, TokenManagerKeyInferenceMsBuildProperty, TokenManagerKeyInferenceEditorConfigKey);

    private static bool IsSuppressed(AnalyzerConfigOptions options, string msBuildProperty, string editorConfigKey)
    {
        if (options is null)
            return false;

        // ① MSBuild 属性（编译器以 build_property.<PropertyName> 键透传给分析器）。
        if (options.TryGetValue("build_property." + msBuildProperty, out var fromMsBuild)
            && IsTrueLike(fromMsBuild))
            return true;

        // ② .editorconfig / global AnalyzerConfig（键名小写）。
        if (options.TryGetValue(editorConfigKey, out var fromEditorConfig)
            && IsTrueLike(fromEditorConfig))
            return true;

        return false;
    }

    private static bool IsTrueLike(string? value)
        => value is not null
           && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("allow", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.Ordinal));
}
