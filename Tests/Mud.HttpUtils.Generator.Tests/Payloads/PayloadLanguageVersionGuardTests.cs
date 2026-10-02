// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 消费面最低基线守卫：生成代码必须在<b>C# 7.3 + netstandard2.0</b> 下零错误编译。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要本守卫</b>：外部 <c>netstandard2.0</c> 消费工程的<b>默认</b> <c>LangVersion</c> 是
/// <c>7.3</c>（实测 CS8370：<c>static</c> 匿名函数需 C# 9、集合表达式需 C# 12）。
/// 而生成代码是「注入消费方编译」的第三方源码，一旦使用高于 7.3 的语法，消费方将看到
/// 指向生成文件的语法错误。既有 <c>MultiTfmCompilationGuardTests</c> 只切换<b>预处理符号</b>、
/// <b>不</b>切换 <c>LangVersion</c>（语言版本默认取 <c>Latest</c>），故<b>无法</b>覆盖本不变量。
/// </para>
/// <para>
/// 同一条断言还隐含：生成代码不得发射 <c>#nullable enable</c>（C# 8 语法，在 7.3 下 CS8630）——
/// 本生成器按配置的 <c>build_property.Nullable</c> 决定是否发射，未启用 nullable 的消费工程不会看到该指令。
/// </para>
/// </remarks>
public class PayloadLanguageVersionGuardTests
{
    private static (IEnumerable<string> GeneratedTexts, ImmutableArray<Diagnostic> Errors) Run(
        LanguageVersion languageVersion,
        string? nullableProperty)
    {
        var parseOptions = new CSharpParseOptions(languageVersion)
            .WithPreprocessorSymbols("NETSTANDARD2_0");

        var compilation = CSharpCompilation.Create(
            "PayloadLanguageVersionGuard",
            new[] { CSharpSyntaxTree.ParseText(PayloadTestData.LegacySource, parseOptions) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var buildProperties = new Dictionary<string, string>();
        if (nullableProperty != null)
            buildProperties["build_property.Nullable"] = nullableProperty;

        var driver = CSharpGeneratorDriver.Create(
            new[] { new PayloadFieldMapGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(buildProperties));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var errors = output.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        var generatedTexts = output.SyntaxTrees
            .Skip(1)
            .Select(tree => tree.ToString())
            .ToArray();

        return (generatedTexts, errors);
    }

    [Fact]
    public void GeneratedCode_CompilesUnder_CSharp73_NetStandard20()
    {
        var (generatedTexts, errors) = Run(LanguageVersion.CSharp7_3, "disable");

        errors.Should().BeEmpty(
            "生成代码必须在 netstandard2.0 消费工程的默认 LangVersion（C# 7.3）下可编译；错误：" +
            string.Join("\n", errors.Select(e => e.ToString())));

        generatedTexts.Should().NotBeEmpty("生成器必须产出映射表");

        generatedTexts.Should().NotContain(
            text => text.Contains("#nullable enable", System.StringComparison.Ordinal),
            "Nullable=disable 的消费工程下不得发射 #nullable enable（C# 8 语法，7.3 下 CS8630）");

        generatedTexts.Should().NotContain(
            text => text.Contains("static (", System.StringComparison.Ordinal),
            "生成代码不得使用 static 匿名函数（C# 9，7.3 下 CS8370）——无捕获性由生成器构造保证");
    }

    [Fact]
    public void GeneratedCode_EmitsNullableEnable_WhenConsumerEnablesNullable()
    {
        var (generatedTexts, errors) = Run(LanguageVersion.Latest, "enable");

        errors.Should().BeEmpty();
        generatedTexts.Should().Contain(
            text => text.Contains("#nullable enable", System.StringComparison.Ordinal),
            "Nullable=enable 的消费工程下必须发射 #nullable enable（与既有生成器同口径）");
    }
}
