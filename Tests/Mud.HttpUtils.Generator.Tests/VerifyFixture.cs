// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using VerifyXunit;
using Verify = VerifyXunit.Verifier;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// Verify 快照测试辅助类，封装生成器驱动的快照验证逻辑。
/// </summary>
public static class VerifyFixture
{
    private static readonly ConcurrentDictionary<string, MetadataReference> _metadataRefCache = new();

    /// <summary>
    /// 运行生成器并验证生成代码的快照。
    /// 提取生成器输出的所有 SyntaxTree 文本，过滤后作为快照验证目标。
    /// </summary>
    /// <param name="driver">已运行的 CSharpGeneratorDriver。</param>
    /// <param name="sourceFile">由编译器自动填充的源文件路径。</param>
    /// <returns>验证任务。</returns>
    public static async Task VerifyGenerator(
        CSharpGeneratorDriver driver,
        Compilation outputCompilation,
        [CallerFilePath] string sourceFile = "")
    {
        // [M0] 快照前先做编译断言：快照只负责形状，编译断言负责「生成代码必须可编译」（F15）。
        // 输入 + 生成产物整体不应有 Error 级编译诊断；
        // 期望若干诊断（如 HTTPCLIENT012 泛型 Info）的用例不在此路径下断言。
        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        errors.Should().BeEmpty(
            $"快照用例的输入 + 生成产物必须可编译（F15）；错误：{string.Join("\n", errors.Select(e => e.ToString()))}");

        // 从 outputCompilation 中提取生成器添加的语法树（跳过原始输入语法树）
        var generatedSources = new List<(string HintName, string Source)>();
        foreach (var tree in outputCompilation.SyntaxTrees.Skip(1))
        {
            var hintName = Path.GetFileName(tree.FilePath);
            // 跳过不需要快照验证的生成代码
            if (hintName.Contains("PreserveAttribute"))
                continue;
            if (hintName.Contains("Registration"))
                continue;
            if (hintName.Contains("EventHandler"))
                continue;
            if (hintName.Contains("FormContent"))
                continue;

            generatedSources.Add((hintName, tree.ToString()));
        }

        // 将生成代码合并为一个文本进行验证
        var combined = new StringBuilder();
        foreach (var (hintName, source) in generatedSources.OrderBy(x => x.HintName))
        {
            combined.AppendLine($"// ===== {hintName} =====");
            combined.AppendLine(source);
            combined.AppendLine();
        }

        var settings = new VerifySettings();
        // 版本脱敏：生成代码的 [GeneratedCode] 特性烧录组件版本号，任何版本提升都会导致全部快照失效。
        // 统一归一化为 <VERSION> 占位符，使快照仅反映代码形状（版本变更不再需要重新接受快照）。
        settings.AddScrubber(sb =>
        {
            var scrubbed = System.Text.RegularExpressions.Regex.Replace(
                sb.ToString(),
                "(GeneratedCode\\(\"Mud\\.HttpUtils\\.Generator\",\\s*\")[^\"]+(\")",
                "${1}<VERSION>${2}");
            sb.Clear();
            sb.Append(scrubbed);
        });
        await Verify.Verify(combined.ToString(), settings, sourceFile);
    }

    /// <summary>
    /// 创建编译并运行生成器，返回驱动程序以供快照验证。
    /// </summary>
    /// <param name="source">源代码。</param>
    /// <param name="additionalReferences">额外的元数据引用。</param>
    /// <returns>已运行生成器的驱动程序。</returns>
    public static (CSharpGeneratorDriver driver, Compilation outputCompilation) RunGeneratorDriver(
        string source,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var references = BasicReferenceAssemblies.GetReferences();
        if (additionalReferences != null)
        {
            references.AddRange(additionalReferences);
        }

        // [M0/F15] 编译断言要求「输入 + 生成产物」整体可编译。快照测试的输入源天然省略
        // <ImplicitUsings>enable</ImplicitUsings> 提供的标准 using（消费项目在项目文件中全局启用）。
        // 此处补齐标准隐式 using，使输入源等价于合法消费配置；
        // 生成产物仍按自包含校验（用其自带的 using 头），因此 F2「缺 using」仍能被编译断言捕获。
        var effectiveSource = ImplicitUsingsPreamble + source;

        var syntaxTree = CSharpSyntaxTree.ParseText(effectiveSource);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver, outputCompilation);
    }

    /// <summary>
    /// 模拟 <c>&lt;ImplicitUsings&gt;enable&lt;/ImplicitUsings&gt;</c> 的标准隐式 using 头。
    /// </summary>
    private const string ImplicitUsingsPreamble = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;

        """;

    /// <summary>
    /// 获取或缓存元数据引用。
    /// </summary>
    public static MetadataReference GetMetadataReference(string path)
    {
        return _metadataRefCache.GetOrAdd(path, p => MetadataReference.CreateFromFile(p));
    }
}
