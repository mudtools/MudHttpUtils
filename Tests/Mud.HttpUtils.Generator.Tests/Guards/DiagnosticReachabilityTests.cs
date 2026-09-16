// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// I-20（§6.5 A-6）描述符可达性守卫：每个 <c>Diagnostics.*</c> 的
/// <see cref="DiagnosticDescriptor"/> 字段都必须在生成器/分析器源码中被引用
/// （通常经 <c>ReportDiagnostic</c>/<c>Diagnostic.Create</c> 报告），或显式登记进保留占位白名单。
/// </summary>
/// <remarks>
/// <para>
/// <b>动机</b>：诊断描述符「名字只在 Diagnostics.cs 中出现、没有任何报告点」属于死描述符，
/// 令用户误以为该诊断会被报告（GEN-15 的 AOT001/002/003 正是这一形态，见 §8.3）。
/// 本守卫上线即在 CI 拦住该类回归。
/// </para>
/// <para>
/// <b>实现手法</b>：反射枚举 <c>Mud.HttpUtils.Generator</c> 程序集内 <c>Diagnostics</c> 类的
/// <see cref="DiagnosticDescriptor"/> 静态字段，取其 <c>descriptor.Id</c>；
/// 对每个描述符字段，判断其在<b>非声明文件</b>中是否仍被引用（AOT001~003 在修复前后恰好是「仅声明、零引用」，
/// 故以「是否在声明文件之外出现」作为可达性判据）；未命中且不在白名单即失败，
/// 打印 <c>id + 字段名 + 位置（定义处 文件:行号）</c>。
/// </para>
/// </remarks>
public class DiagnosticReachabilityTests
{
    private const string GeneratorRoot = @"..\..\..\..\Mud.HttpUtils.Generator";

    /// <summary>
    /// 保留占位白名单：刻意无报告点、不视为死诊断的 ID
    /// （与 DocumentationContractTests.PlaceholderDiagnosticIds 保持一致；README 用注记说明）。
    /// </summary>
    private static readonly HashSet<string> PlaceholderIds = new(StringComparer.Ordinal)
    {
        "HTTPCLIENT002", "HTTPCLIENT006", "HTTPCLIENT010", "HTTPCLIENT019",
    };

    /// <summary>
    /// 扫场源码，收集每个描述符字段的<b>定义处</b>（文件:行号）与<b>定义文件</b>。
    /// </summary>
    private static (string FieldName, string Definition, string DefinitionFile)[] LocateFieldDeclarations()
    {
        var result = new List<(string, string, string)>();
        foreach (var file in EnumerateSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var idx = line.IndexOf("DiagnosticDescriptor ", StringComparison.Ordinal);
                if (idx < 0)
                    continue;

                var fieldName = line.Substring(idx + "DiagnosticDescriptor ".Length).Split(' ', '=')[0];
                if (fieldName.Length > 0)
                    result.Add((fieldName, $"{Path.GetFileName(file)}:{i + 1}", file));
            }
        }
        return result.ToArray();
    }

    /// <summary>枚举 Diagnostics 的全部 DiagnosticDescriptor 静态字段（含 id 与定义处）。</summary>
    private static List<(string FieldName, string Id, string Definition)> EnumerateDescriptors()
    {
        var diagnosticsType = typeof(Mud.HttpUtils.HttpInvokeClassSourceGenerator).Assembly
            .GetType("Mud.HttpUtils.Diagnostics")
            ?? throw new InvalidOperationException("无法找到 Mud.HttpUtils.Diagnostics 类型");

        var declarations = LocateFieldDeclarations();
        var result = new List<(string, string, string)>();
        foreach (var field in diagnosticsType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType.Name != "DiagnosticDescriptor")
                continue;
            if (field.GetValue(null) is not DiagnosticDescriptor descriptor)
                continue;

            var definition = declarations.FirstOrDefault(d => d.FieldName == field.Name).Definition;
            result.Add((field.Name, descriptor.Id, string.IsNullOrEmpty(definition) ? "未知" : definition));
        }
        return result;
    }

    /// <summary>生成器源码全部 *.cs 文件路径（排除 obj/bin）。</summary>
    private static IEnumerable<string> EnumerateSourceFiles()
    {
        var root = Path.GetFullPath(GeneratorRoot);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return file;
        }
    }

    [Fact]
    public void EveryDiagnosticDescriptor_IsReachableOrPlaceholder()
    {
        var descriptors = EnumerateDescriptors();
        descriptors.Should().NotBeEmpty("守卫自身必须能枚举到 Diagnostics 描述符，否则扫描逻辑失效");

        var declarations = LocateFieldDeclarations();

        foreach (var (fieldName, id, definition) in descriptors)
        {
            if (PlaceholderIds.Contains(id))
                continue;

            var declarationFile = declarations.FirstOrDefault(d => d.FieldName == fieldName).DefinitionFile;
            var isReachable = EnumerateSourceFiles()
                .Where(f => f != declarationFile)
                .Any(f => File.ReadAllText(f).Contains(fieldName, StringComparison.Ordinal));

            isReachable.Should().BeTrue(
                $"I-20：描述符 {id}（字段 {fieldName}）零报告点/零引用（仅在其定义处 {definition} 出现），属死描述符。" +
                "应存在至少一个 ReportDiagnostic/Diagnostic.Create 报告点引用该字段，或显式登记进保留占位白名单。");
        }
    }
}