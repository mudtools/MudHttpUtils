// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;
using System.Text.RegularExpressions;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-13：<c>Mud.HttpUtils.Attributes</c> 程序集的公开面一致性守卫。
/// </summary>
/// <remarks>
/// <para>
/// <b>① 命名空间一致性</b>：程序集内全部 <see cref="Attribute"/> 派生类型必须位于
/// <c>Mud.HttpUtils.Attributes</c>。此前 <c>AllowUnmatchedRouteParametersAttribute</c> 位于
/// <c>Mud.HttpUtils</c>（该程序集内唯一例外），使消费方的 <c>using</c> 组合不可预期；已迁入
/// （破坏性变更 BC2，见 08 册 §9），故白名单为<b>空</b>。
/// </para>
/// <para>
/// <b>② README 属性名 ↔ 真实成员</b>：<c>Attributes/README.md</c> 的「关键属性」列中出现的每个反引号标识符
/// 都必须在对应类型的公开成员上真实存在 —— 该列此前记录了 4 个<b>幻影属性</b>
/// （<c>HttpMethodAttribute.Route</c>、<c>QueryAttribute.Encode</c>、<c>RawQueryStringAttribute.PrependQuestionMark</c>、
/// <c>TokenAttribute.Replace</c>），会直接误导用户写出不可编译代码。
/// </para>
/// </remarks>
public class AttributeNamespaceConsistencyTests
{
    private const string ExpectedNamespace = "Mud.HttpUtils.Attributes";
    private const string ReadmeRelativePath = @"../../../../../Mud.HttpUtils.Attributes/README.md";

    /// <summary>
    /// C# 关键字/字面量：README「关键属性」列会用反引号标注默认值与签名片段
    /// （如 <c>默认 `false`</c>、<c>`(int maxRetries)`</c> 中的类型词），
    /// 它们不是特性成员，不参与 G8-13 成员存在性校验。
    /// </summary>
    private static readonly HashSet<string> NonMemberTokens = new(StringComparer.Ordinal)
    {
        "true", "false", "null",
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "double", "float", "decimal", "bool", "string", "char", "object",
        "var", "void", "new", "default", "get", "set", "in", "out", "ref",
    };

    private static readonly Assembly AttributesAssembly =
        typeof(Mud.HttpUtils.Attributes.HttpClientApiAttribute).Assembly;

    [Fact]
    public void AllAttributes_InExpectedNamespace()
    {
        var offenders = AttributesAssembly.GetExportedTypes()
            .Where(t => typeof(Attribute).IsAssignableFrom(t))
            .Where(t => !string.Equals(t.Namespace, ExpectedNamespace, StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty(
            $"G8-13：这些特性位于非预期命名空间（应统一为 {ExpectedNamespace}，白名单为空）：" +
            string.Join("、", offenders));
    }

    /// <summary>
    /// README「关键属性」列中的每个反引号标识符都必须是对应类型的真实公开成员（属性/字段/常量）。
    /// </summary>
    [Fact]
    public void ReadmeKeyPropertyColumn_OnlyReferencesRealMembers()
    {
        var readmePath = Path.GetFullPath(ReadmeRelativePath);
        File.Exists(readmePath).Should().BeTrue($"守卫依赖 README 文件：{readmePath}");

        var rows = File.ReadAllLines(readmePath)
            .Where(l => l.StartsWith("| `", StringComparison.Ordinal) && l.EndsWith("|", StringComparison.Ordinal))
            .ToList();

        rows.Should().NotBeEmpty("README 必须包含特性表格行（否则解析逻辑失效）");

        var problems = new List<string>();
        var checkedRows = 0;

        foreach (var row in rows)
        {
            // 单行表格形如 | `XxxAttribute` | 用途 | 目标 | `P1`, `P2` |（列数因表而异，关键属性恒为最后一列）
            var cells = row.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 5)
                continue;

            var typeToken = Regex.Match(cells[1], "^`([A-Za-z_][A-Za-z0-9_]*)`$");
            if (!typeToken.Success)
                continue;

            // 仅解析「特性类型」行：README 中还包含枚举/属性成员表（首列恰与某枚举同名），
            // 若不限定 Attribute 派生会误判（如把枚举 SerializationMethod 的成员表当成特性行）。
            var type = AttributesAssembly.GetExportedTypes()
                .FirstOrDefault(t => typeof(Attribute).IsAssignableFrom(t)
                    && string.Equals(t.Name, typeToken.Groups[1].Value, StringComparison.Ordinal));
            if (type == null)
                continue;

            // 最后一段有效单元格（表格以 | 结尾 → 末元素为空）
            var keyColumn = cells[^2];
            var declared = Regex.Matches(keyColumn, "`([A-Za-z_][A-Za-z0-9_]*)`")
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (declared.Count == 0)
                continue;

            checkedRows++;

            var realMembers = new HashSet<string>(
                type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(m => m.Name),
                StringComparer.Ordinal);

            foreach (var name in declared)
            {
                // 表头/说明性词汇（如「无属性」）与 C# 关键字/字面量（如默认 `false`）
                // 不参与校验：仅要求「像成员名」的标识符可命中。
                if (NonMemberTokens.Contains(name))
                    continue;
                if (!realMembers.Contains(name))
                    problems.Add($"{type.Name}.{name}");
            }
        }

        checkedRows.Should().BeGreaterThan(0, "守卫必须至少校验到一行（否则正则/表格形态已变化，断言退化为恒真）");

        problems.Should().BeEmpty(
            "G8-13：README「关键属性」列引用了不存在的成员（幻影属性），会误导用户写出不可编译代码：" +
            string.Join("、", problems));
    }
}
