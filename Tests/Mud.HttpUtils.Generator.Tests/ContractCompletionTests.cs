// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 接口契约补全回归测试：生成器无法实现的接口成员必须发射占位实现，使实现类始终满足接口契约。
/// </summary>
/// <remarks>
/// <para>
/// 背景（缺陷修复）：此前对无法生成调用实现的方法、以及无 <c>[Query]</c>/<c>[Path]</c>/<c>[Header]</c>
/// 的接口属性/事件<b>直接跳过</b>（不发射成员），生成类因而缺失接口成员 → 编译报 <c>CS0535</c>。
/// 该错误不说明根因，且会掩盖真正的诊断（典型：MUD001「缺少 HTTP 方法特性」——修复前完全不可见）。
/// </para>
/// <para>
/// 修复方式：由 <c>MethodGenerator</c>（方法）与 <c>InterfaceContractCompletionGenerator</c>
/// （属性/索引器/事件）发射「抛 <see cref="NotSupportedException"/>」的占位成员。
/// </para>
/// <para>
/// 安全约束：占位成员运行期会抛异常，故<b>必须</b>有编译期诊断陪跑（HTTPCLIENT024，Error），
/// 且<b>每次发射占位都报告</b> —— 不能依赖「该成员上的其它诊断」兜底，
/// 因为其中的分析器诊断（MUD001/MUD002）在生成器报出「Error + NotConfigurable」诊断时会整体消失
/// （实测：HTTPCLIENT004/HTTPCLIENT005 可复现；去掉 NotConfigurable 标签或降为 Warning 立即恢复）。
/// </para>
/// <para>
/// 级别为 Error：修复前这些情形表现为 CS0535（编译失败），若降级为 Warning 则构建转为成功，
/// 等于把编译期失败改成运行期故障。
/// </para>
/// </remarks>
public class ContractCompletionTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;
        """;

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "ContractCompletionTests",
            new[] { CSharpSyntaxTree.ParseText(source) },
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (output, diagnostics);
    }

    private static string GetGeneratedCode(Compilation output)
        => string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));

    /// <summary>
    /// 缺少 HTTP 方法特性的方法：修复前生成类缺失该成员 → CS0535，且 MUD001 完全不可见。
    /// 断言占位成员已发射且可编译；同时断言 HTTPCLIENT024 已报告
    /// （根因诊断 MUD001 属分析器诊断，可能因 NotConfigurable 规律整体消失，故不能作为可见性兜底）。
    /// </summary>
    [Fact]
    public void MethodWithoutHttpMethodAttribute_EmitsPlaceholder_AndReportsDiagnostic()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    Task<string> NoHttpMethodAttributeAsync();
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含缺少 HTTP 方法特性方法的接口");

        var generated = GetGeneratedCode(output);
        generated.Should().Contain("NoHttpMethodAttributeAsync", "占位成员必须保留方法名以补齐接口契约");
        generated.Should().Contain("NotSupportedException", "占位成员应以明确异常快速失败");

        var (_, diagnostics) = RunGenerator(source);
        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT024",
            "占位实现必须编译期可见（不能依赖可能被抑制的分析器诊断）");
    }

    /// <summary>
    /// 返回类型不受支持（裸 <c>string</c>）：修复前生成器会产出「非 async 方法体内含 await」的
    /// 不可编译代码（实测 CS4032）。现应改为发射占位实现，并由 MUD002（分析器，与生成器共用
    /// <c>ReturnTypeSupport.IsSupported</c> 判定）说明根因。
    /// </summary>
    [Fact]
    public void UnsupportedReturnType_EmitsPlaceholder_InsteadOfUncompilableBody()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/bad")]
                    string GetString();
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "返回类型为裸 string 的接口");

        var generated = GetGeneratedCode(output);
        generated.Should().Contain("GetString", "占位成员必须保留方法名以补齐接口契约");
        generated.Should().Contain("NotSupportedException");
        generated.Should().NotContain("ExecuteAsync<string>",
            "不受支持的返回类型不得再生成含 await 的方法体（会产出 CS4032）");

        RunGenerator(source).Diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT024");

        MudHttpInterfaceAnalyzerTests.AnalyzeForTest(source)
            .Should().Contain(d => d.Id == "MUD002",
                "返回类型不受支持必须由 MUD002 说明根因（该诊断与生成器共用同一判定）");
    }

    /// <summary>
    /// <c>IAsyncEnumerable&lt;T&gt;</c> 返回：受支持（生成器有流式分支），必须生成 <c>async</c> 流式实现。
    /// </summary>
    /// <remarks>
    /// 回归：此前 <c>MethodAnalyzer</c> 用「<c>^IAsyncEnumerable&lt;…&gt;$</c>」正则匹配类型<b>限定名</b>
    /// （<c>System.Collections.Generic.IAsyncEnumerable&lt;T&gt;</c>），永不匹配 → 流式分支成为死代码，
    /// 生成结果退化为「非 async 方法体内含 await」，编译报 <c>CS4032</c>。
    /// 该缺陷与「返回类型不受支持」同族：分析器与文档都认为受支持，生成器却产出不可编译代码。
    /// </remarks>
    [Fact]
    public void AsyncEnumerableReturn_GeneratesStreamingImplementation_AndCompiles()
    {
        // GeneratorCompileAssert 刻意不注入 ImplicitUsings，故此处需显式 using System.Collections.Generic。
        var source = Usings + """

            using System.Collections.Generic;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/chat/stream")]
                    IAsyncEnumerable<string> StreamChatAsync();
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "返回 IAsyncEnumerable<string> 的接口");

        var generated = GetGeneratedCode(output);
        generated.Should().Contain("async", "流式方法体含 await foreach，必须声明为 async");
        generated.Should().Contain("await foreach");
        generated.Should().Contain("SendAsAsyncEnumerable<string>");

        RunGenerator(source).Diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT024",
            "IAsyncEnumerable<T> 属受支持返回类型，不得发射占位实现");
    }

    /// <summary>
    /// 指针参数方法：修复前既不发射成员（CS0535）也不发射可编译的占位（缺 <c>unsafe</c>）。
    /// 现应发射带 <c>unsafe</c> 修饰符的占位实现，使实现类满足接口契约。
    /// </summary>
    [Fact]
    public void PointerParameterMethod_EmitsUnsafePlaceholder()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/ptr")]
                    unsafe Task<string> PointerAsync(int* p);
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含指针参数方法的接口", allowUnsafe: true);

        GetGeneratedCode(output).Should().Contain("unsafe",
            "指针签名必须由 unsafe 占位成员满足，否则接口契约无法满足");
    }

    /// <summary>
    /// 接口 <c>static abstract</c> 成员：由实现类的<b>静态</b>成员满足，修复前会持续报 CS0535。
    /// </summary>
    [Fact]
    public void StaticAbstractMember_EmitsStaticPlaceholder()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    static abstract string StaticName { get; }
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含 static abstract 成员的接口");

        GetGeneratedCode(output).Should().Contain("StaticName");

        var (_, diagnostics) = RunGenerator(source);
        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT024",
            "static abstract 成员无其它诊断覆盖，必须由生成器报告");
    }

    /// <summary>
    /// <c>ref</c> 返回属性：<c>throw</c> 表达式不能作为 ref 返回值，修复前被直接跳过（CS0535）。
    /// 现应发射语句体访问器的占位实现。
    /// </summary>
    [Fact]
    public void RefReturnProperty_EmitsRefPlaceholder()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    ref int RefState { get; }
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含 ref 返回属性的接口");

        GetGeneratedCode(output).Should().Contain("RefState");

        RunGenerator(source).Diagnostics.Should()
            .Contain(d => d.Id == "HTTPCLIENT024" && d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// 使用方已在 partial 实现类中手写某成员时（占位实现落地前的可用写法），生成器必须让路，
    /// 否则占位成员与其手写实现构成重复定义（CS0111）—— 即把原本可编译的既有代码变成编译失败。
    /// </summary>
    [Fact]
    public void UserImplementedMember_GeneratorGivesWay()
    {
        // 生成类固定为 <接口命名空间>.Internal.<接口名去 I 前缀>，且可访问性为 internal。
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    Task<string> ManualAsync();
                }
            }

            namespace TestNamespace.Internal
            {
                internal partial class TestApi
                {
                    public Task<string> ManualAsync() => Task.FromResult("manual");
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "使用方手写 partial 实现的接口");

        GetGeneratedCode(output).Should().NotContain("ManualAsync",
            "使用方已手写实现，生成器不得再发射同名占位成员（否则 CS0111）");
    }

    /// <summary>
    /// 无 [Query]/[Path]/[Header] 的接口属性：修复前生成类缺失该属性 → CS0535。
    /// 该场景没有其它诊断覆盖，故必须由生成器报告 HTTPCLIENT024（Error，否则占位成员静默降级为运行期异常）。
    /// </summary>
    [Fact]
    public void UnattributedInterfaceProperty_EmitsPlaceholder_AndReportsDiagnostic()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    string UnsupportedState { get; set; }
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含无条件化特性属性的接口");

        var generated = GetGeneratedCode(output);
        generated.Should().Contain("UnsupportedState");
        generated.Should().Contain("NotSupportedException");

        var (_, diagnostics) = RunGenerator(source);
        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT024" && d.Severity == DiagnosticSeverity.Error,
            "不支持的接口属性必须由生成器报告诊断，避免占位成员静默降级为运行期故障");
    }

    /// <summary>
    /// 接口事件：生成器不支持，修复前会因缺失成员产生 CS0535。
    /// </summary>
    [Fact]
    public void InterfaceEvent_EmitsPlaceholder_AndReportsDiagnostic()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    event EventHandler? Changed;
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含事件的接口");

        GetGeneratedCode(output).Should().Contain("Changed");

        var (_, diagnostics) = RunGenerator(source);
        diagnostics.Should().Contain(d => d.Id == "HTTPCLIENT024");
    }

    /// <summary>
    /// 契约补全必须避让生成器「按模式无条件发射」的成员：
    /// 默认（AppContext）模式已发射 <c>Current</c>，若补全再发射同名成员会重复定义（CS0102）。
    /// </summary>
    [Fact]
    public void DeclaredMemberProvidedByInfrastructure_IsNotDuplicated()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    IMudAppContext? Current { get; set; }
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "接口声明了由 AppContext 基础设施提供的 Current 属性");
    }

    /// <summary>
    /// 方法级 [IgnoreGenerator] 语义仍为「完全不发射该成员」（由使用方自行实现），
    /// 不得因契约补全而被发射占位成员（否则会与使用方实现冲突）。
    /// </summary>
    [Fact]
    public void IgnoreGeneratorMethod_NotEmittedByContractCompletion()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/users")]
                    Task<string> GetUsersAsync();

                    [IgnoreGenerator]
                    Task<string> ManualAsync();
                }
            }
            """;

        var (output, _) = RunGenerator(source);

        GetGeneratedCode(output).Should().NotContain("ManualAsync",
            "[IgnoreGenerator] 方法必须仍由使用方实现，契约补全不得发射占位成员");
    }

    /// <summary>
    /// 占位成员抛出的异常消息必须携带诊断 ID（<c>HTTPCLIENT024</c>）。
    /// </summary>
    /// <remarks>
    /// 占位成员只在<b>运行期被调用</b>时才暴露，线上日志若只有一句「未生成实现」，
    /// 无法判断是"缺 HTTP 方法特性"还是"返回类型不受支持"，也无法关联规则与文档。
    /// 方法占位与属性/事件占位（<c>InterfaceContractCompletionGenerator</c>）两条路径都要带 ID。
    /// </remarks>
    [Fact]
    public void PlaceholderExceptionMessage_ContainsDiagnosticId()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/bad")]
                    string GetString();

                    string UnsupportedState { get; set; }
                }
            }
            """;

        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source, description: "含方法占位与属性占位的接口");

        var generated = GetGeneratedCode(output);
        generated.Should().Contain("HTTPCLIENT024",
            "占位实现的异常消息必须携带诊断 ID，便于线上日志关联规则与文档");
        generated.Split("HTTPCLIENT024").Length.Should().BeGreaterThanOrEqualTo(3,
            "方法占位与属性占位都必须携带诊断 ID（方法 1 处 + 属性 get/set 至少 2 处）");
    }

    /// <summary>
    /// 直达返回类型（<c>Stream</c> / <c>HttpResponseMessage</c>）与编排配置组合时，
    /// 必须报告 <c>HTTPCLIENT025</c>（Warning）—— 直达路径绕过执行器，编排不会生效。
    /// </summary>
    /// <remarks>
    /// 与既有 <c>HTTPCLIENT011</c>（<c>[Cache]</c> + <c>Response&lt;T&gt;</c>）同族：
    /// "配置静默失效"必须编译期可见。级别为 Warning（代码可编译且语义正确）。
    /// </remarks>
    [Fact]
    public void DirectReturnType_WithCache_ReportsOrchestrationWarning()
    {
        var source = Usings + """

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface ITestApi
                {
                    [Get("/stream")]
                    [Cache]
                    Task<System.IO.Stream> GetStreamAsync();

                    [Get("/raw")]
                    [Cache]
                    Task<System.Net.Http.HttpResponseMessage> GetRawAsync();

                    [Get("/plain")]
                    [Cache]
                    Task<string> GetPlainAsync();
                }
            }
            """;

        GeneratorCompileAssert.RunAndAssertNoErrors(source, description: "直达返回 + [Cache] 的接口");

        var (_, diagnostics) = RunGenerator(source);
        var warnings = diagnostics.Where(d => d.Id == "HTTPCLIENT025").ToList();

        warnings.Should().HaveCount(2,
            "Stream 与 HttpResponseMessage 两个直达返回方法各报告一次；普通 Task<string> 不得报告");
        warnings.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning,
            "直达返回的编排失效属「语义可用但配置未生效」，级别为 Warning");
    }
}
