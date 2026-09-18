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
/// MUD002 只接受生成器实际支持的<b>异步形态</b>返回类型
/// （Task/Task&lt;T&gt;/ValueTask/ValueTask&lt;T&gt;/IAsyncEnumerable&lt;T&gt;，与生成器共用
/// <c>ReturnTypeSupport.IsSupported</c>）—— 裸 byte[]/Stream/HttpResponseMessage/void 均不受支持。
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

    /// <summary>
    /// 供其它测试类复用本分析器（MUD001/MUD002）的语义分析入口。
    /// 典型用途：断言「生成器发射占位实现」的场景同时具备说明根因的分析器诊断。
    /// </summary>
    internal static ImmutableArray<Diagnostic> AnalyzeForTest(string source) => Analyze(source);

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
        var mud001 = diagnostics.Single(d => d.Id == "MUD001");

        // [GEN-20][§8.7] 定位应落在方法声明范围内，而非整个接口声明。
        mud001.Location.IsInSource.Should().BeTrue();
        var spanText = mud001.Location.SourceTree.GetText().ToString(mud001.Location.SourceSpan);
        spanText.Should().Contain("MissingAttribute",
            "MUD001 定位应落在方法声明上（GEN-20），span 文本须含方法名");
        spanText.Should().NotContain("interface IApi",
            "MUD001 定位不得回退到整个接口声明（GEN-20）");
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
                    Task<System.Net.Http.HttpResponseMessage> RawAsync();
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().NotContain(d => d.Id == "MUD002");
    }

    /// <summary>
    /// 回归：裸（未被 async 形态包裹）的返回类型此前被 MUD002 视为合法，但生成器对它们会产出
    /// 「非 async 方法体内含 await」的不可编译代码（实测 CS4032）——
    /// 属「分析器沉默 + 生成坏代码」的漏报方向，现必须报告 MUD002。
    /// </summary>
    [Theory]
    [InlineData("byte[] Bytes();")]
    [InlineData("System.Net.Http.HttpResponseMessage Raw();")]
    [InlineData("System.IO.Stream Stream();")]
    [InlineData("void FireAndForget();")]
    [InlineData("string GetString();")]
    public void BareNonAsyncReturnType_ReportsMUD002(string declaration)
    {
        var source = HttpClientApiUsings + $$"""
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/x")]
                    {{declaration}}
                }
            }
            """;

        var diagnostics = Analyze(source);
        diagnostics.Should().Contain(d => d.Id == "MUD002",
            "裸返回类型会产出不可编译的生成代码，必须报告 MUD002");
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