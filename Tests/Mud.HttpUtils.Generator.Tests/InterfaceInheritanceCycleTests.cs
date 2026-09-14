using Mud.HttpUtils.Analyzers;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// [Phase2 修复 2.1 / §3.7] 接口继承环检测：循环继承的接口层次不得导致无限递归（StackOverflow 杀 csc）。
/// </summary>
/// <remarks>
/// <para>
/// 触发条件：<c>interface A : B { } interface B : A { }</c>（csc 会报 CS0529，但 Roslyn 语法/符号层
/// 仍会构建出可遍历的继承关系）以及 <c>interface X : X</c> 这类自引用。
/// </para>
/// <para>
/// 修复前 <c>MethodAnalyzer.GetAllBaseInterfaceSyntaxNodes</c> 无 visited 集合，
/// 递归枚举基接口时无限展开——源生成器在 IDE 内会堆栈溢出（进程崩溃）。
/// 修复对齐 <c>TypeSymbolHelper.GetAllRecursive</c> 的既有正确实现。
/// </para>
/// </remarks>
public class InterfaceInheritanceCycleTests
{
    private const string CyclicSource = """
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            // 循环继承：IA : IB，IB : IA。csc 报 CS0529，但符号层仍可遍历。
            public interface IA : IB { }
            public interface IB : IA { }

            [HttpClientApi]
            public interface ICyclicApi : IA
            {
                [Get("/a")]
                Task<string> GetAAsync();
            }
        }
        """;

    private const string SelfReferenceSource = """
        using System.Threading.Tasks;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface ISelfApi : ISelfApi
            {
                [Get("/a")]
                Task<string> GetAAsync();
            }
        }
        """;

    private static Compilation CreateCompilation(string source) => CSharpCompilation.Create(
        "InterfaceCycleTest",
        [CSharpSyntaxTree.ParseText(source)],
        BasicReferenceAssemblies.GetReferences(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static InterfaceDeclarationSyntax FindInterface(Compilation compilation, string name)
        => compilation.SyntaxTrees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>())
            .First(d => d.Identifier.Text == name);

    /// <summary>
    /// 循环继承下 <see cref="MethodAnalyzer.GetAllBaseInterfaceSyntaxNodes"/> 必须终止且集合有限、无重复。
    /// </summary>
    /// <remarks>
    /// 用 <c>Task + Wait(timeout)</c> 包裹：修复前该调用会无限展开（挂起或栈溢出），
    /// 超时断言可将其转为可诊断的测试失败，而非让测试进程静默挂死。
    /// </remarks>
    [Fact]
    public void CyclicInterfaceHierarchy_TraversalTerminatesWithFiniteDistinctSet()
    {
        var compilation = CreateCompilation(CyclicSource);
        var decl = FindInterface(compilation, "ICyclicApi");

        var task = Task.Run(() =>
            MethodAnalyzer.GetAllBaseInterfaceSyntaxNodes(compilation, decl).Take(1000).ToList());

        task.Wait(TimeSpan.FromSeconds(20)).Should().BeTrue(
            "循环继承下基接口遍历必须终止（visited 集合防环；修复前会无限递归）");
        task.IsCompletedSuccessfully.Should().BeTrue(
            "循环继承下基接口遍历不应抛异常");

        var result = task.Result;
        result.Should().HaveCountLessThan(1000, "遍历结果必须是有限集合");
        result.Select(d => d.Identifier.Text).Should().OnlyHaveUniqueItems(
            "同一接口在环中只应被枚举一次（visited 集合语义）");
        result.Should().Contain(d => d.Identifier.Text == "ICyclicApi", "自身必须被枚举");
    }

    /// <summary>自引用接口（<c>ISelfApi : ISelfApi</c>）同样必须终止。</summary>
    [Fact]
    public void SelfReferencingInterface_TraversalTerminates()
    {
        var compilation = CreateCompilation(SelfReferenceSource);
        var decl = FindInterface(compilation, "ISelfApi");

        var task = Task.Run(() =>
            MethodAnalyzer.GetAllBaseInterfaceSyntaxNodes(compilation, decl).Take(1000).ToList());

        task.Wait(TimeSpan.FromSeconds(20)).Should().BeTrue("自引用接口遍历必须终止");
        task.Result.Should().HaveCountLessThan(1000);
    }

    /// <summary>
    /// 端到端：生成器在循环继承输入上必须「跑完」而不是崩溃（异常已由生成器护栏转为诊断）。
    /// </summary>
    [Fact]
    public void Generator_OnCyclicHierarchy_CompletesWithoutThrowing()
    {
        var compilation = CreateCompilation(CyclicSource);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());

        var act = () => driver.RunGenerators(compilation);

        act.Should().NotThrow("循环继承不得让生成器崩溃（应产出诊断或跳过）");
    }
}
