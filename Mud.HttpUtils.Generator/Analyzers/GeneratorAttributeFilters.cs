// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Mud.HttpUtils.Analyzers;

/// <summary>
/// [E-5] 生成器/分析器共用的 [IgnoreGenerator] 判定纯函数，避免三处各自硬编码特性名字符串。
/// <para>
/// 语义（决策 2026-09-13）：接口级忽略 = 生成器完全不介入（不生成实现类、不生成 DI 注册/工厂、
/// 不报该接口的 AOT/MUD 诊断），由使用方自行实现并注册；方法级忽略 = 跳过该方法生成。
/// </para>
/// </summary>
internal static class GeneratorAttributeFilters
{
    private static readonly string[] IgnoreGeneratorNames = ["IgnoreGeneratorAttribute", "IgnoreGenerator"];

    public static bool HasIgnoreGenerator(INamedTypeSymbol symbol)
        => symbol.GetAttributes().Any(a => IsIgnoreGenerator(a.AttributeClass?.Name));

    public static bool HasIgnoreGenerator(IMethodSymbol method)
        => method.GetAttributes().Any(a => IsIgnoreGenerator(a.AttributeClass?.Name));

    public static bool HasIgnoreGenerator(ISymbol symbol)
        => symbol.GetAttributes().Any(a => IsIgnoreGenerator(a.AttributeClass?.Name));

    private static bool IsIgnoreGenerator(string? attributeClassName)
        => attributeClassName is not null
           && IgnoreGeneratorNames.Contains(attributeClassName, StringComparer.Ordinal);
}