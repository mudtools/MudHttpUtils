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
/// M0.4（骨架）/ M2 收尾：文档-诊断一致性守卫。
/// <para>
/// 封堵两类回归：
/// 1. 「新增诊断忘记写文档」/「文档承诺的诊断在 Diagnostics.cs 不存在或级别不一致」（F11 家族）；
/// 2. 「build props 注册的 CompilerVisibleProperty 在生成器中无读取点」/「生成器读取了未注册的属性」。
/// </para>
/// <para>
/// 约束：诊断 ID 的「占位白名单」（HTTPCLIENT002/006/010/019）允许在 Diagnostics.cs 存在但不在
/// README 诊断表中列出（README 用注记说明，见 §3.4 设计约定 4）。其余 ID 必须双向一致。
/// </para>
/// </summary>
public class DocumentationContractTests
{
    private const string GeneratorReadmePath = @"../../../../../Mud.HttpUtils.Generator/README.md";
    private const string GeneratorPropsPath = @"../../../../../Mud.HttpUtils.Generator/build/Mud.HttpUtils.Generator.props";

    /// <summary>占位白名单：ID 允许在 Diagnostics.cs 中定义但不出现在 README 诊断表（README 用注记说明）。</summary>
    private static readonly HashSet<string> PlaceholderDiagnosticIds = new(StringComparer.Ordinal)
    {
        "HTTPCLIENT002", "HTTPCLIENT006", "HTTPCLIENT010", "HTTPCLIENT019",
    };

    private static readonly Regex DiagnosticRowRegex =
        new(@"\|[ \t]*`(?<id>[A-Z][A-Z0-9]*\d{3})`[ \t]*\|[ \t]*(?<severity>[^|]+?)[ \t]*\|",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// build_property. 之后的属性名应为合法 C# 标识符（MSBuild 属性名注册值亦为标识符）。
    /// 用「最长标识符前缀」提取，避免 XML 文档注释中的全角标点（如 ）。《》等）污染截取结果
    /// （G7-06 在 GeneratorConfigSnapshot 的 XML 注释含 <c>build_property.HttpClientOptionsName）。</c> 曾被误报为未注册属性）。
    /// </summary>
    private static readonly Regex BuildPropertyKeyRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string ReadReadme()
        => File.ReadAllText(Path.GetFullPath(GeneratorReadmePath));

    private static string ReadProps()
        => File.ReadAllText(Path.GetFullPath(GeneratorPropsPath));

    /// <summary>
    /// 收集 README 全部诊断表中的 (id, severity)。
    /// </summary>
    private static Dictionary<string, string> ReadReadmeDiagnostics()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in DiagnosticRowRegex.Matches(ReadReadme()))
        {
            var id = match.Groups["id"].Value;
            var severity = match.Groups["severity"].Value.Trim();
            // AOT007 分级行形如 "Error / Warning（F10 分级）"，取其首选级别。
            result[id] = severity.Split('/')[0].Trim();
        }
        return result;
    }

    /// <summary>
    /// 反射 Diagnostics 静态字段，收集 (id, severity)。返回 (descriptor, descriptorType)。
    /// </summary>
    private static Dictionary<string, (string Id, string Severity, object Descriptor)> ReadDiagnosticsDescriptors()
    {
        var result = new Dictionary<string, (string, string, object)>(StringComparer.Ordinal);
        var diagnosticsType = typeof(Mud.HttpUtils.HttpInvokeClassSourceGenerator).Assembly
            .GetType("Mud.HttpUtils.Diagnostics")
            ?? throw new InvalidOperationException("无法找到 Mud.HttpUtils.Diagnostics 类型");

        foreach (var field in diagnosticsType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType.Name != "DiagnosticDescriptor")
                continue;
            if (field.GetValue(null) is not DiagnosticDescriptor descriptor)
                continue;

            var severityName = descriptor.DefaultSeverity switch
            {
                DiagnosticSeverity.Info => "Info",
                DiagnosticSeverity.Warning => "Warning",
                _ => "Error",
            };
            result[descriptor.Id] = (descriptor.Id, severityName, descriptor);
        }

        return result;
    }

    /// <summary>
    /// 收集 props 中注册的 CompilerVisibleProperty。
    /// </summary>
    private static HashSet<string> ReadRegisteredProperties()
    {
        var props = ReadProps();
        var result = new HashSet<string>(StringComparer.Ordinal);
        var regex = new Regex(@"CompilerVisibleProperty Include=""(?<name>[^""]+)""", RegexOptions.Compiled);
        foreach (Match match in regex.Matches(props))
        {
            result.Add(match.Groups["name"].Value);
        }
        return result;
    }

    [Fact]
    public void ReadmeIdempotency_HasIgnoreGeneratorSupportedSurfaceGuard()
    {
        // [GEN-14][§8.2] 防复发：README 不得把 [IgnoreGenerator] 描述/示例为支持「属性/字段」标注面。
        // IgnoreGeneratorAttribute 已按 E-2 决策收窄为 Interface | Method，属性/字段标注会触发 CS0592。
        // 精准守卫：拦截「[IgnoreGenerator] 标注在属性/字段声明上」的代码示例（接口/方法合法示例不受影响）。
        var readme = ReadReadme();
        var propertyExamplePattern = new Regex(
            @"\[IgnoreGenerator\]\s*(?:\r?\n){0,2}\s*(?:public\s+)?[A-Za-z_][A-Za-z0-9_]*\s*\{[^}]*get;",
            RegexOptions.Compiled);
        propertyExamplePattern.IsMatch(readme).Should().BeFalse(
            "README 不得出现注明 [IgnoreGenerator] 标注属性的示例（支持面已收窄为接口/方法；属性/字段请改用占位实现 + HTTPCLIENT024 提示）");
    }

    [Fact]
    public void ReadmeDiagnosticIds_AllExistInDiagnostics_WithConsistentSeverity()
    {
        var readme = ReadReadmeDiagnostics();
        var diagnostics = ReadDiagnosticsDescriptors();

        foreach (var (id, severity) in readme)
        {
            diagnostics.Should().ContainKey(id, $"README 诊断表声明的 {id} 必须在 Diagnostics.cs 中存在");
            if (PlaceholderDiagnosticIds.Contains(id))
                continue;
            // AOT007 分级别允许文档为 "Error / Warning"，但 DefaultSeverity 为 Error（降级由调用方选择 descriptor）。
            if (id == "AOT007")
                continue;
            diagnostics[id].Severity.Should().Be(severity,
                $"README 对 {id} 的严重级别声明必须与 Diagnostics.cs 一致");
        }
    }

    [Fact]
    public void DiagnosticsNonPlaceholderIds_AllListedInReadme()
    {
        var readme = ReadReadmeDiagnostics();
        var diagnostics = ReadDiagnosticsDescriptors();

        foreach (var (id, severity, _) in diagnostics.Values)
        {
            if (PlaceholderDiagnosticIds.Contains(id))
                continue; // 占位白名单：允许未列出
            readme.Should().ContainKey(id,
                $"Diagnostics.cs 中非占位诊断 {id} 必须在 README 诊断表中出现（防止新增诊断忘记写文档）");
        }
    }

    /// <summary>
    /// README 中 MUD002 行的"受支持返回类型形态"清单必须与代码能力口径一致。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 前两条守卫只校验诊断的 <b>ID 与级别</b>，不校验<b>能力口径</b>。
    /// 历史上 <c>IAsyncEnumerable&lt;T&gt;</c> 与裸 <c>byte[]</c>/<c>Stream</c>/<c>HttpResponseMessage</c>
    /// 都属于"ID 与级别都对、能力口径错"的漏网案例（README/分析器认为支持、生成器实际不支持）。
    /// </para>
    /// <para>
    /// 实现方式：在 README 的 MUD002 行末尾维护一个受约定格式的机器可解析标记
    /// <c>&lt;!-- supported-return-shapes: Task, Task&lt;T&gt;, ... --&gt;</c>，
    /// 与本测试比对 <c>ReturnTypeSupport.SupportedReturnShapes</c>。
    /// 失败消息会同时给出两侧清单，便于直接修正。
    /// </para>
    /// </remarks>
    [Fact]
    public void ReadmeMud002SupportedShapes_MatchCodeCapability()
    {
        var readme = ReadReadme();
        var mud002Line = readme
            .Split('\n')
            .FirstOrDefault(line => line.Contains("| `MUD002` |", StringComparison.Ordinal));

        mud002Line.Should().NotBeNull("README 必须存在 MUD002 诊断行");

        const string marker = "supported-return-shapes:";
        mud002Line!.Contains(marker, StringComparison.Ordinal).Should().BeTrue(
            "MUD002 行必须维护机器可解析的形态清单标记（<!-- supported-return-shapes: Task, Task<T>, ... -->），" +
            "否则本文档的能力口径无法被守卫");

        var markerIndex = mud002Line.IndexOf(marker, StringComparison.Ordinal);
        var payload = mud002Line.Substring(markerIndex + marker.Length);
        // 只裁掉注释结束标记与首尾空白：不能用 TrimEnd('-', '>') —— 那会把末尾形态的 `<T>` 一起吃掉。
        var commentEnd = payload.IndexOf("-->", StringComparison.Ordinal);
        if (commentEnd >= 0)
            payload = payload.Substring(0, commentEnd);
        payload = payload.Trim();

        var documented = payload
            .Split(',')
            .Select(shape => shape.Trim())
            .Where(shape => shape.Length > 0)
            .ToArray();

        documented.Should().NotBeEmpty("形态清单不能为空");
        documented.Should().BeEquivalentTo(ReturnTypeSupport.SupportedReturnShapes,
            "README 承诺的受支持返回类型形态必须与 ReturnTypeSupport 的能力口径一致" +
            $"（README: [{string.Join(", ", documented)}]；代码: [{string.Join(", ", ReturnTypeSupport.SupportedReturnShapes)}]）");
    }

    [Fact]
    public void RegisteredCompilerVisibleProperties_AllHaveReadPoints()
    {
        var registered = ReadRegisteredProperties();
        registered.Should().NotBeEmpty();

        var generatorSources = Directory.EnumerateFiles(
            Path.GetFullPath(@"../../../../../Mud.HttpUtils.Generator"),
            "*.cs",
            SearchOption.AllDirectories);

        foreach (var property in registered)
        {
            var readKey = $"build_property.{property}";
            var read = generatorSources.Any(f =>
                File.ReadAllText(f).Contains(readKey, StringComparison.Ordinal));
            read.Should().BeTrue(
                $"props 注册的 CompilerVisibleProperty '{property}' 必须在生成器源码中被读取（否则配置静默失效）");
        }
    }

    [Fact]
    public void GeneratorReadsNoUnregisteredBuildProperties()
    {
        var registered = ReadRegisteredProperties();
        var generatorSources = Directory.EnumerateFiles(
            Path.GetFullPath(@"../../../../../Mud.HttpUtils.Generator"),
            "*.cs",
            SearchOption.AllDirectories);

        var usedKeys = generatorSources
            .SelectMany(f => File.ReadAllText(f).Split('\n'))
            .Select(line =>
            {
                var idx = line.IndexOf("build_property.", StringComparison.Ordinal);
                if (idx < 0) return null;
                var rest = line.Substring(idx + "build_property.".Length).Trim();
                // 仅提取合法标识符前缀：XML 注释中的全角标点（）。《》等）与自然语言后缀一律截断，
                // 与 props 注册名（均为合法 C# 标识符）保持同一字符集，杜绝解析歧义。
                var match = BuildPropertyKeyRegex.Match(rest);
                return match.Success ? match.Value : null;
            })
            .Where(k => k != null)
            .Select(k => k!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var key in usedKeys)
        {
            registered.Should().Contain(key,
                $"生成器读取的 build_property.{key} 必须已在 build/Mud.HttpUtils.Generator.props 注册为 CompilerVisibleProperty");
        }
    }
}