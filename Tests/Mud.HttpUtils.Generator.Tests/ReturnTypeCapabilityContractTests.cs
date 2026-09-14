// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 返回类型能力口径守卫：对同一组类型样本断言
/// <b>「生成器是否发射占位实现」⟺「MUD002 是否报告」</b>互为充要条件。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要</b>：返回类型支持面有三处表达 —— 生成器门禁
/// （<c>ReturnTypeSupport.IsSupported</c> + <c>MethodGenerator.GenerateExecutorCall</c> 的分支）、
/// <c>MUD002</c> 分析器、README 诊断表。任何一处漂移都会产出"文档/分析器认为支持、生成器实际不支持"
/// （或反之）的漏网案例。
/// </para>
/// <para>
/// 历史缺陷正是本测试要封堵的两类：
/// <list type="bullet">
///   <item><c>IAsyncEnumerable&lt;T&gt;</c>：README 与 MUD002 都认为支持，生成器却因正则永不命中而产出
///         <c>CS4032</c> 不可编译代码（流式分支死代码）；</item>
///   <item>裸 <c>byte[]</c>/<c>Stream</c>/<c>HttpResponseMessage</c>：MUD002 旧白名单认为合法，
///         生成器却产出 <c>CS4032</c>；</item>
///   <item><c>Task&lt;Stream&gt;</c>：MUD002/README 认为支持，生成器产出"可编译但运行期必然失败"的
///         <c>ExecuteAsync&lt;System.IO.Stream&gt;</c>（把响应体当 JSON 反序列化进 <c>Stream</c>）。</item>
/// </list>
/// </para>
/// </remarks>
public class ReturnTypeCapabilityContractTests
{
    private const string Usings = """
        using System;
        using System.IO;
        using System.Collections.Generic;
        using System.Net.Http;
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        """;

    /// <summary>
    /// 构造「单个方法」的接口源码。<paramref name="declaration"/> 为方法签名（无方法体）。
    /// </summary>
    private static string BuildSource(string declaration)
        => Usings + $$"""

            namespace TestNamespace
            {
                public class Dto
                {
                    public string? Name { get; set; }
                }

                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/x")]
                    {{declaration}}
                }
            }
            """;

    /// <summary>取生成代码（跳过输入语法树）。</summary>
    private static string GetGeneratedCode(Compilation output)
        => string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

    private static (Compilation Output, bool EmittedPlaceholder) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "ReturnTypeCapabilityContractTests",
            new[] { CSharpSyntaxTree.ParseText(source) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var emittedPlaceholder = diagnostics.Any(d => d.Id == "HTTPCLIENT024");
        return (output, emittedPlaceholder);
    }

    /// <summary>
    /// 受支持返回类型样本：不得发射占位实现，且 MUD002 不得报告；生成代码必须可编译。
    /// </summary>
    [Theory]
    // 非泛型异步形态 → 非泛型 ExecuteAsync（void 内部返回类型）。
    [InlineData("Task RunAsync();", "ExecuteAsync(")]
    [InlineData("ValueTask RunAsync();", "ExecuteAsync(")]
    // 泛型异步形态 → ExecuteAsync<T> / 各专用分支。
    [InlineData("Task<string> GetAsync();", "ExecuteAsync<string>")]
    [InlineData("ValueTask<string> GetAsync();", "ExecuteAsync<string>")]
    [InlineData("Task<Dto> GetAsync();", "ExecuteAsync<TestNamespace.Dto>")]
    [InlineData("Task<byte[]> GetAsync();", "DownloadAsync(")]
    [InlineData("Task<Response<Dto>> GetAsync();", "ExecuteAsResponseAsync<TestNamespace.Dto>")]
    [InlineData("Task<HttpResponseMessage> GetAsync();", "SendRawAsync(")]
    // 流式返回 → await foreach + SendAsAsyncEnumerable。
    [InlineData("IAsyncEnumerable<Dto> StreamAsync();", "SendAsAsyncEnumerable<")]
    public void SupportedReturnType_NoPlaceholder_AndNoMud002(string declaration, string expectedCall)
    {
        var source = BuildSource(declaration);

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: $"返回类型受支持的方法：{declaration}");

        var generated = GetGeneratedCode(output);
        generated.Should().Contain(expectedCall, $"受支持的返回类型必须走对应生成分支：{declaration}");
        generated.Should().NotContain("NotSupportedException",
            "受支持的返回类型不得发射占位实现");

        var (_, emittedPlaceholder) = RunGenerator(source);
        emittedPlaceholder.Should().BeFalse("生成器不得为受支持的返回类型报告 HTTPCLIENT024");

        MudHttpInterfaceAnalyzerTests.AnalyzeForTest(source)
            .Should().NotContain(d => d.Id == "MUD002",
                "生成器支持的返回类型不得被 MUD002 误报（否则 Error 级误报会阻断可正常编译的代码）");
    }

    /// <summary>
    /// 裸 <c>Stream</c>：作为响应体 <c>T</c> 受支持（<c>Task&lt;Stream&gt;</c>），
    /// 但**裸返回**不受支持，必须生成直达调用 <c>SendStreamAsync</c>。
    /// </summary>
    [Fact]
    public void TaskOfStream_GeneratesDirectSendStreamAsync_NotJsonDeserialization()
    {
        var source = BuildSource("Task<Stream> GetStreamAsync();");

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "返回 Task<Stream> 的接口");

        var generated = GetGeneratedCode(output);
        generated.Should().Contain("SendStreamAsync",
            "Task<Stream> 必须走直达返回（响应流所有权归调用方），而不是把响应体 JSON 反序列化为 Stream");
        generated.Should().NotContain("ExecuteAsync<System.IO.Stream>",
            "修复前生成 ExecuteAsync<System.IO.Stream>：编译通过但运行期必然失败");
        generated.Should().NotContain("ExecuteAsync<global::System.IO.Stream>",
            "全限定名形态同样不得走 JSON 反序列化路径");
    }

    /// <summary>
    /// 不受支持返回类型样本：必须发射占位实现 + 报告 HTTPCLIENT024，且 MUD002 必须报告；生成代码仍可编译。
    /// </summary>
    [Theory]
    [InlineData("byte[] Get();")]
    [InlineData("Stream Get();")]
    [InlineData("HttpResponseMessage Get();")]
    [InlineData("Response<Dto> Get();")]
    [InlineData("string Get();")]
    [InlineData("void FireAndForget();")]
    [InlineData("Dto Get();")]
    public void UnsupportedReturnType_EmitsPlaceholder_AndReportsMud002(string declaration)
    {
        var source = BuildSource(declaration);

        // 占位实现必须仍然可编译（否则会把诊断换成难以定位的 CS0535/CS4032）。
        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: $"返回类型不受支持的方法：{declaration}");

        GetGeneratedCode(output).Should().Contain("NotSupportedException",
            "不受支持的返回类型必须发射抛异常的占位实现");

        var (_, emittedPlaceholder) = RunGenerator(source);
        emittedPlaceholder.Should().BeTrue("生成器必须为不受支持的返回类型报告 HTTPCLIENT024");

        MudHttpInterfaceAnalyzerTests.AnalyzeForTest(source)
            .Should().Contain(d => d.Id == "MUD002",
                "生成器不受支持的返回类型必须由 MUD002 说明根因（二者共用 ReturnTypeSupport 判定）");
    }
}
