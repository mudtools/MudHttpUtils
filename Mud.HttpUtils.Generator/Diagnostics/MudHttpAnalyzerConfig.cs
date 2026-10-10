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
    /// <summary>
    /// MSBuild 属性名：豁免 MUD005（Query/Path 令牌注入）。
    /// </summary>
    /// <remarks>
    /// 必须在 <c>build/Mud.HttpUtils.Generator.props</c> 注册为 <c>CompilerVisibleProperty</c>
    /// （已注册），否则 MSBuild 属性不会进入 <c>build_property.*</c> 通道、开关静默失效。
    /// 判定代码使用<b>字面量</b>键（见 <see cref="IsSuppressed"/> 的备注）。
    /// </remarks>
    public const string QueryTokenInjectionMsBuildProperty = "MudHttpSuppressQueryTokenInjection";

    /// <summary>.editorconfig 键：豁免 MUD005（Query/Path 令牌注入）。</summary>
    public const string QueryTokenInjectionEditorConfigKey = "mud_suppress_query_token_injection";

    /// <summary>
    /// MSBuild 属性名：豁免 HTTPCLIENT018（TokenManagerKey 默认推断）。
    /// </summary>
    /// <remarks>同 <see cref="QueryTokenInjectionMsBuildProperty"/>：须注册 <c>CompilerVisibleProperty</c>（已注册）。</remarks>
    public const string TokenManagerKeyInferenceMsBuildProperty = "MudHttpSuppressTokenManagerKeyInference";

    /// <summary>.editorconfig 键：豁免 HTTPCLIENT018（TokenManagerKey 默认推断）。</summary>
    public const string TokenManagerKeyInferenceEditorConfigKey = "mud_suppress_token_manager_key_inference";

    /// <summary>
    /// 是否豁免 <b>MUD005</b>（URL 承载令牌告警）。
    /// </summary>
    /// <param name="globalOptions">全局分析器配置（<c>context.Options.AnalyzerConfigOptionsProvider.GlobalOptions</c>）。</param>
    /// <param name="treeOptions">
    /// 语法树粒度配置（<c>GetOptions(node.SyntaxTree)</c>）；仅 <b>.editorconfig 键</b>需要它 ——
    /// 普通 <c>.editorconfig</c> 的键<b>不会</b>出现在 <c>GlobalOptions</c> 中（后者只含 <c>.globalconfig</c>
    /// 与 <c>build_property.*</c>），传入 null 时退化为只认 MSBuild 属性（调用方若可拿到语法树应传入）。
    /// </param>
    /// <returns>已显式声明豁免返回 <c>true</c>；配置不可用时返回 <c>false</c>（保守：仍告警）。</returns>
    public static bool IsQueryTokenInjectionSuppressed(
        AnalyzerConfigOptions globalOptions,
        AnalyzerConfigOptions? treeOptions = null)
        => IsSuppressed(globalOptions, treeOptions,
            "build_property.MudHttpSuppressQueryTokenInjection", QueryTokenInjectionEditorConfigKey);

    /// <summary>
    /// 是否豁免 <b>HTTPCLIENT018</b>（TokenManagerKey 使用默认推断值）。
    /// </summary>
    /// <param name="globalOptions">全局分析器配置。</param>
    /// <param name="treeOptions">语法树粒度配置（仅 .editorconfig 键需要，语义同 <see cref="IsQueryTokenInjectionSuppressed"/>）。</param>
    /// <returns>已显式声明豁免返回 <c>true</c>；配置不可用时返回 <c>false</c>。</returns>
    public static bool IsTokenManagerKeyInferenceSuppressed(
        AnalyzerConfigOptions globalOptions,
        AnalyzerConfigOptions? treeOptions = null)
        => IsSuppressed(globalOptions, treeOptions,
            "build_property.MudHttpSuppressTokenManagerKeyInference", TokenManagerKeyInferenceEditorConfigKey);

    /// <summary>
    /// 声明式豁免判定。
    /// </summary>
    /// <remarks>
    /// MSBuild 键以<b>字面量</b>传入（而非 <c>"build_property." + const</c> 拼接）：仓库存在守卫测试
    /// <c>RegisteredCompilerVisibleProperties_AllHaveReadPoints</c> / <c>GeneratorReadsNoUnregisteredBuildProperties</c>，
    /// 二者按字面量扫描源码 —— 拼接写法会让"props 未注册 CompilerVisibleProperty"的静默失效绕过守卫
    /// （F6(b) 实测即因此漏注册）。
    /// </remarks>
    private static bool IsSuppressed(
        AnalyzerConfigOptions globalOptions,
        AnalyzerConfigOptions? treeOptions,
        string msBuildKey,
        string editorConfigKey)
    {
        // ① MSBuild 属性（编译器以 build_property.<PropertyName> 键透传；同时存在于全局与树粒度配置）。
        if (TryGetTrueLike(globalOptions, msBuildKey) || TryGetTrueLike(treeOptions, msBuildKey))
            return true;

        // ② .editorconfig / .globalconfig 键（普通 .editorconfig 仅体现在树粒度配置中）。
        if (TryGetTrueLike(treeOptions, editorConfigKey) || TryGetTrueLike(globalOptions, editorConfigKey))
            return true;

        return false;
    }

    private static bool TryGetTrueLike(AnalyzerConfigOptions? options, string key)
        => options is not null && options.TryGetValue(key, out var value) && IsTrueLike(value);

    private static bool IsTrueLike(string? value)
        => value is not null
           && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("allow", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.Ordinal));
}
