// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 生成物的「可空性卫生」守卫：生成代码不得把可空性告警泄漏到消费方构建。
/// </summary>
/// <remarks>
/// <para>
/// <c>// &lt;auto-generated/&gt;</c> 文件头<b>不</b>抑制 <c>CS8619</c>/<c>CS8601</c>（实测），
/// 而生成代码的转换器调用会以其<b>返回类型</b>参与赋值 ⇒ 泛型实参的渲染格式必须保留可空标注：
/// <c>List&lt;string?&gt;</c> 属性若渲染成 <c>Delimited&lt;string&gt;</c>（返回 <c>List&lt;string&gt;</c>），
/// 消费方会看到 <c>CS8619</c>（可空性不匹配，错误指向生成文件），开 <c>TreatWarningsAsErrors</c> 即构建失败。
/// </para>
/// <para>
/// 断言方式：对比「输入编译」与「生成后编译」的告警集合，只允许**新增为空**
/// （输入源自身的 CS0105 等存量告警被基线扣除），并正向钉住生成文本中的 <c>Delimited&lt;string?&gt;</c>，
/// 以防「告警集合为空」因用例输入退化而空转变绿。
/// </para>
/// </remarks>
public class PayloadNullabilityGuardTests
{
    /// <summary>含可空引用类型元素的载荷（<c>List&lt;string?&gt;</c> 的两种推断形态）。</summary>
    private const string NullableElementPayload = """
        namespace PayloadTests
        {
            [PayloadContract(Converter = typeof(PayloadConverter))]
            public sealed partial class NullableElementPayload
            {
                [PayloadField("Names")] public List<string?> Names { get; set; } = new List<string?>();

                [PayloadField("Tags", ItemName = "Tag")] public List<string?> Tags { get; set; } = new List<string?>();
            }
        }
        """;

    [Fact]
    public void GeneratedCode_DoesNotIntroduceNullableWarnings_WhenConsumerEnablesNullable()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "PayloadNullabilityGuard",
            new[] { CSharpSyntaxTree.ParseText(PayloadTestData.Source(NullableElementPayload), parseOptions) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var buildProperties = new Dictionary<string, string> { ["build_property.Nullable"] = "enable" };
        var driver = CSharpGeneratorDriver.Create(
            new[] { new PayloadFieldMapGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        diagnostics.Should().BeEmpty("本用例的载荷声明本身合法，生成器不应报告任何诊断");

        var baseline = compilation.GetDiagnostics()
            .Select(d => d.Id + "|" + d.GetMessage())
            .ToHashSet();

        var introduced = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Warning || d.Severity == DiagnosticSeverity.Error)
            .Where(d => !baseline.Contains(d.Id + "|" + d.GetMessage()))
            .Select(d => d.Id + ": " + d.GetMessage())
            .ToArray();

        introduced.Should().BeEmpty(
            "生成代码必须自带正确的可空性（如 Delimited<string?>），不得把 CS8619/CS8601 泄漏到消费方构建；新增：" +
            string.Join(" | ", introduced));

        var generatedText = string.Join("\n", output.SyntaxTrees.Skip(1).Select(tree => tree.ToString()));

        generatedText.Should().NotBeEmpty("生成器必须产出映射表（否则本用例会空转变绿）");

        generatedText.Should().Contain(
            "Delimited<string?>(n, ',')",
            "可空元素类型必须以带标注的实参渲染，否则返回的 List<string> 与属性 List<string?> 可空性不匹配");
    }
}
