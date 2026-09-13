// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// 逐参数构造签名（不依赖 SymbolDisplay 的默认值格式化，见审计 F1）。
/// <para>
/// 背景：<see cref="SymbolDisplayVisitor"/> 对「值类型参数 + <c>ExplicitDefaultValue == null</c>」
/// （即 <c>CancellationToken ct = default</c> 的符号形态）输出的字面量随 Roslyn 宿主版本变化——
/// 4.11.0 输出非法的 <c>null</c>，.NET SDK 10 内置编译器输出 <c>default</c>。
/// 生成器 DLL 由消费方编译器加载，故生成代码正确性取决于消费方 SDK。
/// 本构造器用 <see cref="TypeConverter.GetDefaultValueLiteral"/> 在生成期显式决定字面量，消除宿主差异。
/// </para>
/// </summary>
internal static class ParameterSignatureBuilder
{
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string Build(IMethodSymbol method) =>
        method.Parameters.Length == 0
            ? string.Empty
            : string.Join(", ", method.Parameters.Select(BuildParameter));

    private static string BuildParameter(IParameterSymbol p)
    {
        var sb = new StringBuilder();

        switch (p.RefKind)
        {
            case RefKind.Ref: sb.Append("ref "); break;
            case RefKind.Out: sb.Append("out "); break;
            case RefKind.In: sb.Append("in "); break;
            case RefKind.RefReadOnlyParameter:
                // RefKind.RefReadOnlyParameter 在 Microsoft.CodeAnalysis 4.11 中可用；
                // 为兼容更旧宿主，按枚举序值 4 识别（RefReadOnly 为 3，RefReadOnlyParameter 为 4）。
                sb.Append("ref readonly "); break;
        }

        if (p.IsParams) sb.Append("params ");
        sb.Append(p.Type.ToDisplayString(TypeFormat)).Append(' ').Append(p.Name);

        if (p.HasExplicitDefaultValue)
        {
            sb.Append(" = ").Append(TypeConverter.GetDefaultValueLiteral(p.Type, p.ExplicitDefaultValue));
        }

        return sb.ToString();
    }
}