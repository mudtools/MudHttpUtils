// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// HTTPCLIENT018（TokenManagerKey 使用默认推断值）诊断分析器。
/// </summary>
/// <remarks>
/// <para>
/// 该诊断原由生成器在生成管线上报，现迁至分析器。迁移原因：增量生成器的 per-interface 节点
/// 只以「接口指纹 + 全局配置快照 + 全局盐」为缓存输入，<b>.editorconfig 目录级键</b>变更不改变
/// 三者中任何一个 ⇒ 节点保持 Cached ⇒ 输出回调不重跑，树粒度豁免永远不生效
/// （与 AOT004/5/7 迁出生成管道同一理由）。分析器随编译变化自然重算，且天然持有
/// <c>GetOptions(语法树)</c>，一次覆盖 MSBuild 属性 / .globalconfig 键 / 目录级 .editorconfig 键三种豁免通道。
/// </para>
/// <para>
/// 判定面与生成器原实现逐字等价：接口配了 <c>[HttpClientApi(TokenManage)]</c>（且未走 HttpClient 模式）、
/// 接口级 <c>[Token]</c> 未显式指定 TokenType / TokenManagerKey（皆空 ⇒ 使用默认推断值）、
/// 未声明式豁免 ⇒ 上报 Warning。
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TokenManagerKeyInferenceAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Diagnostics.TokenManagerKeyInferredFromDefault);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        // CA1062：显式参数守卫（Roslyn 会传非 null，公开可覆写入口显式校验可避免第三方直接调用时的 NRE）。
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInterfaceDeclaration, SyntaxKind.InterfaceDeclaration);
    }

    /// <summary>
    /// 接口级判定：与生成器 <c>ExtractConfigurationFromAttributes</c> 同口径 ——
    /// ① [IgnoreGenerator] 接口不介入；② TokenManager 模式（配了 TokenManage 且未配 HttpClient）；
    /// ③ 接口级 [Token] 未显式指定 TokenType / TokenManagerKey；④ 未声明式豁免。
    /// </summary>
    private static void AnalyzeInterfaceDeclaration(SyntaxNodeAnalysisContext context)
    {
        // 分析器异常护栏：诊断逻辑异常不得中断编译。
        try
        {
            if (context.Node is not InterfaceDeclarationSyntax interfaceDecl)
                return;

            var interfaceSymbol = context.SemanticModel.GetDeclaredSymbol(interfaceDecl, context.CancellationToken);
            if (interfaceSymbol == null)
                return;

            // [IgnoreGenerator]：用户自备实现，生成器不介入 ⇒ 本诊断同样不适用。
            if (GeneratorAttributeFilters.HasIgnoreGenerator(interfaceSymbol))
                return;

            var httpClientApiAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(
                interfaceSymbol,
                HttpClientGeneratorConstants.HttpClientApiAttributeNames);
            if (httpClientApiAttribute == null)
                return;

            // 仅 TokenManager 模式有意义（配了 TokenManage、未走 HttpClient 模式）——
            // 与生成器 effectiveTokenManage 同口径。
            var httpClient = AttributeDataHelper.GetStringValueFromAttribute(
                httpClientApiAttribute,
                HttpClientGeneratorConstants.HttpClientProperty);
            var tokenManage = AttributeDataHelper.GetStringValueFromAttribute(
                httpClientApiAttribute,
                HttpClientGeneratorConstants.TokenManageProperty);
            if (!string.IsNullOrEmpty(httpClient) || string.IsNullOrEmpty(tokenManage))
                return;

            // 接口级 [Token] 已显式指定 ⇒ 不使用默认推断值（构造形参默认值语义：
            // 任意形式的接口级 [Token] 都使 TokenType / TokenManagerKey 非空）。
            var tokenAttribute = AttributeDataHelper.GetAttributeDataFromSymbol(
                interfaceSymbol,
                HttpClientGeneratorConstants.TokenAttributeNames);
            var tokenType = TokenHelper.GetTokenTypeFromAttribute(tokenAttribute);
            var tokenManagerKey = TokenHelper.GetTokenManagerKeyFromAttribute(tokenAttribute);
            if (!string.IsNullOrEmpty(tokenType) || !string.IsNullOrEmpty(tokenManagerKey))
                return;

            // 一等豁免（默认仍报，豁免必须显式）：MSBuild 属性 / .globalconfig 键 / 目录级 .editorconfig 键。
            // 树粒度配置只有 GetOptions(语法树) 能读到 —— 分析器路径天然具备（生成器路径不具备，见类型 remarks）。
            if (MudHttpAnalyzerConfig.IsTokenManagerKeyInferenceSuppressed(
                    context.Options.AnalyzerConfigOptionsProvider.GlobalOptions,
                    context.Options.AnalyzerConfigOptionsProvider.GetOptions(interfaceDecl.SyntaxTree)))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.TokenManagerKeyInferredFromDefault,
                interfaceDecl.GetLocation(),
                interfaceSymbol.Name,
                TokenHelper.GetDefaultTokenType()));
        }
        catch (Exception ex)
        {
            GeneratorDebugLogger.LogError(nameof(TokenManagerKeyInferenceAnalyzer), ex);
        }
    }
}
