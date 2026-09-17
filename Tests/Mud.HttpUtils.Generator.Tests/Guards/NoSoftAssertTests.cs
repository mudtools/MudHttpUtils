// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// I-21（§6.5 A-6）反例守卫：禁止「<c>if (… != null)</c> 包裹 <c>Should()</c>/<c>Assert</c>」的软断言形态。
/// </summary>
/// <remarks>
/// <para>
/// <b>动机</b>：形如 <c>if (generatedCode != null) {{ … .Should() … }}</c> 的写法在生成失败时<b>静默跳过断言</b>，
/// 使「本应红灯」的测试变成绿灯（CFG 家族回归的温床）。A-0 已清除历史 13 处软断言，本守卫防止复发。
/// </para>
/// <para>
/// <b>实现手法</b>：对测试工程源码做<b>反例正则扫描</b>——进入「以 <c>!= null</c> 作为条件的 <c>if</c> 块」后，
/// 在块闭合（花括号深度回零）之前出现 <c>.Should()</c> 或 <c>Assert.</c> 即失败，并打印 <c>文件:行号</c>。
/// </para>
/// </remarks>
public class NoSoftAssertTests
{
    private const string TestsRoot = @"..\..\..\..\Tests\Mud.HttpUtils.Generator.Tests";

    private static readonly Regex NonNullGuardRegex = new(
        @"if\s*\([^)]*!=\s*null\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 双引号字符串字面量（含转义 "" 与 \"），用于剥离——避免把测试中被断言的生成代码文本
    /// （如 <c>.NotContain("if (Keyword != null)")</c>）误判为真实 if 软断言。
    /// </summary>
    private static readonly Regex StringLiteralRegex = new(
        @"""(?:[^""\\]|\\.)*""|""""(?:[^""]|"" "")*""""",
        RegexOptions.Compiled);

    /// <summary>剥离字符串字面量后的代码文本。</summary>
    private static string StripStrings(string code)
        => StringLiteralRegex.Replace(code, "\"\"");

    private static IEnumerable<(string File, int Line, string Text)> ScanViolations()
    {
        var root = Path.GetFullPath(TestsRoot);
        Directory.Exists(root).Should().BeTrue($"找不到测试工程源码目录：{root}");

        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);

            var inGuard = false;
            var guardLine = 0;
            var braceDepth = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];

                // 剥离开字符串字面量（避免把生成代码文本中的 "if (…) != null" 误判），再取去注释后的代码
                var stripped = StripStrings(raw).Trim().Split("//")[0].Trim();

                // 计算花括号深度净变化（基于去字符串后的文本）
                var delta = stripped.Count(c => c == '{') - stripped.Count(c => c == '}');

                if (!inGuard)
                {
                    if (stripped.Contains("if", StringComparison.Ordinal)
                        && NonNullGuardRegex.IsMatch(stripped))
                    {
                        inGuard = true;
                        guardLine = i + 1;
                        braceDepth = stripped.Count(c => c == '{') - stripped.Count(c => c == '}');
                        // 条件与 { 同行时即进入（已有 depth）。
                        if (braceDepth <= 0 && stripped.Count(c => c == '{') > 0)
                            braceDepth = 1;
                        if (braceDepth <= 0)
                            continue;
                    }
                    else
                    {
                        continue;
                    }
                }

                // 在守卫块内检查软断言（基于去字符串后的文本）
                if ((stripped.Contains(".Should()", StringComparison.Ordinal)
                     || stripped.Contains(".Should(", StringComparison.Ordinal)
                     || stripped.Contains("Assert.", StringComparison.Ordinal))
                    && !stripped.StartsWith("//", StringComparison.Ordinal))
                {
                    yield return (file, guardLine, lines[guardLine - 1].Trim());
                }

                braceDepth += delta;
                if (braceDepth <= 0)
                {
                    inGuard = false;
                    braceDepth = 0;
                }
            }
        }
    }

    [Fact]
    public void NoSoftAssert_NonNullGuardWrappingAssertion()
    {
        var violations = ScanViolations().ToArray();

        violations.Should().BeEmpty(
            "I-21：禁止以「if (… != null)」包裹 Should()/Assert 的软断言写法（生成失败即静默跳过断言）。" +
            "命中（文件:行号）：\n  - " +
            string.Join("\n  - ", violations.Select(v => Path.GetFileName(v.File) + ":" + v.Line + "  " + v.Text)));
    }
}