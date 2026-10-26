// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// T-06（CFG-03）：注册生成器产出的命名 HttpClient 超时契约。
/// <para>
/// 修复前生成器回退默认值为 100（与 <see cref="Mud.HttpUtils.Attributes.HttpClientApiAttribute"/>
/// 的对外文档契约「默认 50 秒」不一致，BC-1）。本组测试锁定：未显式设置 <c>Timeout</c> 时生成
/// <c>TimeSpan.FromSeconds(50)</c>；显式设置时使用显式值。
/// </para>
/// <para>
/// MT-14（BC-21）：注册入口由裸 <c>services.AddHttpClient</c> 改为
/// <c>HttpClientServiceCollectionExtensions.AddMudHttpClient</c>（完全限定调用，避免依赖消费方 using），
/// 使生成客户端获得 keyed 注册与 <c>CreateEnhancedClient</c> 的配置覆盖。本组同步锁定该契约。
/// </para>
/// </summary>
public class RegistrationTimeoutGenerationTests
{
    private static string? RunRegistrationGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeRegistrationGenerator();
        CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString();
    }

    /// <summary>跑注册生成器并拼接全部生成树（扩展方法文件 + 工厂注册文件）。</summary>
    private static string RunRegistrationGeneratorAllTrees(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new HttpInvokeRegistrationGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        return string.Join("\n", outputCompilation.SyntaxTrees.Skip(1).Select(t => t.ToString()));
    }

    /// <summary>跑注册生成器并返回（生成文本, 诊断）。</summary>
    private static (string? Generated, ImmutableArray<Diagnostic> Diagnostics) RunRegistrationGeneratorWithDiagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeRegistrationGenerator();
        CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString(), diagnostics);
    }

    /// <summary>
    /// P1-3 回归：<c>[HttpClientApi(Timeout = 0)]</c> 不得生成 <c>FromSeconds(0)</c>
    /// （HttpClient.Timeout 不接受 Zero，运行期 AOORE），并恰报 1 条 HTTPCLIENT042。
    /// </summary>
    [Fact]
    public void CFG03_HttpClientApiWithZeroTimeout_SkipsAssignmentAndReportsHttpClient042()
    {
        var source = """
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi(Timeout = 0)]
                public interface ITimeoutZeroApi
                {
                    [Get("/users")]
                    System.Threading.Tasks.Task<string> GetUsersAsync();
                }
            }
            """;

        var (generated, diagnostics) = RunRegistrationGeneratorWithDiagnostics(source);

        generated.Should().NotBeNullOrEmpty();
        generated.Should().NotContain("TimeSpan.FromSeconds(0)",
            "Timeout = 0 不得生成 client.Timeout 赋值（运行期 ArgumentOutOfRangeException）");
        generated.Should().Contain("AddMudHttpClient(services,",
            "命名客户端注册本身仍须经 AddMudHttpClient（不传配置委托）");

        diagnostics.Where(d => d.Id == "HTTPCLIENT042").Should().ContainSingle(
            "Timeout ≤ 0 必须恰报 1 条 HTTPCLIENT042 指向特性");

        // P1-3 补充：Location 必须落在消费方源码（此前 ProcessInterface 未传 Location ⇒ None，IDE 无法导航）。
        var timeoutDiagnostic = diagnostics.Single(d => d.Id == "HTTPCLIENT042");
        timeoutDiagnostic.Location.IsInSource.Should().BeTrue(
            "HTTPCLIENT042 必须可导航到消费方源码");
        timeoutDiagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(4,
            "诊断应指向 [HttpClientApi(Timeout = ...)] 特性行（接口声明的 Span 起点），而不是 Location.None");
    }

    /// <summary>
    /// P2-3 回归：无 DI 工厂路径（<c>RestService.RegisterGeneratedFactory</c>）必须带默认异常脱敏，
    /// 显式注入优先（<c>options?.ExceptionRedactor ?? 默认实现</c>）。
    /// DI 路径由 <c>AddMudHttpClient</c> 的 <c>TryAddSingleton&lt;IExceptionRedactor&gt;</c> 兜底，
    /// 工厂路径此前传 <c>null</c> ⇒ 两条路径安全能力不对等。
    /// </summary>
    [Fact]
    public void CFG03_NonDiFactory_PassesDefaultExceptionRedactor()
    {
        var source = """
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IRedactorProbeApi
                {
                    [Get("/probe")]
                    System.Threading.Tasks.Task<string> GetProbeAsync();
                }
            }
            """;

        // 工厂注册代码在独立的 generated 文件里，须拼接全部生成树后再断言。
        var generated = RunRegistrationGeneratorAllTrees(source);

        generated.Should().NotBeNullOrEmpty();
        generated.Should().Contain(
            "exceptionRedactor: options?.ExceptionRedactor ?? new global::Mud.HttpUtils.DefaultSensitiveFieldExceptionRedactor(),",
            "无 DI 工厂路径必须默认脱敏，且显式注入优先（P2-3）");
    }

    /// <summary>P1-3 回归：负值 Timeout 同路径（用户表达「禁用超时」意图的自然写法）。</summary>
    [Fact]
    public void CFG03_HttpClientApiWithNegativeTimeout_SkipsAssignmentAndReportsHttpClient042()
    {
        var source = """
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi(Timeout = -1)]
                public interface ITimeoutNegativeApi
                {
                    [Get("/users")]
                    System.Threading.Tasks.Task<string> GetUsersAsync();
                }
            }
            """;

        var (generated, diagnostics) = RunRegistrationGeneratorWithDiagnostics(source);

        generated.Should().NotContain("client.Timeout",
            "负值 Timeout 不得生成 client.Timeout 赋值");
        diagnostics.Where(d => d.Id == "HTTPCLIENT042").Should().ContainSingle(
            "负值 Timeout 必须恰报 1 条 HTTPCLIENT042");
    }

    [Fact]
    public void CFG03_HttpClientApiWithoutTimeout_GeneratedConfig_Uses50()
    {
        var source = """
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITimeoutDefaultApi
                {
                    [Get("/users")]
                    System.Threading.Tasks.Task<string> GetUsersAsync();
                }
            }
            """;

        var generated = RunRegistrationGenerator(source);

        generated.Should().NotBeNullOrEmpty("带 [HttpClientApi] 的接口应生成注册代码");
        generated.Should().Contain(
            "global::System.TimeSpan.FromSeconds(50);",
            "未显式设置 Timeout 时必须使用文档契约默认值 50（CFG-03 / BC-1）");
        generated.Should().Contain(
            "HttpClientServiceCollectionExtensions.AddMudHttpClient(services,",
            "MT-14（BC-21）：注册必须经 AddMudHttpClient，使 keyed 注册与配置覆盖对生成客户端生效");
    }

    [Fact]
    public void CFG03_HttpClientApiWithExplicitTimeout_UsesExplicitValue()
    {
        var source = """
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi(Timeout = 77)]
                public interface ITimeoutExplicitApi
                {
                    [Get("/users")]
                    System.Threading.Tasks.Task<string> GetUsersAsync();
                }
            }
            """;

        var generated = RunRegistrationGenerator(source);

        generated.Should().NotBeNullOrEmpty();
        generated.Should().Contain(
            "client.Timeout = global::System.TimeSpan.FromSeconds(77);",
            "显式设置的 Timeout 必须原样传导至生成的命名 HttpClient");
        generated.Should().NotContain("TimeSpan.FromSeconds(50);",
            "显式设置时不应混入默认值");
    }
}
