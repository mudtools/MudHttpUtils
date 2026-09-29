// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯社会公共秩序等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// G9-07 / D-03 契约：<c>build_property.Nullable</c> 对全部产物形态（实现类 / 注册 / FormContent）的
/// <c>#nullable enable</c> 条件化发射钉子。
/// </summary>
/// <remarks>
/// 历史缺口：FormContentGenerator 从未读取该配置（<c>EmitNullableEnable</c> 恒为默认 true），
/// 与「消费项目 Nullable=disable 不发射」的 D-03 契约隐性不一致。G9-07 将配置以参数化方式接线
/// （FormContentGenerator 经 G7-06 值相等快照模式读取），本类钉住三种产物形态的双向行为。
/// </remarks>
public class NullableConfigurationEmitTests
{
    private const string ImplicitUsingsPreamble = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;

        """;

    private const string Source = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface ITestApi
            {
                [Get("/users")]
                Task<string> GetUsersAsync();
            }

            [FormContent]
            public partial class UploadForm
            {
                [FilePath]
                public string DocumentPath { get; set; } = string.Empty;
            }
        }
        """;

    /// <summary>Nullable=disable：全部产物（实现类 / 注册 / FormContent）不得发射 #nullable enable。</summary>
    [Fact]
    public void NullableDisable_NoGeneratedFileEmitsNullableEnable()
    {
        var generated = RunGenerators(new Dictionary<string, string>
        {
            ["build_property.Nullable"] = "disable",
        });

        generated.Should().NotBeEmpty("生成器必须产出实现类 / 注册 / FormContent 产物");

        var offenders = generated
            .Where(t => t.Content.Contains("#nullable enable", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty(
            "Nullable=disable 的消费项目下，任何产物形态都不得发射 #nullable enable" +
            $"（越界产物：[{string.Join(", ", offenders.Take(3).Select(t => t.TreeName))}]）");
    }

    /// <summary>正向对照：默认（enable）配置下产物必须发射 #nullable enable（防止断言空转变绿）。</summary>
    [Fact]
    public void NullableEnable_DefaultConfiguration_StillEmitsNullableEnable()
    {
        var generated = RunGenerators(new Dictionary<string, string>());

        generated.Any(t => t.Content.Contains("#nullable enable", StringComparison.Ordinal))
            .Should().BeTrue(
                "默认 Nullable=enable 配置下产物必须发射 #nullable enable（D-03 正向行为不得被 G9-07 破坏）");
    }

    /// <summary>
    /// 以指定 build_property 配置驱动三类生成器（实现类 / 注册 / FormContent），返回生成产物文本。
    /// </summary>
    private static List<(string TreeName, string Content)> RunGenerators(Dictionary<string, string> buildProperties)
    {
        var references = BasicReferenceAssemblies.GetReferences();
        var syntaxTree = CSharpSyntaxTree.ParseText(ImplicitUsingsPreamble + Source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[]
            {
                new HttpInvokeClassSourceGenerator().AsSourceGenerator(),
                new HttpInvokeRegistrationGenerator().AsSourceGenerator(),
                new FormContentGenerator().AsSourceGenerator(),
            },
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        return output.SyntaxTrees.Skip(1)
            .Select(t => (TreeName: t.FilePath, Content: t.ToString()))
            .ToList();
    }
}
