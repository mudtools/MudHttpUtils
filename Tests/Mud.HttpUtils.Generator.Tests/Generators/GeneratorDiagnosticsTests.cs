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

    #region P3-7 - 无 DI 工厂路径 AppResilienceResolver/AppManager 接线

    [Fact]
    public void Registration_WiresAppResilienceResolverAndAppManager()
    {
        // P3-7：无 DI 工厂路径此前硬编码 appResilienceResolver: null / appManager: null，
        // 导致应用级弹性隔离与 UseApp/BeginScope 能力在 ForGenerated 下缺失。
        // 生成工厂必须从 options 透传这两个可选服务（appManager 在 executor 与实现类构造共两处）。
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
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeRegistrationGenerator();
        CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var generated = string.Join(
            "\n",
            outputCompilation.SyntaxTrees.Skip(1).Select(t => t.ToString()));

        generated.Should().Contain("appResilienceResolver: options?.AppResilienceResolver",
            "无 DI 工厂必须从 options 透传应用级弹性策略解析器");

        // 生成文本同时包含 #if NET5_0_OR_GREATER 的 [ModuleInitializer] 与 #else 的
        // RegisterAllFactories 两个注册路径（预处理指令保留在生成源码文本中），每条路径在
        // executor 与实现类构造各接线一次 ⇒ 单接口共 4 处 appManager 接线。
        var appManagerWires = generated.Split("appManager: options?.AppManager").Length - 1;
        appManagerWires.Should().Be(4, "ModuleInitializer 与 RegisterAllFactories 两条注册路径 × executor/实现类两处构造均需接线 AppManager");
    }

    #endregion

    #region P3-4 - REG002 诊断定位（分组名非法时携带接口 Location）

    /// <summary>
    /// P3-4：RegistryGroupName 非法时 HTTPCLIENTREG002 必须定位到接口声明（而非 Location.None）。
    /// <para>
    /// 可达路径为管线入口 ProcessInterface 的接口级校验（携带 interfaceSyntax.GetLocation()）；
    /// 分组注册方法内的防御性再校验（原以 Location.None 上报）经本次修复同样携带首个归属接口的
    /// Location——该分支在标准管线不可达（入口已过滤非法名），故经可达路径钉住「REG002 必带接口定位」契约。
    /// </para>
    /// </summary>
    [Fact]
    public void Registration_InvalidRegistryGroupName_LocatesAtInterfaceDeclaration()
    {
        var source = """
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi(RegistryGroupName = "1Bad-Group")]
                public interface IOrderApi
                {
                    [Get("/orders")]
                    System.Threading.Tasks.Task<string> GetOrdersAsync();
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeRegistrationGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);

        var reg002 = driver.GetRunResult().Diagnostics
            .Where(d => d.Id == "HTTPCLIENTREG002").ToList();

        reg002.Should().ContainSingle("非法 RegistryGroupName 必须上报 HTTPCLIENTREG002");
        var location = reg002.Single().Location;
        location.IsInSource.Should().BeTrue("REG002 应携带接口声明的源码定位，而非 Location.None");
        location.SourceTree!.GetText().ToString(location.SourceSpan)
            .Should().Contain("IOrderApi", "REG002 应定位到问题接口声明，便于就地修复");
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

    #region M6-HC-29 - 流式方法 AOT JsonTypeInfo 缺失提示（MUDGEN301）

    private const string StreamingSource = @"
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

    [Fact]
    public void StreamingAsyncEnumerable_ReportsMUDGEN301()
    {
        var driver = RunGenerator(StreamingSource);
        var diagnostics = driver.GetRunResult().Diagnostics;

        var diagnostic = diagnostics.Should().ContainSingle(d => d.Id == "MUDGEN301",
            "IAsyncEnumerable 流式方法的生成调用恒传 null JsonTypeInfo，AOT 下元素类型未入 Context 会静默反序列化为 default").Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning, "D5-A 决策：静默失败模式必须默认可见，但不阻断构建");
        diagnostic.GetMessage().Should().Contain("StreamAsync").And.Contain("string");

        // 报告点必须精确落在方法声明上：否则使用方无法就地以 #pragma warning disable MUDGEN301 抑制
        // （pragma 的生效范围按位置判定）。
        var span = diagnostic.Location.SourceSpan;
        diagnostic.Location.SourceTree!.GetText().ToString(span).Should().Contain("StreamAsync",
            "报告点须定位到方法声明（含方法名），pragma 抑制才能按位置生效");
    }

    [Fact]
    public void NonStreamingMethod_DoesNotReportMUDGEN301()
    {
        var source = @"
using System.Threading.Tasks;
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;

namespace TestNamespace
{
    [HttpClientApi]
    public interface ITestApi
    {
        [Get(""/items"")]
        Task<string> GetAsync();
    }
}";

        var driver = RunGenerator(source);

        driver.GetRunResult().Diagnostics.Should().NotContain(d => d.Id == "MUDGEN301",
            "非流式返回类型经执行器注入序列化器（options 含消费方 Context resolver），不存在该提示");
    }

    /// <summary>
    /// MUDGEN301 必须可抑制：不加 <c>NotConfigurable</c> 且默认启用
    /// —— 使用方确认元素类型已入 Context（或仅 JIT 部署）时需能 <c>#pragma warning disable MUDGEN301</c> / <c>NoWarn</c>。
    /// </summary>
    [Fact]
    public void MudGen301_IsSuppressible()
    {
        var descriptor = typeof(Diagnostics).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .Single(d => d.Id == "MUDGEN301");

        descriptor.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);
        descriptor.IsEnabledByDefault.Should().BeTrue();
        descriptor.CustomTags.Should().NotContain(WellKnownDiagnosticTags.NotConfigurable,
            "NotConfigurable 会令 #pragma / NoWarn 抑制失效");
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
    /// [P3-8] 嵌套接口 hintName 跨链元数碰撞守卫。
    /// <para>
    /// <c>class A&lt;T&gt;{class B{interface IFoo}}</c> 与 <c>class A{class B&lt;T&gt;{interface IFoo}}</c>
    /// 同命名空间共存时，仅用名称的包含链 parts 均为 ["A","B"] ⇒ 旧实现 hintName 同为 "A+B_Foo"
    /// ⇒ CS8785 产物覆盖。修复后包含类型名纳入各自泛型元数（元数据名风格），
    /// 二者分别为 "A`1+B_Foo" 与 "A+B`1_Foo"。
    /// </para>
    /// <para>
    /// 注：两接口的生成实现类名同为 Foo（{ns}.Generated 平铺命名空间）⇒ CS0101 类名重复。
    /// 此为比元数更宽的存量问题（同一命名空间下任意不同包含类型中的同名嵌套接口均碰撞，与元数无关，
    /// GEN-17 场景亦然），不在本项（hintName 唯一化）修复范围；故不做全量编译零错断言，
    /// 以 hintName 双份完整且可区分为本项守卫目标。
    /// </para>
    /// </summary>
    [Fact]
    public void NestedInterface_CrossChainArityCollision()
    {
        var source = @"
using Mud.HttpUtils;
using Mud.HttpUtils.Attributes;
using System.Threading.Tasks;

namespace TestNamespace
{
    // 链一：泛型包含类型 A<T>，非泛型 B
    public class A<T>
    {
        public class B
        {
            [HttpClientApi]
            public interface IFoo { [Get(""/x"")] Task<string> X(); }
        }
    }

    // 链二：非泛型 A，泛型包含类型 B<T>——与链一同名共存即构成跨链碰撞
    public class A
    {
        public class B<T>
        {
            [HttpClientApi]
            public interface IFoo { [Get(""/y"")] Task<string> Y(); }
        }
    }
}";

        var driver = RunGenerator(source);
        var diagnostics = driver.GetRunResult().Diagnostics;

        diagnostics.Should().NotContain(d => d.Id == "CS8785",
            "跨链同名包含类型不得令 hintName 冲突（否则 CS8785 全量产物消失）");

        var hintNames = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources.Select(s => s.HintName))
            .ToArray();

        hintNames.Should().Contain(h => h.Contains("A`1+B_Foo", StringComparison.Ordinal),
            "链一（A<T>{B{IFoo}}）应产出带 A`1+B_Foo 的 hintName（包含类型元数入名）");
        hintNames.Should().Contain(h => h.Contains("A+B`1_Foo", StringComparison.Ordinal),
            "链二（A{B<T>{IFoo}}）应产出带 A+B`1_Foo 的 hintName，与链一区分");
        hintNames.Count(h => h.Contains("Foo", StringComparison.Ordinal)).Should().Be(2,
            "两条链的 IFoo 必须各自生成一次，不得其一被覆盖");
    }

    #region HTTPCLIENT038 - 接口符号解析失败兜底提示（P2-5）

    /// <summary>
    /// P2-5：HTTPCLIENT038 发射契约。
    /// <para>
    /// 触发分支（<c>model.Symbol is not INamedTypeSymbol</c>）在标准 FAWM 管线下不可达：
    /// FAWM 对 GetDeclaredSymbol 为 null 的声明在 transform 前即跳过，且驱动级实测缺接口名、
    /// 缺右括号等畸形声明均得到 Roslyn 合成的错误符号（如缺名接口生成 NullOrEmptyInterfaceName）。
    /// 又因 GeneratorAttributeSyntaxContext 密封、SourceProductionContext 不可外部构造，
    /// 无法注入 null-Symbol 输入跑驱动级测试——故按报告验证方式「单测构造 model.Symbol == null
    /// 输入」的单元级等价物，直接断言发射点纯函数的 ID / 级别 / 消息与定位。
    /// </para>
    /// </summary>
    [Fact]
    public void InterfaceSymbolUnresolved_ReportsInfoDiagnosticWithInterfaceName()
    {
        var decl = CSharpSyntaxTree.ParseText("""
            using Mud.HttpUtils.Attributes;
            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/ping")]
                    Task<string> PingAsync();
                }
            }
            """).GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single();

        var diagnostic = HttpInvokeClassSourceGenerator.InterfaceSymbolUnresolvedDiagnostic(decl);

        diagnostic.Id.Should().Be("HTTPCLIENT038");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Info,
            "语法不完整属瞬时状态，自愈后提示自动消失，不应阻断构建");
        diagnostic.GetMessage().Should().Contain("ITestApi",
            "报告修复方向要求消息含接口名");
        diagnostic.GetMessage().Should().Contain("无法解析",
            "报告修复方向要求消息说明根因（符号解析失败）");
        diagnostic.Location.SourceTree.Should().NotBeNull();
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan)
            .Should().Contain("interface ITestApi",
                "报告点须定位到接口声明，便于用户就地定位语法缺口");
    }

    #endregion

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
