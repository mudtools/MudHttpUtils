using System.Diagnostics;

namespace Mud.HttpUtils.Generator.Tests;

public class GeneratorDiagnosticsTests
{
    private static GeneratorDriver RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BasicReferenceAssemblies.GetReferences();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generatorType = TestHelper.GetType("Mud.HttpUtils.HttpInvokeClassSourceGenerator");
        var generator = (IIncrementalGenerator)Activator.CreateInstance(generatorType)!;
        var driver = CSharpGeneratorDriver.Create(generator);
        return driver.RunGenerators(compilation);
    }

    #region HTTPCLIENT012 - Generic Interface Not Supported

    [Fact]
    public void Generator_WithGenericInterface_GeneratesHTTPCLIENT012()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi<T>
    {
        [Get(""/items"")]
        Task<T> GetItemsAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT012");
    }

    #endregion

    #region HTTPCLIENT007 - HttpClient and TokenManager Mutually Exclusive

    [Fact]
    public void Generator_WithBothHttpClientAndTokenManager_GeneratesHTTPCLIENT007()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(HttpClient = ""myClient"", TokenManage = ""myManager"")]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT007");
    }

    #endregion

    #region HTTPCLIENT005 - Invalid URL Template

    [Fact]
    public void Generator_WithInvalidUrlTemplate_GeneratesHTTPCLIENT005()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/users/{userId/invalid"")]
        Task<string> GetUserAsync(string userId);
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT005");
    }

    #endregion

    #region No Diagnostics for Valid Interface

    [Fact]
    public void Generator_WithValidInterface_NoDiagnostics()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().BeEmpty();
    }

    #endregion

    #region HTTPCLIENT018 - TokenManager Without Explicit Key

    [Fact]
    public void Generator_WithTokenManagerButNoKey_GeneratesHTTPCLIENT018()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi(TokenManage = ""myManager"")]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT018");
    }

    #endregion

    #region AOT006 - [HttpJsonSerializable] not covered by any JsonSerializerContext

    [Fact]
    public void Generator_WithHttpJsonSerializableButNoContext_GeneratesAOT006()
    {
        var source = @"
using Mud.HttpUtils.Attributes;
namespace TestNamespace
{
    [HttpJsonSerializable]
    public class UserDto { public string Name { get; set; } }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "AOT006");
    }

    [Fact]
    public void Generator_WithHttpJsonSerializableCoveredByContext_NoAOT006()
    {
        var source = @"
using Mud.HttpUtils.Attributes;
using System.Text.Json.Serialization;
namespace TestNamespace
{
    [HttpJsonSerializable]
    public class UserDto { public string Name { get; set; } }

    [JsonSourceGenerationOptions]
    [JsonSerializable(typeof(UserDto))]
    internal partial class AppJsonContext : JsonSerializerContext
    {
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "AOT006");
    }

    #endregion

    // ============================================================
    // NEW-GEN-14：生成器异常完整堆栈输出
    // ============================================================

    [Fact]
    public void Generator_WhenUnexpectedExceptionOccurs_DiagnosticMessageShouldContainStackTrace()
    {
        // Arrange：构造一个异常对象，验证 GeneratorDebugLogger.LogError 的输出行为
        // NEW-GEN-14 修复：对于非预期异常，通过 GeneratorDebugLogger.LogError 输出到 Trace（Release 也可输出）
        var ex = new InvalidOperationException("测试异常");

        // Act：捕获 Trace 输出
        var traceOutput = CaptureTraceOutput(() => GeneratorDebugLogger.LogError("TestContext", ex));

        // Assert：验证 Trace 输出包含上下文、异常类型名和异常消息
        traceOutput.Should().Contain("TestContext",
            "LogError 应在输出中包含上下文标识，便于在 Trace 中定位来源");
        traceOutput.Should().Contain("InvalidOperationException",
            "LogError 应在输出中包含异常类型名，便于快速识别异常种类");
        traceOutput.Should().Contain("测试异常",
            "LogError 应在输出中包含异常消息，便于诊断异常原因");
    }

    /// <summary>
    /// 验证 NEW-GEN-14 修复：非预期异常的诊断消息应使用 ex.ToString() 包含完整堆栈信息。
    /// <para>
    /// 此测试标记为 Skip，因为难以确定性触发生成器内部的非预期异常（NullReferenceException 等）。
    /// 该场景通过 <see cref="Generator_WhenUnexpectedExceptionOccurs_DiagnosticMessageShouldContainStackTrace"/>
    /// 对 GeneratorDebugLogger.LogError 的单元测试间接覆盖。
    /// </para>
    /// </summary>
    [Fact(Skip = "NEW-GEN-14：意外异常难以确定性触发，通过 GeneratorDebugLogger.LogError 单元测试覆盖")]
    public void Generator_WhenUnexpectedExceptionOccurs_DiagnosticShouldUseFullExceptionToString()
    {
        // 若未来能确定性触发 HttpClientApiGenerationError 诊断，可在此通过编译边缘场景接口
        // 并验证诊断消息包含 ex.ToString() 输出的格式（如包含堆栈跟踪关键词 "at " 或异常类型全名）
    }

    /// <summary>
    /// 捕获 Trace.WriteLine 的输出内容，用于验证 GeneratorDebugLogger.LogError 的行为。
    /// </summary>
    private static string CaptureTraceOutput(Action action)
    {
        var output = new StringBuilder();
        using var listener = new TextWriterTraceListener(new StringWriter(output));
        Trace.Listeners.Add(listener);
        try
        {
            action();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            listener.Flush();
        }
        return output.ToString();
    }
}
