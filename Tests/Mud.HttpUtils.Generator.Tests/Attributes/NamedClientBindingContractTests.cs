// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G7-04a：命名 HttpClient 绑定脱节的契约测试。
/// <para>
/// 注册端生成 <c>AddMudHttpClient("{接口名}_HttpClient", ...)</c> 命名客户端，但实现类构造函数注入
/// 类型级 <c>IEnhancedHttpClient</c> / <c>IHttpRequestExecutor</c> —— 命名客户端仅在对应名称成为默认
/// <c>IEnhancedHttpClient</c>（TryAdd 先注册者胜）时才被实现类实际使用。
/// 本组用例钉死：① 生成注册源码含「类型级解析 + 命名客户端」说明注释；② 同一编译 ≥2 个
/// <c>[HttpClientApi]</c> 接口时报告 <c>HTTPCLIENT033</c>（Info，每编译一次）；单接口不报告（去噪口径）。
/// </para>
/// </summary>
public class NamedClientBindingContractTests
{
    private const string SingleApiSource = """
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(Timeout = 30)]
            public interface IUserApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
        }
        """;

    private const string MultiApiSource = """
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi(Timeout = 30)]
            public interface IUserApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }

            [HttpClientApi(Timeout = 60)]
            public interface ITenantApi
            {
                [Get("/tenants")]
                System.Threading.Tasks.Task<string> GetTenantsAsync();
            }
        }
        """;

    private static (string RegistrationSource, ImmutableArray<Diagnostic> Diagnostics) RunRegistrationGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeRegistrationGenerator();

        // 源码：RunGeneratorsAndUpdateCompilation 的输出编译可直接枚举生成树（已验证可取全量产物）。
        var driver1 = CSharpGeneratorDriver.Create(generator);
        driver1.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var registrationSource = string.Join(
            "\n",
            outputCompilation.SyntaxTrees
                .Skip(1)
                .Select(t => t.ToString()));

        // 诊断：与 GeneratorDiagnosticsTests 同口径 —— 使用 RunGenerators 返回的新 driver 的
        // GetRunResult().Diagnostics（原 driver 上取不到增量管线的运行结果）。
        var driver2 = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var diagnostics = driver2.GetRunResult().Diagnostics;
        return (registrationSource, diagnostics);
    }

    /// <summary>
    /// G7-04a：注册源码必须含「类型级 IEnhancedHttpClient 解析 + 命名客户端仅在该名称成为默认时生效」的说明注释。
    /// </summary>
    [Fact]
    public void Registration_NamedHttpClient_CommentOrDiagnostic_Present()
    {
        var (generated, diagnostics) = RunRegistrationGenerator(SingleApiSource);

        generated.Should().NotBeNullOrEmpty("带 [HttpClientApi] 的接口应生成注册代码");
        generated.Should().Contain(
            "HttpClientServiceCollectionExtensions.AddMudHttpClient(services,",
            "MT-14（BC-21）：注册必须经 AddMudHttpClient，使 keyed 注册与配置覆盖对生成客户端生效");
        generated.Should().Contain(
            "IUserApi_HttpClient",
            "命名客户端名应为 {接口名}_HttpClient（G7-04a）");
        generated.Should().Contain(
            "类型级 IEnhancedHttpClient",
            "G7-04a：注册源码必须显式说明实现类按类型级 IEnhancedHttpClient 解析，命名客户端不必然生效");

        // 单接口场景不报告 HTTPCLIENT033（避免单接口工程的构建噪音）
        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT033",
            "G7-04a 去噪口径：仅当同一编译 ≥2 个 [HttpClientApi] 接口时才报告 HTTPCLIENT033");
    }

    /// <summary>
    /// G7-04a：同一编译 ≥2 个 [HttpClientApi] 接口时，每编译至多报告一次 HTTPCLIENT033（Info）。
    /// </summary>
    [Fact]
    public void MultiInterface_ReportsHttpClient033_ExactlyOnce_AsInfo()
    {
        var (generated, diagnostics) = RunRegistrationGenerator(MultiApiSource);

        generated.Should().Contain("IUserApi_HttpClient");
        generated.Should().Contain("ITenantApi_HttpClient");

        var namedClientDiags = diagnostics.Where(d => d.Id == "HTTPCLIENT033").ToArray();
        namedClientDiags.Should().HaveCount(1,
            "G7-04a：HTTPCLIENT033 应每编译至多报告一次（全局注册点报告，与 G7-13 降噪口径一致）");
        namedClientDiags[0].Severity.Should().Be(DiagnosticSeverity.Info,
            "G7-04a：HTTPCLIENT033 为 Info（代码可编译，仅提示命名客户端配置可能未隔离）");
    }
}