// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;
using System.Text.RegularExpressions;
using Mud.HttpUtils.Attributes;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// I-17（§6.5 A-6）反例守卫：禁止「枚举型特性参数经 <c>ConstructorArguments[…].Value?.ToString()</c>
/// 直出整数后参与语义比较」的写法。
/// </summary>
/// <remarks>
/// <para>
/// <b>动机</b>：Roslyn 对枚举 <see cref="TypedConstant"/> 的 <c>Value</c> 返回底层整数
/// （如 <c>Replace</c> → <c>1</c>），若 <c>ToString()</c> 直出 <c>"1"</c> 而消费端按成员名比较将恒失配
/// （GEN-04/05/CFG 系列缺陷的根因之一）。A-4 已把所有枚举读取收口到
/// <c>AttributeArgumentReader.GetEnumMemberName</c>（序号 → 成员名映射），故识别本形态即为回归信号。
/// </para>
/// <para>
/// <b>判定依据</b>：字符串型特性参数（如 Query 的 name/value、Header 的 "Authorization"）经
/// <c>ConstructorArguments[N].Value?.ToString()</c> 读取是<b>合法</b>的（<c>Value</c> 本就是 string）。
/// 只有<b>枚举</b>型参数被 <c>ToString()</c> 直出后与<b>成员名</b>做语义比较才构成违规。
/// 故本守卫以「<c>ConstructorArguments</c> + <c>.ToString()</c> 存在，且同语句将结果与某个
/// <c>Mud.HttpUtils.Attributes</c> 枚举成员名（引号内字面量）比较」为反例形态，
/// 从而既拦住真正的枚举反例、又不误伤合法的字符串参数读取。
/// </para>
/// <para>
/// 白名单：<c>AttributeArgumentReader</c> 实现文件内部、以及 <c>//</c>/<c>///</c> 注释行。
/// </para>
/// </remarks>
public class GeneratorSourceContractTests
{
    private const string GeneratorRoot = @"..\..\..\..\Mud.HttpUtils.Generator";

    /// <summary>字符串比较形式标记。</summary>
    private static readonly string[] ComparisonMarkers =
    {
        "==", "!=", ".Contains(", ".StartsWith(", ".EndsWith(", "switch",
    };

    /// <summary>收集 Mud.HttpUtils.Attributes 中全部枚举成员名（作为「被比较的成员名」判定表）。</summary>
    private static readonly HashSet<string> EnumMemberNames = LoadEnumMemberNames();

    private static HashSet<string> LoadEnumMemberNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var attributesAssembly = typeof(RetryAttribute).Assembly;
        foreach (var type in attributesAssembly.GetTypes())
        {
            if (!type.IsEnum)
                continue;
            foreach (var member in Enum.GetNames(type))
                names.Add(member);
        }
        return names;
    }

    private static IEnumerable<(string File, int Line, string Text)> ScanViolations()
    {
        var root = Path.GetFullPath(GeneratorRoot);
        Directory.Exists(root).Should().BeTrue($"找不到生成器源码目录：{root}");

        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !f.Contains("AttributeArgumentReader", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // 跳过空行与纯注释行（// 或 ///）
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                // 命中判定：同一物理行同时出现 ConstructorArguments 与 .ToString()
                if (!line.Contains("ConstructorArguments", StringComparison.Ordinal)
                    || !line.Contains(".ToString()", StringComparison.Ordinal))
                    continue;

                // 语义比较判定：该行/上下文（相邻行）存在比较形式标记，
                // 且其中出现基类库枚举成员名字面量（枚举反例才违规，字符串参数读取合法）。
                var statement = string.Concat(lines.Skip(i).Take(3).Select(l => l.Split("//")[0])).Trim();
                if (!ComparisonMarkers.Any(m => statement.Contains(m, StringComparison.Ordinal)))
                    continue;

                var hitMember = EnumMemberNames.FirstOrDefault(m =>
                    statement.Contains($"\"{m}\"", StringComparison.Ordinal));
                if (hitMember == null)
                    continue;

                yield return (file, i + 1, $"{line.Trim()}  （比较成员名 \"{hitMember}\"）");
            }
        }
    }

    [Fact]
    public void NoEnumArgument_ToString_SemanticComparison()
    {
        var violations = ScanViolations().ToArray();

        violations.Should().BeEmpty(
            "I-17：枚举型特性参数必须经 AttributeArgumentReader.GetEnumMemberName（序号→成员名）再参与比较，禁止" +
            " ConstructorArguments[…].Value?.ToString() 直出整数进入语义分支。命中（文件:行号）：\n  - " +
            string.Join("\n  - ", violations.Select(v => Path.GetFileName(v.File) + ":" + v.Line + "  " + v.Text)));
    }
}