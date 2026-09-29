// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Mud.HttpUtils.CodeFixes.Tests;

/// <summary>
/// [Phase1 修复 2.5] 文档-修复器一致性守卫：README「可自动修复 = 是」的诊断集合
/// 必须与 CodeFixes 程序集实际注册的 <see cref="CodeFixProvider.FixableDiagnosticIds"/> 并集**完全相等**。
/// </summary>
/// <remarks>
/// <para>
/// 封堵两类回归（审查 1.1/1.2/1.3 的文档侧表现）：
/// <list type="number">
///   <item><b>文档过度承诺</b>：README 写「可自动修复 = 是」，但修复器根本没注册该 ID
///   —— 用户按文档等灯泡，实际永远等不到（HTTPCLIENT005 的历史症状）；</item>
///   <item><b>文档漏报</b>：修复器已支持，README 仍写「否」—— 用户不知道该诊断可一键修复。</item>
/// </list>
/// </para>
/// <para>
/// 之所以放在本测试项目而非 <c>Generator.Tests</c>：本守卫必须反射 CodeFixes 程序集的实际修复器集合，
/// 而 Generator.Tests 不引用 CodeFixes（依赖方向相反）。约束「CodeFixes 不得引用 Generator 程序集」
/// 只针对产品代码，测试项目引用两侧用于契约比对是允许的（方案 A.5 修订 8）。
/// </para>
/// </remarks>
public class ReadmeFixableContractTests
{
    private const string GeneratorReadmePath = @"../../../../Mud.HttpUtils.Generator/README.md";
    private const string CodeFixesReadmePath = @"../../../../Mud.HttpUtils.CodeFixes/README.md";

    /// <summary>匹配诊断表行首的 ID 列：<c>| `AOT004` | …</c>。</summary>
    private static readonly Regex RowStartRegex =
        new(@"^\|\s*`(?<id>[A-Z][A-Z0-9]*\d{3})`\s*\|", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// 解析 README 诊断表中的「可自动修复」列，返回值为「是」的 ID 集合。
    /// </summary>
    /// <remarks>
    /// 表头约定（6 列）：诊断 ID | 严重级别 | 触发条件 | 解决方案 | 可自动修复 | 可抑制。
    /// 按 <c>|</c> 切分后「可自动修复」位于索引 5。
    /// </remarks>
    private static HashSet<string> ParseReadmeFixableIds(string readme)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in readme.Split('\n'))
        {
            var match = RowStartRegex.Match(line);
            if (!match.Success)
                continue;

            var cells = line.Split('|');
            if (cells.Length < 7)
                continue; // 非 6 列诊断表（如速查表）——本守卫只针对标准诊断表

            if (cells[5].Trim().StartsWith("是", StringComparison.Ordinal))
                result.Add(match.Groups["id"].Value);
        }

        return result;
    }

    /// <summary>反射 CodeFixes 程序集，收集全部修复器声明的 FixableDiagnosticIds 并集。</summary>
    private static HashSet<string> ReadCodeFixIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in typeof(AotXmlCodeFixProvider).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(CodeFixProvider).IsAssignableFrom(type))
                continue;
            if (Activator.CreateInstance(type) is not CodeFixProvider provider)
                continue;

            foreach (var id in provider.FixableDiagnosticIds)
                ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// 核心守卫：Generator README「可自动修复 = 是」集合 ≡ CodeFixes 实际注册集合。
    /// </summary>
    [Fact]
    public void GeneratorReadmeFixableIds_MatchCodeFixProviders()
    {
        var readme = File.ReadAllText(Path.GetFullPath(GeneratorReadmePath));
        var readmeFixable = ParseReadmeFixableIds(readme);
        var codeFixIds = ReadCodeFixIds();

        readmeFixable.Should().NotBeEmpty("README 必须至少标注一个「可自动修复 = 是」的诊断");
        codeFixIds.Should().NotBeEmpty("CodeFixes 程序集必须至少注册一个 CodeFixProvider");

        readmeFixable.Should().BeEquivalentTo(codeFixIds,
            "README「可自动修复」列必须与 CodeFixes 实际注册的 FixableDiagnosticIds 完全一致" +
            $"（README: [{string.Join(", ", readmeFixable.OrderBy(x => x))}]；" +
            $"实际: [{string.Join(", ", codeFixIds.OrderBy(x => x))}]）");
    }

    /// <summary>
    /// CodeFixes README 的「诊断 ID 速查」表必须覆盖全部实际注册的修复器 ID，且不得虚报。
    /// </summary>
    [Fact]
    public void CodeFixesReadmeQuickReference_CoversExactlyRegisteredIds()
    {
        var readme = File.ReadAllText(Path.GetFullPath(CodeFixesReadmePath));
        var documented = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in readme.Split('\n'))
        {
            var match = RowStartRegex.Match(line);
            if (!match.Success)
                continue;

            // CodeFixes README 速查表为 4 列（诊断 ID | 严重级别 | 来源 | 可自动修复），
            // 按 '|' 切分后共 6 段（首尾各一空段）。
            var cells = line.Split('|');
            if (cells.Length >= 6)
                documented.Add(match.Groups["id"].Value);
        }

        var codeFixIds = ReadCodeFixIds();

        documented.Should().BeEquivalentTo(codeFixIds,
            "CodeFixes README 速查表必须与程序集实际注册的 FixableDiagnosticIds 一一对应" +
            $"（文档: [{string.Join(", ", documented.OrderBy(x => x))}]；" +
            $"实际: [{string.Join(", ", codeFixIds.OrderBy(x => x))}]）");
    }

    /// <summary>
    /// 修复器类型必须可用无参构造实例化（Roslyn MEF 导出的前提），
    /// 否则 IDE 侧 <c>[ExportCodeFixProvider]</c> 加载会静默失败。
    /// </summary>
    [Fact]
    public void AllCodeFixProviders_AreParameterlessConstructible()
    {
        var providerTypes = typeof(AotXmlCodeFixProvider).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t))
            .ToList();

        providerTypes.Should().NotBeEmpty();

        foreach (var type in providerTypes)
        {
            var ctor = type.GetConstructor(Type.EmptyTypes);
            ctor.Should().NotBeNull($"{type.Name} 必须提供无参构造函数，否则 MEF 无法导出 CodeFixProvider");
        }
    }
}
