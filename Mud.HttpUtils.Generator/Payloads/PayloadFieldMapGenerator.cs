// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Models.Payloads;

namespace Mud.HttpUtils;

/// <summary>
/// 载荷字段映射生成器（第 4 个生成器族）：为「外部报文 → 强类型载荷」生成字段映射委托表。
/// </summary>
/// <remarks>
/// <para>
/// <b>生成物是「委托表」而非「XML 解析代码」</b>：产物只被消费方手写运行时引擎（AOT 安全）消费，
/// 不被任何其它生成器消费 ⇒ 不受「自定义生成器 → System.Text.Json 链式」不可行（Roslyn 生成器互不可见）的限制。
/// </para>
/// <para>
/// <b>增量管道</b>：逐类模型（<see cref="PayloadContractModel"/>，以指纹做值相等）与配置值快照
/// （复用既有 <see cref="GeneratorConfigSnapshot"/>：禁用 / 生成标记 / nullable / 生成器版本）
/// 组合后注册<b>逐项</b>输出 —— 改 A 类不触发 B 类重生成；
/// <b>刻意不引入 <c>CompilationProvider</c></b>（消除编译级粒度重执行）。
/// 生成器版本变化经由快照的 <c>Version</c> 值传播，故无需额外的 salt 节点。
/// </para>
/// <para>
/// <b>异常纪律</b>：执行体整体 try/catch，异常转为 <c>PAYLOAD001</c> 诊断；
/// 绝不让异常逸出（逸出即 AD0001，用户仅看到「生成器崩溃」）。
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
internal sealed class PayloadFieldMapGenerator : TransitiveCodeGenerator
{
    /// <inheritdoc/>
    public override void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ① 契约模型（per-class 粒度；等价性由模型指纹决定）
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PayloadContractModelBuilder.ContractAttributeMetadataName,
                // 谓词放宽到 TypeDeclarationSyntax：record 声明**不是** ClassDeclarationSyntax，
                // 若收窄到 class 会让 [PayloadContract] 标在 record 上时静默无诊断；
                // 放宽后由 PAYLOAD009 给出可操作提示（class / struct / interface 的合法性由 csc 依
                // AttributeUsage(AttributeTargets.Class) 判定）。
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => PayloadContractModelBuilder.Build(ctx))
            .WithComparer(EqualityComparer<PayloadContractModel>.Default)
            .WithTrackingName("PayloadFieldMap_SyntaxProvider");

        // ② 配置值快照（复用 G7-06 既有快照：Disable / EmitMarkers / NullableEnable / Version 均为本生成器所需）
        var configSnapshot = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => GeneratorConfigSnapshot.Create(provider))
            .WithComparer(GeneratorConfigSnapshot.Comparer)
            .WithTrackingName("PayloadFieldMap_ConfigSnapshot");

        // ③ 执行（逐项注册点：无 Collect()，故单类变更不牵连其它类）
        context.RegisterSourceOutput(
            models.Combine(configSnapshot).WithTrackingName("PayloadFieldMap_CompleteData"),
            (sourceProductionContext, pair) => Execute(sourceProductionContext, pair.Left, pair.Right));
    }

    /// <summary>
    /// 逐契约生成逻辑（实例方法：异常兜底复用基类的 <c>ReportErrorDiagnostic</c>）。
    /// </summary>
    private void Execute(
        SourceProductionContext context,
        PayloadContractModel model,
        GeneratorConfigSnapshot configSnapshot)
    {
        try
        {
            // 全局禁用开关（build_property.DisableMudSourceGenerator）：与其它生成器语义一致，
            // 关闭后不产出任何文件，也不报告诊断（便于「仅分析器构建」排查 MUD*/AOT* 诊断）。
            if (configSnapshot.Disable)
                return;

            for (var i = 0; i < model.Diagnostics.Length; i++)
            {
                var diagnostic = model.Diagnostics[i];
                context.ReportDiagnostic(Diagnostic.Create(
                    diagnostic.Descriptor, diagnostic.Location, diagnostic.ClassName, diagnostic.Message));
            }

            // 有 Error 即不产出（避免「半成品」误导：缺字段的映射表比编译失败更危险）
            if (model.HasErrors)
                return;

            AddSourceValidated(context, model.HintName, PayloadFieldMapRenderer.Render(model, configSnapshot));
        }
        catch (Exception exception)
        {
            ReportErrorDiagnostic(context, Diagnostics.PayloadGenerationError, model.TypeName, exception, model.Location);
        }
    }
}
