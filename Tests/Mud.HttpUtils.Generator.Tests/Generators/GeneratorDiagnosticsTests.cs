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

    #region HTTPCLIENT020 - [Retry] on non-idempotent method without AllowNonIdempotent

    [Fact]
    public void Generator_WithRetryOnPostWithoutAllowNonIdempotent_GeneratesHTTPCLIENT020()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Post(""/orders"")]
        [Retry(3, 1000)]
        Task<string> CreateOrderAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT020");
        var diagnostic = diagnostics.First(d => d.Id == "HTTPCLIENT020");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_WithRetryOnPostWithAllowNonIdempotent_NoHTTPCLIENT020()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Post(""/orders"")]
        [Retry(3, 1000, AllowNonIdempotent = true)]
        Task<string> CreateOrderAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT020");
    }

    [Fact]
    public void Generator_WithRetryOnGetWithoutAllowNonIdempotent_NoHTTPCLIENT020()
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
        [Retry(3, 1000)]
        Task<string> GetDataAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT020");
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

    #region HTTPCLIENT022 - Path/HmacSignature token injection mode lacks recovery capability (P3.3 / TK-18)

    [Fact]
    public void Generator_WithPathInjectionMode_GeneratesHTTPCLIENT022()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = ""ITestTokenManager"")]
    [Token(TokenType = ""AccessToken"", InjectionMode = TokenInjectionMode.Path)]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        var diag = diagnostics.Should().ContainSingle(d => d.Id == "HTTPCLIENT022").Subject;
        diag.Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_WithHmacSignatureInjectionMode_GeneratesHTTPCLIENT022()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = ""ITestTokenManager"")]
    [Token(TokenType = ""AccessToken"", InjectionMode = TokenInjectionMode.HmacSignature)]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT022");
    }

    [Fact]
    public void Generator_WithHeaderInjectionMode_NoHTTPCLIENT022()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    public interface ITestTokenManager
    {
        IMudAppContext GetDefaultApp();
        IMudAppContext GetApp(string appKey);
    }

    [HttpClientApi(TokenManage = ""ITestTokenManager"")]
    [Token]
    public interface ITestApi
    {
        [Get(""/data"")]
        Task<string> GetDataAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT022");
    }

    #endregion

    #region AOT006 - [HttpJsonSerializable] not covered by any JsonSerializerContext

    // [F6] AOT006 已迁出生成管道，由独立 DiagnosticAnalyzer（HttpJsonSerializableCoverageAnalyzer）
    // 在编译分析阶段报告。此处保留「生成器不再产出 AOT006」的断言（防回归），
    // 其实际诊断逻辑与恰好 1 条的行为由 AotDtoCoverageAnalyzerTests.Analyzer_* 覆盖。

    [Fact]
    public void Generator_WithHttpJsonSerializableButNoContext_DoesNotReportAOT006FromGenerator()
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

        diagnostics.Should().NotContain(d => d.Id == "AOT006",
            "AOT006 已由独立分析器承载，生成器不应再报告（F6）");
    }

    [Fact]
    public void Generator_WithHttpJsonSerializableCoveredByContext_NoAOT006FromGenerator()
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

    // [本轮核验修复] 此处原有 `Generator_WhenUnexpectedExceptionOccurs_DiagnosticShouldUseFullExceptionToString`
    // 恒空的 Skip 用例，其断言的语义（诊断消息含完整 ex.ToString()）已在 [Phase4 修复 2.3 / §5.2] 中
    // **被反向修正**——非预期异常的诊断消息改为「类型名: 消息」，完整堆栈仅走 GeneratorDebugLogger.LogError
    // （堆栈含本机绝对路径，写入诊断会泄漏到 IDE 错误列表/CI 日志）。
    // 该用例既无法确定性触发（意外异常不可构造），其目标又与现行实现相反，已删除；
    // 现行契约由上方 LogError 用例 + HttpInvokeClassSourceGenerator 的注释固化。

    #region F7 - 注册代码命名空间安全化

    [Fact]
    public void Registration_InvalidAssemblyName_FallsBackToSafeNamespace()
    {
        // F7：AssemblyName 含 '-' 等非法标识符字符时，注册代码 namespace 必须回退到
        // Microsoft.Extensions.DependencyInjection，不得产出非法 C#。
        var source = """
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/users")]
                    System.Threading.Tasks.Task<string> GetUsersAsync();
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "my-lib",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeRegistrationGenerator();
        CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var generated = outputCompilation.SyntaxTrees.Skip(1).FirstOrDefault()?.ToString();

        generated.Should().NotBeNullOrEmpty();
        generated.Should().NotContain("namespace my-lib", "非法 AssemblyName 不得直接替换进 namespace");
        generated.Should().Contain("namespace Microsoft.Extensions.DependencyInjection",
            "非法 AssemblyName 应回退到安全的 DI 命名空间");
    }

    [Fact]
    public void Registration_GlobalNamespaceInterface_ReportsError()
    {
        // F7：全局命名空间接口无法生成合法 DI 注册代码（global::.IApi 不合法），必须报 HTTPCLIENTREG001。
        var source = """
            using Mud.HttpUtils.Attributes;

            [HttpClientApi]
            public interface IApi
            {
                [Get("/users")]
                System.Threading.Tasks.Task<string> GetUsersAsync();
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeRegistrationGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var diagnostics = driver.GetRunResult().Diagnostics;

        var regDiagnostics = driver.GetRunResult().Diagnostics.Where(d => d.Id == "HTTPCLIENTREG001").ToList();
        regDiagnostics.Should().ContainSingle();
        regDiagnostics.Single().GetMessage().IndexOf("全局命名空间", StringComparison.Ordinal).Should().BeGreaterThanOrEqualTo(0,
            "HTTPCLIENTREG001 消息应包含全局命名空间的迁移指引");
    }

    #endregion

    #region F14 - ref/out/params/指针参数校验

    [Theory]
    [InlineData("ref int id")]
    [InlineData("out string value")]
    [InlineData("in int pinned")]
    [InlineData("params string[] tags")]
    public void Generator_WithUnsupportedParameterModifier_ReportsHTTPCLIENT004(string parameter)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Post("/data")]
                    Task<string> PostAsync([Body] string body, {{parameter}});
                }
            }
            """;

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        var relevant = diagnostics.Where(d => d.Id == "HTTPCLIENT004").ToList();
        relevant.Should().ContainSingle("{{parameter}} 应被 HTTPCLIENT004 拒绝");
    }

    [Fact]
    public void Generator_WithPointerParameter_ReportsHTTPCLIENT004()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Post("/data")]
                    unsafe Task<string> PostAsync([Body] string body, int* ptr);
                }
            }
            """;

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        var relevant = diagnostics.Where(d => d.Id == "HTTPCLIENT004").ToList();
        relevant.Should().ContainSingle("指针参数应被 HTTPCLIENT004 拒绝");
    }

    [Fact]
    public void Generator_WithoutUnsupportedModifiers_NoHTTPCLIENT004()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users/{id}")]
                    Task<string> GetAsync([Path] int id);
                }
            }
            """;

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT004");
    }

    #endregion

    #region GEN-08 - 流式返回编排静默失效（HTTPCLIENT025）

    [Fact]
    public void StreamingWithCache_ReportsHTTPCLIENT025()
    {
        var source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/items"")]
        [Cache]
        IAsyncEnumerable<string> StreamAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        // IAsyncEnumerable<T> 直达返回（SendAsAsyncEnumerable 绕过执行器）+ [Cache] → HTTPCLIENT025
        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT025",
            "流式返回不走 Cache/Resilience 编排，[Cache] 会静默失效，必须编译期提示");
    }

    [Fact]
    public void StreamingWithRetry_ReportsHTTPCLIENT025()
    {
        var source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/items"")]
        [Retry]
        IAsyncEnumerable<string> StreamAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT025",
            "流式返回 + [Retry] 组合同样应报告 HTTPCLIENT025（重试不会生效）");
    }

    [Fact]
    public void StreamingWithoutResilience_NoDiagnostic()
    {
        var source = @"
using System.Collections.Generic;
using System.Threading.Tasks;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/items"")]
        IAsyncEnumerable<string> StreamAsync();
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT025",
            "无 [Cache]/[Retry]/[CircuitBreaker]/[Timeout] 时流式返回不应误报 HTTPCLIENT025");
    }

    #endregion

    /// <summary>
    /// [GEN-17][§8.5] 嵌套接口平铺后 hintName 唯一性碰撞守卫。
    /// <para>
    /// 构造文档所述两组歧义结构：
    /// 「A{class B_C{IFoo}}」（包含类型名为含下划线的 B_C）与
    /// 「A{class B{class C{IFoo}}}」（B 内含两层嵌套 C）。
    /// 旧实现用 "_" 连接嵌套链，两组结构平铺后均得到 "B_C_" 后缀 → hintName 冲突（CS8785/产物覆盖）。
    /// 修复后跨层使用 "+" 分隔符使二者分别得到 "B_C_Foo" 与 "B+C_Foo"（生成类名取接口名去 I 前缀）。
    /// </para>
    /// </summary>
    [Fact]
    public void NestedInterface_FlattenedNameCollision()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;
using System.Threading.Tasks;

namespace TestNamespace
{
    // 情形一：包含类型名为 B_C（含下划线）
    public class B_C
    {
        [HttpClientApi]
        public interface IFoo { [Get(""/x"")] Task<string> X(); }
    }

    // 情形二：包含类型链 B → C 两层嵌套
    public class B
    {
        public class C
        {
            [HttpClientApi]
            public interface IFoo { [Get(""/y"")] Task<string> Y(); }
        }
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "CS8785",
            "嵌套链平铺后 hintName 不得冲突（否则 CS8785 全量产物消失）");

        var hintNames = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources.Select(s => s.HintName))
            .ToArray();

        // 生成类名取接口名去 I 前缀后的「Foo」，前缀分别为 B_C_ 与 B+C_，
        // 关键不变量是二者必须不同（含下划线单层 vs 跨层 + 分隔），否则 hintName 撞名。
        hintNames.Should().Contain(h => h.Contains("B_C_Foo", StringComparison.Ordinal),
            "情形一（B_C 单层含下划线）应产出带 B_C_Foo 的 hintName（无跨层分隔符）");
        hintNames.Should().Contain(h => h.Contains("B+C_Foo", StringComparison.Ordinal),
            "情形二（B→C 两层嵌套）应产出带 B+C_Foo 的 hintName，与情形一区分");
        hintNames.Count(h => h.Contains("Foo", StringComparison.Ordinal)).Should().Be(2,
            "两个 IFoo 嵌套接口（B_C 内与 B→C 内）必须各自生成一次，不得其一被覆盖");
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
