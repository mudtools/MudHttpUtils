// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯社会公共秩序等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G9-05：类型解析快速路径的行为钉子——文件级命名空间（<c>namespace X;</c>）下以简单名引用的
/// <c>TokenManage</c> 类型必须正确<b>解析</b>（不误报 <c>TokenManagerTypeNotFound</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 历史实现只枚举块级 <c>NamespaceDeclarationSyntax</c>，文件级声明（C# 10 起主流写法）不在快速路径内，
/// 全部落到更贵的全树保底扫描（正确性由保底兜住，属性能/命中率缺口——文件级命名空间消费项目的
/// 每个未命中快速路径的简单类型名都要付一次全树扫描）。G9-05 改用
/// <c>BaseNamespaceDeclarationSyntax</c>（块级 + 文件级）并按 Compilation 弱引用缓存命名空间名单。
/// </para>
/// <para>
/// <b>可观测性边界</b>：解析结果只用于校验（TokenManager 契约成员检查 / 诊断正确性），
/// 发射端使用特性原始字符串（<c>ConstructorGenerator</c> 的 <c>Configuration.TokenManagerType</c>），
/// 因此「解析成功」的行为信号是<b>不出现 TokenManagerTypeNotFound 诊断</b>，而非产物文本差异。
/// 测试构造说明：C# 限制单文件最多一个文件级命名空间且其必须位于所有成员之前（CS8954/CS8956），
/// 故跨命名空间场景由<b>两棵语法树</b>构成（文件级声明独占一棵）——这正是文件级命名空间消费项目的真实形态。
/// </para>
/// </remarks>
public class FileScopedNamespaceTypeResolutionTests
{
    /// <summary>接口树：接口命名空间（TestNamespace）与 TokenManage 类型命名空间（OtherNamespace）不同，快速路径前三个捷径（全名 / Mud.HttpUtils 前缀 / 接口命名空间前缀）均不命中。</summary>
    private const string ApiTreeSource = """
        using System;
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(TokenManage = "ITestTokenManager")]
            public interface ITestApi
            {
                [Get("/users")]
                Task<string> GetDataAsync();
            }
        }
        """;

    /// <summary>TokenManage 树（文件级命名空间形态）：只有 G9-05 补齐的 BaseNamespaceDeclarationSyntax 枚举才能进快速路径。</summary>
    private const string FileScopedTokenManagerTree = """
        namespace OtherNamespace;

        public interface ITestTokenManager
        {
            Mud.HttpUtils.IMudAppContext GetDefaultApp();
            Mud.HttpUtils.IMudAppContext GetApp(string appKey);
        }
        """;

    /// <summary>TokenManage 树（块级命名空间对照形态）。</summary>
    private const string BlockScopedTokenManagerTree = """
        namespace OtherNamespace
        {
            public interface ITestTokenManager
            {
                Mud.HttpUtils.IMudAppContext GetDefaultApp();
                Mud.HttpUtils.IMudAppContext GetApp(string appKey);
            }
        }
        """;

    /// <summary>文件级命名空间下的简单名 TokenManage 必须被解析成功（不误报类型未找到）。</summary>
    [Fact]
    public void TokenManagerType_ResolvedInFileScopedNamespace_EmitsTypedField()
    {
        var (generated, generatorDiagnostics) = RunScenario(FileScopedTokenManagerTree);

        generatorDiagnostics.Should().NotContain(d => d.Id == Diagnostics.TokenManagerTypeNotFound.Id,
            "文件级命名空间（namespace X;）声明的 TokenManage 类型必须被解析成功" +
            "（G9-05 补齐快速路径；若误报未找到则校验链路整体失效）");
        generated.Should().Contain("ITestTokenManager _tokenManager",
            "TokenManage 类型必须照常发射为 _tokenManager 字段（发射用特性原始名，与解析解耦）");
    }

    /// <summary>块级命名空间对照：G9-05 重构不得反向破坏既有命中场景。</summary>
    [Fact]
    public void TokenManagerType_ResolvedInBlockScopedNamespace_StillEmitsTypedField()
    {
        var (generated, generatorDiagnostics) = RunScenario(BlockScopedTokenManagerTree);

        generatorDiagnostics.Should().NotContain(d => d.Id == Diagnostics.TokenManagerTypeNotFound.Id,
            "块级命名空间（既有命中场景）的解析行为不得被 G9-05 破坏");
        generated.Should().Contain("ITestTokenManager _tokenManager");
    }

    /// <summary>端到端正例：接口与 TokenManage 类型同处一个文件级命名空间（真实消费形态）——产物可编译。</summary>
    [Fact]
    public void SameFileScopedNamespace_TokenManager_ProducesCompilableOutput()
    {
        var source = """
            using System;
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace;

            public interface ITestTokenManager
            {
                IMudAppContext GetDefaultApp();
                IMudAppContext GetApp(string appKey);
            }

            [HttpClientApi(TokenManage = "ITestTokenManager")]
            public interface ITestApi
            {
                [Get("/users")]
                Task<string> GetDataAsync();
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "文件级命名空间消费场景（接口 + TokenManage 同命名空间）");

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generated.Should().Contain("ITestTokenManager _tokenManager",
            "文件级命名空间消费场景必须产出可编译的类型化 _tokenManager 字段");
    }

    /// <summary>
    /// G9-05 机制钉子（源码文本级）：快速路径的命名空间枚举必须使用 <c>BaseNamespaceDeclarationSyntax</c>
    /// （同时覆盖块级与文件级），防止未来改动退回块级-only 枚举。
    /// </summary>
    [Fact]
    public void FindTypeInSourceNamespaces_MustEnumerateBaseNamespaceDeclarations()
    {
        var source = File.ReadAllText(TestRepoRoot.PathOf(
            "Mud.HttpUtils.Generator", "HttpInvoke", "Implementation", "InterfaceImplementationGenerator.cs"));

        source.Should().Contain("BaseNamespaceDeclarationSyntax",
            "快速路径命名空间枚举必须使用 BaseNamespaceDeclarationSyntax（块级 + 文件级），" +
            "退回块级-only 的 NamespaceDeclarationSyntax 即 G9-05 缺陷复现");
    }

    /// <summary>以「TokenManage 类型树 + 接口树」两棵语法树运行生成器，返回（生成源文本, 生成器诊断）。</summary>
    private static (string Generated, ImmutableArray<Diagnostic> GeneratorDiagnostics) RunScenario(
        string tokenManagerTreeSource)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[]
            {
                CSharpSyntaxTree.ParseText(tokenManagerTreeSource),
                CSharpSyntaxTree.ParseText(ApiTreeSource),
            },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new HttpInvokeClassSourceGenerator().AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var generated = string.Join("\n", output.SyntaxTrees.Skip(2).Select(t => t.ToString()));
        var generatorDiagnostics = driver.GetRunResult().Diagnostics;
        return (generated, generatorDiagnostics);
    }
}
