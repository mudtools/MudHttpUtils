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
