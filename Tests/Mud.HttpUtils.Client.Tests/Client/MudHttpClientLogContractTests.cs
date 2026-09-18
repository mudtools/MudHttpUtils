// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// L-4：<c>MudHttpClientLog</c> 的源码级契约守卫。
/// </summary>
/// <remarks>
/// <para>
/// 该文件用 <c>#if NET6_0_OR_GREATER</c>（<c>[LoggerMessage]</c> 源生成）与 <c>#else</c>
/// （<c>netstandard2.0</c> 手写 <c>LoggerMessage.Define</c>）维护同一组日志方法的两份实现。
/// 任一分支漏加 / EventId 冲突都会造成「某 TFM 上日志缺失或语义混淆」，而**编译期完全不可见**
/// （两个分支不会同时参与编译）。本测试从源码文本层面补上这道门禁。
/// </para>
/// <para>
/// 断言三件事：
/// <list type="number">
///   <item><description>每个分支内 <b>EventId 不重复</b>（本次 L-4 修复的真实缺陷）。</description></item>
///   <item><description>两个分支的<b>公开方法名集合完全一致</b>。</description></item>
///   <item><description>同一方法在两个分支使用<b>同一 EventId</b>。</description></item>
/// </list>
/// </para>
/// </remarks>
public class MudHttpClientLogContractTests
{
    private static string LocateSourceFile()
    {
        // 从测试输出目录（bin/Debug/netX.0）逐级向上查找仓库根，再定位源文件。
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir.FullName, "Mud.HttpUtils.Client", "HttpClient", "MudHttpClientLog.cs");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "未能定位 Mud.HttpUtils.Client/HttpClient/MudHttpClientLog.cs —— 契约测试需要仓库源码树（请勿在打包产物上运行）。");
    }

    /// <summary>LoggerMessage 双分支的源码片段（<c>#if</c> 段与 <c>#else</c> 段）。</summary>
    private static (string IfBranch, string ElseBranch) SplitLoggerMessageBranches()
    {
        var lines = File.ReadAllLines(LocateSourceFile());

        // 该文件含多个条件块；LoggerMessage 双分支是"方法数最多"的那一对，
        // 通过统计每对 (if, else, endif) 中出现的日志方法数来定位，避免硬编码行号。
        var directives = new List<(int Index, string Kind)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("#if ", StringComparison.Ordinal))
                directives.Add((i, "if"));
            else if (trimmed.StartsWith("#else", StringComparison.Ordinal))
                directives.Add((i, "else"));
            else if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                directives.Add((i, "endif"));
        }

        string? bestIf = null;
        string? bestElse = null;
        var bestCount = -1;

        for (var i = 0; i < directives.Count; i++)
        {
            if (directives[i].Kind != "if")
                continue;

            // 找到与之配对的 else / endif（同级，规范嵌套下第一个 else 即配对）
            var elseIdx = -1;
            var endifIdx = -1;
            for (var j = i + 1; j < directives.Count; j++)
            {
                if (directives[j].Kind == "else" && elseIdx < 0)
                    elseIdx = j;
                else if (directives[j].Kind == "endif")
                {
                    endifIdx = j;
                    break;
                }
            }

            if (elseIdx < 0 || endifIdx < 0)
                continue;

            var ifStart = directives[i].Index;
            var elseStart = directives[elseIdx].Index;
            var endifStart = directives[endifIdx].Index;

            var ifText = string.Join("\n", lines.Skip(ifStart).Take(elseStart - ifStart));
            var count = Regex.Matches(ifText, @"public static (?:partial )?void \w+\(").Count;

            if (count > bestCount)
            {
                bestCount = count;
                bestIf = ifText;
                bestElse = string.Join("\n", lines.Skip(elseStart).Take(endifStart - elseStart));
            }
        }

        if (bestIf == null || bestElse == null || bestCount <= 0)
            throw new InvalidOperationException("未能在 MudHttpClientLog.cs 中定位 LoggerMessage 双分支。");

        return (bestIf, bestElse);
    }

    /// <summary>从源码片段提取 (方法名 → EventId) 映射。</summary>
    private static Dictionary<string, int> ExtractEventIds(string branchText)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        // 分支 A：`[LoggerMessage(EventId = 123, ...)]` + `public static partial void Name(`
        var attributePattern = new Regex(
            @"EventId\s*=\s*(\d+)[\s\S]{0,400}?public static partial void (?<name>\w+)\(",
            RegexOptions.Multiline);
        // 注：`Match` 与 Moq.Match 同名（GlobalUsings 引入 Moq），故全限定。
        foreach (System.Text.RegularExpressions.Match m in attributePattern.Matches(branchText))
            result[m.Groups["name"].Value] = int.Parse(m.Groups[1].Value);

        // 分支 B：`new EventId(123, nameof(Name))`
        var definePattern = new Regex(@"new EventId\((\d+),\s*nameof\((?<name>\w+)\)\)");
        foreach (System.Text.RegularExpressions.Match m in definePattern.Matches(branchText))
            result[m.Groups["name"].Value] = int.Parse(m.Groups[1].Value);

        return result;
    }

    [Fact]
    public void LoggerMessageBranches_ShouldHaveIdenticalMethodSets()
    {
        var (ifBranch, elseBranch) = SplitLoggerMessageBranches();

        var ifMethods = ExtractEventIds(ifBranch).Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var elseMethods = ExtractEventIds(elseBranch).Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();

        ifMethods.Should().NotBeEmpty("源生成分支必须解析到日志方法");
        elseMethods.Should().BeEquivalentTo(ifMethods,
            "L-4：netstandard2.0（#else）与 net6+（#if）分支的日志方法集必须完全一致，" +
            "否则某个 TFM 上会出现「有调用无实现」或「有实现无调用」");
    }

    [Fact]
    public void EventIds_ShouldBeUniqueWithinEachBranch()
    {
        var (ifBranch, elseBranch) = SplitLoggerMessageBranches();

        foreach (var (name, ids) in new[]
                 {
                     ("#if (net6+)", ExtractEventIds(ifBranch)),
                     ("#else (netstandard2.0)", ExtractEventIds(elseBranch)),
                 })
        {
            var duplicated = ids
                .GroupBy(kvp => kvp.Value)
                .Where(g => g.Count() > 1)
                .Select(g => $"EventId {g.Key} → {string.Join(", ", g.Select(x => x.Key))}")
                .ToArray();

            duplicated.Should().BeEmpty(
                $"{name} 分支内 EventId 必须唯一；重复会让日志消费方无法按 EventId 区分语义。" +
                $"重复项：{string.Join(" | ", duplicated)}");
        }
    }

    [Fact]
    public void SameMethod_ShouldUseSameEventIdInBothBranches()
    {
        var (ifBranch, elseBranch) = SplitLoggerMessageBranches();

        var ifIds = ExtractEventIds(ifBranch);
        var elseIds = ExtractEventIds(elseBranch);

        var mismatched = ifIds
            .Where(kvp => elseIds.TryGetValue(kvp.Key, out var elseId) && elseId != kvp.Value)
            .Select(kvp => $"{kvp.Key}: #if={kvp.Value} vs #else={elseIds[kvp.Key]}")
            .ToArray();

        mismatched.Should().BeEmpty(
            "同一日志方法在两个 TFM 分支必须使用同一 EventId（否则跨 TFM 的日志聚合会对不上）；" +
            $"不一致项：{string.Join(" | ", mismatched)}");
    }

    [Fact]
    public void UserTokenScopeInvalidationFallback_ShouldNotReuseSerializationEventId()
    {
        var (ifBranch, elseBranch) = SplitLoggerMessageBranches();

        foreach (var branch in new[] { ifBranch, elseBranch })
        {
            var ids = ExtractEventIds(branch);

            ids.Should().ContainKey("UserTokenScopeInvalidationFallback");
            ids.Should().ContainKey("RequestBodySerializationFastPathFallback");

            ids["UserTokenScopeInvalidationFallback"]
                .Should().NotBe(ids["RequestBodySerializationFastPathFallback"],
                    "L-4 回归：这两个事件曾共用 EventId 166，导致 ns2.0 日志无法区分「用户令牌 scope 降级」与「序列化 fast-path 回退」");
        }
    }
}
