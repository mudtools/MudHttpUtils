// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Mud.HttpUtils.Generator.Tests.Incremental;

/// <summary>
/// 增量行为测试(对齐 Refit 的 <c>Incremental/</c> 设计意图)。
/// </summary>
/// <remarks>
/// 本仓库的生成器管线通过 <c>WithComparer(EqualityComparer&lt;InterfaceModel&gt;.Default)</c> 进行增量比较，
/// 而 <see cref="Mud.HttpUtils.Models.InterfaceModel"/> 的 <c>Equals</c> 以 <c>Fingerprint</c> 为判等依据。
/// 因此"无关变更不重新生成 / 接口变更重新生成"的增量正确性,等价于断言两次构建的
/// <see cref="Mud.HttpUtils.Models.InterfaceModel"/> 是否相等。
/// <para>
/// 注:Refit 的对应测试使用 <c>GeneratorDriverOptions(trackIncrementalSteps: true)</c> 直接断言
/// <c>IncrementalStepRunReason.Cached/Modified</c>。本仓库的 <c>HttpInvokeClassSourceGenerator</c> 仅实现
/// <c>IIncrementalGenerator</c>,且测试运行时加载的 Roslyn 程序集存在版本碎片化
/// (<c>IIncrementalGenerator</c> 取自 SDK 内联 Roslyn,而 <c>ISourceGenerator</c> 取自另一份
/// <c>Microsoft.CodeAnalysis</c>),导致无法在本单元测试中向生成器注入 <c>GeneratorDriverOptions</c>
/// (既有的 <c>AotXmlRejectionTests</c> 已记录该限制)。故此处直接对增量比较的核心单元
/// <see cref="Mud.HttpUtils.Models.InterfaceModel"/> 判等逻辑做断言,等价验证增量缓存语义。
/// </para>
/// </remarks>
public class IncrementalTests
{
    private const string InterfaceSource = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
        public interface IApi
        {
            [Get("/users/{id}")]
            Task<string> GetAsync([Path] int id);
        }
        """;

    private const string InterfaceSourceWithUnrelatedClass =
        InterfaceSource + "\n// unrelated comment\npublic class Unrelated { }\n";

    private const string InterfaceSourceWithExtraMethod = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
        public interface IApi
        {
            [Get("/users/{id}")]
            Task<string> GetAsync([Path] int id);

            [Get("/users")]
            Task<string> ListAsync();
        }
        """;

    // 仅注释变更:不影响生成代码,应命中缓存。
    private const string InterfaceSourceWithLeadingComment = """
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        // 仅注释变更:不影响生成代码
        [HttpClientApi(HttpClient = "IEnhancedHttpClient")]
        public interface IApi
        {
            [Get("/users/{id}")]
            Task<string> GetAsync([Path] int id);
        }
        """;

    [Fact]
    public void UnrelatedFileChange_ShouldNotRegenerate()
    {
        var before = BuildModel(InterfaceSource);
        var after = BuildModel(InterfaceSourceWithUnrelatedClass);

        Assert.True(before.Equals(after),
            "无关类型/注释追加不应改变 InterfaceModel 指纹(应命中增量缓存,不重新生成)。");
    }

    [Fact]
    public void InterfaceMethodChange_ShouldRegenerate()
    {
        var before = BuildModel(InterfaceSource);
        var after = BuildModel(InterfaceSourceWithExtraMethod);

        Assert.False(before.Equals(after),
            "接口方法新增应改变 InterfaceModel 指纹(应触发重新生成)。");
    }

    // 评审补充(Mud 独有):基于 InterfaceModel 指纹设计(指纹不含注释/空白琐事),
    // 仅注释变更应命中缓存,不应触发重生成。
    [Fact]
    public void CommentOnlyChange_ShouldNotRegenerate()
    {
        var before = BuildModel(InterfaceSource);
        var after = BuildModel(InterfaceSourceWithLeadingComment);

        Assert.True(before.Equals(after),
            "仅注释变更不应改变 InterfaceModel 指纹(应命中增量缓存,不重新生成)。");
    }

    // 反向补充:HttpClientApi 关键特性值(如 HttpClient)变更应触发重生成。
    [Fact]
    public void HttpClientAttributeValueChange_ShouldRegenerate()
    {
        var before = BuildModel(InterfaceSource);
        var after = BuildModel("""
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            [HttpClientApi(HttpClient = "IOtherHttpClient")]
            public interface IApi
            {
                [Get("/users/{id}")]
                Task<string> GetAsync([Path] int id);
            }
            """);

        Assert.False(before.Equals(after),
            "HttpClientApi.HttpClient 关键特性值变更应改变 InterfaceModel 指纹(应触发重新生成)。");
    }

    private static Mud.HttpUtils.Models.InterfaceModel BuildModel(string source)
    {
        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var semanticModel = compilation.GetSemanticModel(tree);
        var iface = tree.GetRoot()
            .DescendantNodes()
            .OfType<InterfaceDeclarationSyntax>()
            .First();

        var symbol = semanticModel.GetDeclaredSymbol(iface)
            ?? throw new InvalidOperationException("无法解析接口符号。");
        var attributes = symbol.GetAttributes();

        var context = CreateContext(iface, symbol, semanticModel, attributes);
        return new Mud.HttpUtils.Models.InterfaceModel(iface, context);
    }

    // GeneratorAttributeSyntaxContext 的构造函数为 internal,通过反射调用构造结构体实例。
    private static GeneratorAttributeSyntaxContext CreateContext(
        SyntaxNode node,
        ISymbol symbol,
        SemanticModel semanticModel,
        ImmutableArray<AttributeData> attributes)
    {
        var ctor = typeof(GeneratorAttributeSyntaxContext)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single();

        return (GeneratorAttributeSyntaxContext)ctor.Invoke(
            new object[] { node, symbol, semanticModel, attributes });
    }

    private static Compilation CreateCompilation(string source)
    {
        var references = BasicReferenceAssemblies.GetReferences();
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
