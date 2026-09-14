// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.CodeAnalysis.Diagnostics;
using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// E-5：接口级 [IgnoreGenerator] 生成期整体生效（决策 2026-09-13：接口与方法均支持）。
/// <para>
/// 接口级忽略 = 生成器完全不介入（不生成实现类/DI 注册/工厂，不报该接口的 AOT/MUD 诊断）；
/// 方法级忽略 = 仅跳过该方法，同接口其他方法照常生成（回归）。
/// </para>
/// </summary>
public class IgnoreGeneratorTests
{
    private const string CancellationTokenSignature = "global::System.Threading.CancellationToken";

    private static (GeneratorDriver Driver, Compilation Output) RunBothGenerators(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "IgnoreGeneratorTests",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeClassSourceGenerator();
        var registration = new HttpInvokeRegistrationGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { generator.AsSourceGenerator(), registration.AsSourceGenerator() });
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver, output);
    }

    private static Compilation RunForImplementation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "IgnoreGeneratorTests",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeClassSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output;
    }

    private static ImmutableArray<Diagnostic> RunWithAnalyzers(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "IgnoreGeneratorTests",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analysis = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(
            new HttpJsonSerializableCoverageAnalyzer(),
            new MudHttpInterfaceAnalyzer()));
        return analysis.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private const string InterfaceLevelIgnoreSource = """
        using System;
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            [IgnoreGenerator]
            public interface IApi
            {
                [Get("/users/{id}")]
                Task<string> GetUserAsync([Path] int id);
            }
        }
        """;

    private const string InterfaceLevelIgnoreXmlSource = """
        using System;
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            [IgnoreGenerator]
            public interface IXmlApi
            {
                [Post("/api/data")]
                [SerializationMethod(SerializationMethod.Xml)]
                Task<string> PostDataAsync([Body] MyDto data);
            }

            public class MyDto { public string Name { get; set; } }
        }
        """;

    [Fact]
    public void InterfaceLevelIgnoreGenerator_NoGeneratedSource()
    {
        var output = RunForImplementation(InterfaceLevelIgnoreSource);

        var generatedTrees = output.SyntaxTrees.Skip(1).ToList();
        generatedTrees.Should().NotContain(t => t.ToString().Contains("class Api :"),
            "接口级 [IgnoreGenerator] 不应生成实现类");

        // 注册/工厂中亦不应出现该接口
        var allGenerated = string.Join("\n", generatedTrees.Select(t => t.ToString()));
        allGenerated.Should().NotContain("IApi", "接口级 [IgnoreGenerator] 不应出现在注册代码中");
    }

    [Fact]
    public void InterfaceLevelIgnoreGenerator_NoAOT007()
    {
        // XML 方法 + AOT 上下文 → 原实现报 AOT007 Error 阻断构建；修复后跳过。
        var syntaxTree = CSharpSyntaxTree.ParseText(InterfaceLevelIgnoreXmlSource);
        var compilation = CSharpCompilation.Create(
            "IgnoreGeneratorTests",
            new[] { syntaxTree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = Mud.HttpUtils.Analyzers.AotXmlRejectionAnalyzer.Analyze(
            compilation, isAotContext: true, CancellationToken.None);

        diagnostics.Should().NotContain(d => d.Id == "AOT007",
            "接口级 [IgnoreGenerator] 不应产生 AOT007（用户自备实现，不存在 XmlSerializer 代码）");
    }

    [Fact]
    public void InterfaceLevelIgnoreGenerator_NoMUD001Or002()
    {
        var diagnostics = RunWithAnalyzers(InterfaceLevelIgnoreSource);
        diagnostics.Should().NotContain(d => d.Id == "MUD001");
        diagnostics.Should().NotContain(d => d.Id == "MUD002");
    }

    [Fact]
    public void MethodLevelIgnoreGenerator_StillSkipsOnlyThatMethod()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/users/{id}")]
                    Task<string> GetUserAsync([Path] int id);

                    [IgnoreGenerator]
                    [Get("/manual")]
                    Task<string> ManualAsync();
                }
            }
            """;

        var output = RunForImplementation(source);
        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        generated.Should().Contain("GetUserAsync", "同接口其他方法照常生成");
        generated.Should().NotContain("ManualAsync", "方法级 [IgnoreGenerator] 仅跳过该方法");
    }
}