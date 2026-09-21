// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-09（DP-6 方案 A）：空 <c>IAppManager&lt;IMudAppContext&gt;</c> 的失败必须自带根因与修复步骤。
/// </summary>
/// <remarks>
/// <para>
/// <b>背景</b>：源生成器为默认模式接口 <c>TryAddSingleton</c> 一个<b>空</b>的
/// <c>DefaultAppManager&lt;IMudAppContext&gt;</c>（未注册任何应用）。其 <c>LogWarning</c> 依赖
/// <c>ILoggerFactory</c>，而 <c>AddMudHttpClient</c> 不注册日志基础设施 ⇒ 裸 <c>ServiceCollection</c>
/// 宿主下<b>完全静默</b>；随后 <c>GetDefaultApp()</c> 抛出的原始消息只说「未设置默认应用」，不指向真因。
/// </para>
/// <para>
/// <b>修复形态</b>：生成工厂把 <c>GetDefaultApp()</c> 调用包在<b>单语句</b> try/catch 中，
/// 捕获 <c>InvalidOperationException</c> 后重抛带根因与修复步骤的异常（保留 <c>InnerException</c>）。
/// 单语句限定是必须的：<c>DefaultAppManager.GetApp()</c> 会先经 <c>AppKeyValidator</c> 抛
/// <c>ArgumentException</c>，try 块范围放宽会误吞并改变消息语义。
/// </para>
/// <para>
/// <b>为何用生成文本断言</b>：该消息存在于<b>消费方生成代码</b>中（工厂委托），
/// 而 <c>Client.Tests</c> 不含 <c>[HttpClientApi]</c> 接口（无生成产物），
/// 故此处以「生成文本 + 可编译」双断言固定契约（与 <c>MultiTfmCompilationGuardTests</c> 同口径）。
/// </para>
/// </remarks>
public class EmptyAppManagerMessageTests
{
    private const string Source = """
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface ITestApi
            {
                [Get("/data")]
                Task<string> GetDataAsync();
            }
        }
        """;

    private static Compilation RunBothGenerators(string source)
    {
        // DI 注册产物依赖 Microsoft.Extensions.DependencyInjection[.Extensions] 与 Microsoft.Extensions.Http
        // （BasicReferenceAssemblies 未覆盖），与 MultiTfmCompilationGuardTests 同口径按程序集名补引用。
        var references = BasicReferenceAssemblies.GetReferences();
        foreach (var assemblyName in new[]
                 {
                     "Microsoft.Extensions.DependencyInjection.Abstractions",
                     "Microsoft.Extensions.Http",
                 })
        {
            references.Add(MetadataReference.CreateFromFile(System.Reflection.Assembly.Load(assemblyName).Location));
        }

        var compilation = CSharpCompilation.Create(
            "EmptyAppManagerMessageTests",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
        [
            new HttpInvokeClassSourceGenerator().AsSourceGenerator(),
            new HttpInvokeRegistrationGenerator().AsSourceGenerator(),
        ]);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output;
    }

    [Fact]
    public void DefaultModeRegistration_ResolvesAppContext_WithActionableFailureMessage()
    {
        var output = RunBothGenerators(Source);

        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(
            $"生成产物必须可编译；错误：{string.Join("\n", output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))}");

        var registration = output.SyntaxTrees
            .Skip(1)
            .First(t => System.IO.Path.GetFileName(t.FilePath).Contains("HttpClientApiExtensions"))
            .ToString();

        // 解析必须经单语句 try/catch（而不是把 GetDefaultApp() 内联进构造函数调用）
        registration.Should().Contain("__appContext = appManager.GetDefaultApp();",
            "GetDefaultApp() 必须是可被 try 包住的独立语句（G8-09 方案 A）");
        registration.Should().Contain("catch (global::System.InvalidOperationException ex)",
            "只捕获 InvalidOperationException（GetDefaultApp 的唯一失败类型），不得放宽到 Exception");
        registration.Should().Contain("RegisterApp(appKey, context, isDefault: true)",
            "异常消息必须给出可执行的修复步骤（G8-09：错误信息自带根因与修复指引）");
        registration.Should().Contain("源生成器已自动注册空的 IAppManager<IMudAppContext>",
            "异常消息必须指明根因是「生成器自动注册的空管理器」");
        registration.Should().Contain("                        ex);",
            "重抛必须把原异常作为 InnerException 传入（保留原始「未设置默认应用」信息）");
    }

    /// <summary>
    /// 守卫范围：TokenManager / HttpClient 模式的接口走裸注册（无该工厂），
    /// 不得因 G8-09 的改动而被注入无关代码。
    /// </summary>
    [Fact]
    public void TokenManagerModeRegistration_DoesNotEmitAppContextResolution()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                public interface ITestTokenManager
                {
                    IMudAppContext GetDefaultApp();
                    IMudAppContext GetApp(string appKey);
                }

                [HttpClientApi(TokenManage = "ITestTokenManager")]
                public interface ITestApi
                {
                    [Get("/data")]
                    Task<string> GetDataAsync();
                }
            }
            """;

        var output = RunBothGenerators(source);

        var registration = output.SyntaxTrees
            .Skip(1)
            .First(t => System.IO.Path.GetFileName(t.FilePath).Contains("HttpClientApiExtensions"))
            .ToString();

        registration.Should().NotContain("__appContext = appManager.GetDefaultApp();",
            "TokenManager 模式由 DI 直接满足构造参数，不需要（也不得）注入默认应用解析与异常包装");
    }
}
