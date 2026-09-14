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
    private const string GeneratorReadmePath = @"..\..\..\..\Mud.HttpUtils.Generator\README.md";
    private const string GeneratorPropsPath = @"..\..\..\..\Mud.HttpUtils.Generator\build\Mud.HttpUtils.Generator.props";

    /// <summary>占位白名单：ID 允许在 Diagnostics.cs 中定义但不出现在 README 诊断表（README 用注记说明）。</summary>
    private static readonly HashSet<string> PlaceholderDiagnosticIds = new(StringComparer.Ordinal)
    {
        "HTTPCLIENT002", "HTTPCLIENT006", "HTTPCLIENT010", "HTTPCLIENT019",
    };

    private static readonly Regex DiagnosticRowRegex =
        new(@"\|[ \t]*`(?<id>[A-Z][A-Z0-9]*\d{3})`[ \t]*\|[ \t]*(?<severity>[^|]+?)[ \t]*\|",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    [Fact]
    public void RegisteredCompilerVisibleProperties_AllHaveReadPoints()
    {
        var registered = ReadRegisteredProperties();
        registered.Should().NotBeEmpty();

        var generatorSources = Directory.EnumerateFiles(
            Path.GetFullPath(@"..\..\..\..\Mud.HttpUtils.Generator"),
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
            Path.GetFullPath(@"..\..\..\..\Mud.HttpUtils.Generator"),
            "*.cs",
            SearchOption.AllDirectories);

        var usedKeys = generatorSources
            .SelectMany(f => File.ReadAllText(f).Split('\n'))
            .Select(line =>
            {
                var idx = line.IndexOf("build_property.", StringComparison.Ordinal);
                if (idx < 0) return null;
                var rest = line.Substring(idx + "build_property.".Length).Trim();
                var end = rest.IndexOfAny(['"', ',', ' ', ')', ';', '=', '<', '>', '`']);
                return end > 0 ? rest.Substring(0, end) : rest;
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