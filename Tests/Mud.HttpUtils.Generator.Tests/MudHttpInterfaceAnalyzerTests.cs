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
/// F9：MUD001/MUD002 与生成器能力对齐的测试。
/// <para>
/// MUD001 豁免 [IgnoreGenerator]（接口级跳过全部、方法级跳过单个）；
/// MUD002 支持生成器实际支持的返回类型（IAsyncEnumerable/HttpResponseMessage/byte[]/Stream）。
/// </para>
/// </summary>
public class MudHttpInterfaceAnalyzerTests
{
    private const string HttpClientApiUsings = """
        using System;
        using System.Threading.Tasks;
        using System.Collections.Generic;
        using Mud.HttpUtils.Attributes;

        """;

    private static ImmutableArray<Diagnostic> Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "MudHttpInterfaceAnalyzerTests",
            new[] { tree },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analysis = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MudHttpInterfaceAnalyzer()));
        return analysis.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private const string BaseInterfaceBody = """
            [Get("/users")]
            Task<string> GetUsersAsync();
        """;

    [Fact]
    public void IgnoreGeneratorMethod_NoMUD001()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [IgnoreGenerator]
                    Task<string> ManualMethod();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().NotContain(d => d.Id == "MUD001");
    }

    [Fact]
    public void IgnoreGeneratorInterface_NoMUD001Or002()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                [IgnoreGenerator]
                public interface IApi
                {
                    Task<string> ManualMethod();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().NotContain(d => d.Id == "MUD001");
        diagnostics.Should().NotContain(d => d.Id == "MUD002");
    }

    [Fact]
    public void MissingHttpMethodAttribute_ReportsMUD001()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    Task<string> MissingAttribute();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().Contain(d => d.Id == "MUD001");
    }

    [Fact]
    public void AsyncEnumerableReturn_NoMUD002()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/stream")]
                    IAsyncEnumerable<string> StreamAsync();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().NotContain(d => d.Id == "MUD002");
    }

    [Fact]
    public void HttpResponseMessageReturn_NoMUD002()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/raw")]
                    System.Net.Http.HttpResponseMessage Raw();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().NotContain(d => d.Id == "MUD002");
    }

    [Fact]
    public void ByteArrayReturn_NoMUD002()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/bytes")]
                    Task<byte[]> GetBytesAsync();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().NotContain(d => d.Id == "MUD002");
    }

    [Fact]
    public void StreamReturn_NoMUD002()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/stream")]
                    Task<System.IO.Stream> GetStreamAsync();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().NotContain(d => d.Id == "MUD002");
    }

    [Fact]
    public void TaskOfList_NoMUD002()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/list")]
                    Task<List<string>> GetListAsync();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().NotContain(d => d.Id == "MUD002");
    }

    [Fact]
    public void UserDefinedStringReturnType_ReportsMUD002()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/custom")]
                    CustomResult GetCustom();
                }

                public class CustomResult { }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().Contain(d => d.Id == "MUD002",
            "自定义返回类型不在生成器支持白名单内，应报 MUD002");
    }

    [Fact]
    public void StringReturn_ReportsMUD002()
    {
        // string 不在生成器返回类型白名单（生成器要求 Task/ValueTask 等包装或直达类型）
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/s")]
                    string GetString();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().Contain(d => d.Id == "MUD002");
    }

    [Fact]
    public void HttpMethodAttributes_NoMUD001()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    [Post("/users")]
                    Task<string> PostUsersAsync();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().NotContain(d => d.Id == "MUD001");
    }

    /// <summary>
    /// 自定义 HTTP 方法特性（继承 <c>Mud.HttpUtils.Attributes.HttpMethodAttribute</c>）不受支持 → 报告 MUD001。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 依据：生成器由「特性名」推导 HTTP 动词并发射 <c>HttpMethod.&lt;Verb&gt;</c>，
    /// 自定义特性名无法映射到 <c>System.Net.Http.HttpMethod</c> 的合法成员（生成代码会 CS0117），
    /// 因此该写法本就不受生成器支持。
    /// </para>
    /// <para>
    /// 由此 MUD001 与生成器门控口径一致（<c>MethodGenerator</c> 仅接受
    /// <see cref="HttpClientGeneratorConstants.SupportedHttpMethods"/> 中的特性名）：
    /// 给出明确的 MUD001，优于生成一段运行期才抛 <c>NotSupportedException</c> 的占位实现而无任何提示。
    /// </para>
    /// </remarks>
    [Fact]
    public void CustomHttpMethodAttribute_NotSupported_ReportsMUD001()
    {
        var source = HttpClientApiUsings + """
            namespace TestNamespace
            {
                public sealed class PurgeAttribute : HttpMethodAttribute
                {
                    public PurgeAttribute(string requestUri) : base("PURGE", requestUri) { }
                }

                [HttpClientApi]
                public interface IApi
                {
                    [Purge("/cache")]
                    Task<string> PurgeAsync();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().Contain(d => d.Id == "MUD001",
            "生成器不支持继承 HttpMethodAttribute 的自定义特性（HTTP 动词由特性名推导），应报告 MUD001");
    }
}