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
/// M6-HC-13：<c>MudHttpClientLog</c> 的 EventId <b>全表唯一性</b>守卫。
/// </summary>
/// <remarks>
/// <para>
/// 既有的 <see cref="MudHttpClientLogContractTests"/> 只比对「日志方法数最多」的那一对
/// <c>#if</c>/<c>#else</c> 分支，<b>不覆盖</b>条件块之外的尾部批次（TMX 轮新增日志），
/// 因此 <c>TokenRefreshSuppressed</c> / <c>TokenCacheSerializationFailed</c> 曾分别占用
/// 170/171，与 <c>PolicyCacheFull</c>(170) / <c>TokenRecoveryRedirectDetected</c>(171) 撞号。
/// </para>
/// <para>
/// 本测试对整个源文件做全量扫描：同名方法（双分支重复定义）去重后，任一 EventId 只能映射到
/// 一个方法名，从而在任何 TFM 上都杜绝跨语义的号段复用。
/// </para>
/// </remarks>
public class EventIdUniquenessGuardTests
{
    private static string LocateSourceFile()
    {
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
            "未能定位 Mud.HttpUtils.Client/HttpClient/MudHttpClientLog.cs —— 契约测试需要仓库源码树。");
    }

    /// <summary>从整个源文件提取 (方法名 → EventId)，双分支的同名方法自然去重。</summary>
    private static Dictionary<string, int> ExtractAllEventIds(string text)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        // 分支 A：`[LoggerMessage(EventId = 123, ...)]` + `public static partial void Name(`
        var attributePattern = new Regex(
            @"\[LoggerMessage\(EventId\s*=\s*(\d+)[\s\S]{0,800}?public static (?:partial )?void (?<name>\w+)\(",
            RegexOptions.Multiline);

        // 注：`Match` 与 Moq.Match 同名（GlobalUsings 引入 Moq），故全限定。
        foreach (System.Text.RegularExpressions.Match m in attributePattern.Matches(text))
            result[m.Groups["name"].Value] = int.Parse(m.Groups[1].Value);

        // 分支 B：`new EventId(123, nameof(Name))`
        var definePattern = new Regex(@"new EventId\((\d+),\s*nameof\((?<name>\w+)\)\)");
        foreach (System.Text.RegularExpressions.Match m in definePattern.Matches(text))
            result[m.Groups["name"].Value] = int.Parse(m.Groups[1].Value);

        return result;
    }

    [Fact]
    public void AllEventIds_ShouldBeGloballyUnique()
    {
        var text = File.ReadAllText(LocateSourceFile());
        var ids = ExtractAllEventIds(text);

        ids.Should().NotBeEmpty("必须能从 MudHttpClientLog.cs 解析到日志事件");

        var collisions = ids
            .GroupBy(kvp => kvp.Value)
            .Select(g => new { Id = g.Key, Names = g.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray() })
            .Where(g => g.Names.Length > 1)
            .Select(g => $"EventId {g.Id} → {string.Join(", ", g.Names)}")
            .ToArray();

        collisions.Should().BeEmpty(
            "HC-13：EventId 在全表（含条件块之外的尾部批次）必须唯一，否则跨 TFM 的日志聚合无法区分语义。" +
            $"重复项：{string.Join(" | ", collisions)}");
    }

    [Fact]
    public void TmxBatch_ShouldUseMigratedUnoccupiedIds()
    {
        var ids = ExtractAllEventIds(File.ReadAllText(LocateSourceFile()));

        // 迁移后的落点
        ids.Should().ContainKey("TokenRefreshSuppressed");
        ids["TokenRefreshSuppressed"].Should().Be(181,
            "HC-13：TokenRefreshSuppressed 由原 170 迁移至 181");
        ids.Should().ContainKey("TokenCacheSerializationFailed");
        ids["TokenCacheSerializationFailed"].Should().Be(182,
            "HC-13：TokenCacheSerializationFailed 由原 171 迁移至 182");

        // 撞号的原占用方必须保持不动
        ids["PolicyCacheFull"].Should().Be(170);
        ids["TokenRecoveryRedirectDetected"].Should().Be(171);
    }
}