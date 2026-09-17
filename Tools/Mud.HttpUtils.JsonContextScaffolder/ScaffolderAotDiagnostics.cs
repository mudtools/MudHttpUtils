// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.CodeAnalysis;

namespace Mud.HttpUtils.JsonContextScaffolder;

/// <summary>
/// Scaffolder 侧 AOT 诊断描述符集中定义。
/// </summary>
/// <remarks>
/// GEN-15（§8.3，方案 A 迁移）：AOT001/AOT002/AOT003 描述符从生成器
/// <c>Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs</c> 迁移至此。
/// <para>
/// 说明：
/// </para>
/// <list type="bullet">
///   <item>
///     本程序集（<c>Mud.HttpUtils.JsonContextScaffolder</c>）是 pre-build 工具，
///     <b>不参与 analyzers 分发</b>，不会把这三个描述符当作源生成器/分析器诊断发布，
///     因此不会触发仓库的 RS 发布跟踪（<c>AnalyzerReleases.Unshipped.md</c>）。
///   </item>
///   <item>
///     Scaffolder 实际报告经 <see cref="ScaffolderDiagnostic"/>（字符串 ID），
///     但 ID / 严重级别的<b>唯一事实源</b>收敛于此三处 <see cref="DiagnosticDescriptor"/>，
///     避免「生成器与脚手架各自维护一份、互相漂移」的历史问题（与 GEN-16 的段位隔离原则一致）。
///   </item>
/// </list>
/// </remarks>
internal static class ScaffolderAotDiagnostics
{
    /// <summary>AOT001：同一 SerializerClassName 下存在冲突的 NamingPolicy 配置。</summary>
    internal static readonly DiagnosticDescriptor AotDuplicateSerializerClassName = new(
        id: "AOT001",
        title: "AOT JSON Context 类名冲突",
        messageFormat: "SerializerClassName '{0}' 存在冲突的 NamingPolicy 配置。同一 Context 内只能使用一个命名策略。建议统一配置或拆分为不同分组。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>AOT002：开放泛型类型在低版本 TFM 上标注 [HttpJsonSerializable]。</summary>
    internal static readonly DiagnosticDescriptor AotOpenGenericOnLegacyTfm = new(
        id: "AOT002",
        title: "开放泛型类型在低版本 TFM 上标注 [HttpJsonSerializable]",
        messageFormat: "类型 '{0}' 是开放泛型，在 net8.0 以下不支持源生成开放泛型。低版本将走反射兜底，AOT 下不可用。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>AOT003：多态类型缺少 [JsonDerivedType] 标注。</summary>
    internal static readonly DiagnosticDescriptor AotPolymorphismWithoutJsonDerivedType = new(
        id: "AOT003",
        title: "多态类型缺少 [JsonDerivedType] 标注",
        messageFormat: "类型 '{0}' 存在基类（多态序列化），但未标注 [JsonDerivedType]。以基类反序列化派生类时源生成不含派生类型，可能丢字段。建议在同程序集内补充 [JsonDerivedType] 或由 Scaffolder 自动补全。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>将 Roslyn <see cref="DiagnosticSeverity"/> 映射为 <see cref="ScaffolderDiagnosticSeverity"/>。</summary>
    internal static ScaffolderDiagnosticSeverity ToScaffolderSeverity(DiagnosticSeverity severity)
        => severity switch
        {
            DiagnosticSeverity.Error => ScaffolderDiagnosticSeverity.Error,
            DiagnosticSeverity.Warning => ScaffolderDiagnosticSeverity.Warning,
            DiagnosticSeverity.Info => ScaffolderDiagnosticSeverity.Info,
            _ => ScaffolderDiagnosticSeverity.Info,
        };
}