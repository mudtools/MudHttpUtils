// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// M6-HC-02 / M6-HC-30 回归守卫：<c>[HeaderCollection]</c> 字典头接线与头值 CR/LF 校验。
/// </summary>
/// <remarks>
/// <para>
/// 历史缺陷（HC-02）：HeaderCollectionParameterBinder 此前全项目零调用点，
/// <c>[HeaderCollection]</c> 字典参数被静默忽略 —— 鉴权/租户头丢失且无任何编译期提示。
/// 接线修复后按优先级分派：参数带 [Header] → HeaderParameterBinder；否则带 [HeaderCollection] →
/// HeaderCollectionParameterBinder；两者均不匹配且参数名含 "header"（OrdinalIgnoreCase）→
/// 报告 HTTPCLIENT037（Info，低噪音拼写/遗漏提示）。
/// </para>
/// <para>
/// 快照输入源统一维护在 <see cref="SnapshotInputSources"/>（与编译断言测试共用，防止漂移）；
/// 快照只负责「形状」，可编译性由 <see cref="GeneratorSnapshotCompileTests"/> 批量断言，
/// 发射形态关键字（foreach / IsValid / Debug.WriteLine / Remove+Add）由本类
/// EndToEnd_* 用例逐条钉死。
/// </para>
/// </remarks>
public class HeaderCollectionBindingTests
{
    #region Verify 快照

    /// <summary>场景 1：单个 [HeaderCollection] 字典参数 —— foreach 逐项发射 + HC-30 CR/LF 校验。</summary>
    [Fact]
    public Task Snapshot_HeaderCollection_ShouldEmitForeachWithValidation()
    {
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(
            SnapshotInputSources.All.First(s => s.Name == nameof(Snapshot_HeaderCollection_ShouldEmitForeachWithValidation)).Source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>场景 2：[Header] 与 [HeaderCollection] 混用 —— 两者均发射且互不干扰。</summary>
    [Fact]
    public Task Snapshot_HeaderCollection_MixedWithHeader_ShouldEmitBoth()
    {
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(
            SnapshotInputSources.All.First(s => s.Name == nameof(Snapshot_HeaderCollection_MixedWithHeader_ShouldEmitBoth)).Source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    /// <summary>场景 3：<c>IDictionary&lt;string, object?&gt;</c> 版本 —— 值经 ToString 后发射。</summary>
    [Fact]
    public Task Snapshot_HeaderCollection_ObjectValues_ShouldUseToString()
    {
        var (driver, outputCompilation) = VerifyFixture.RunGeneratorDriver(
            SnapshotInputSources.All.First(s => s.Name == nameof(Snapshot_HeaderCollection_ObjectValues_ShouldUseToString)).Source);
        return VerifyFixture.VerifyGenerator(driver, outputCompilation);
    }

    #endregion

    #region 发射形态关键字（HC-30）

    private static string GetGeneratedCode(string source)
    {
        var output = GeneratorCompileAssert.RunAndAssertNoErrors(
            source,
            description: "[HeaderCollection] 端到端用例必须可编译");
        return string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
    }

    /// <summary>
    /// HC-30：HeaderCollection 每个字典项须先 IsNullOrWhiteSpace 检查，再对 Key/Value 调
    /// <c>HttpHeaderValueValidator.IsValid</c>（CR/LF），非法项经 Debug.WriteLine 跳过（不发射、不 Remove），
    /// 合法项才 Remove + Add。
    /// </summary>
    [Fact]
    public void EndToEnd_HeaderCollection_EmitsForeachWithCrLfValidation()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Post("/data")]
                    Task<string> PostAsync([HeaderCollection] IDictionary<string, string?> headers);
                }
            }
            """;

        var code = GetGeneratedCode(source);

        code.Should().Contain("foreach (var __headerKvp in headers)", "字典头须逐项发射");
        code.Should().Contain("!string.IsNullOrWhiteSpace(__headerKvp.Key)", "空白 Key 须跳过");
        code.Should().Contain("var __headerValue = __headerKvp.Value?.ToString();");
        code.Should().Contain("!string.IsNullOrWhiteSpace(__headerValue)", "空白 Value 须跳过");
        code.Should().Contain("global::Mud.HttpUtils.HttpHeaderValueValidator.IsValid(__headerKvp.Key)",
            "Key 须经 CR/LF 校验（HC-30）");
        code.Should().Contain("global::Mud.HttpUtils.HttpHeaderValueValidator.IsValid(__headerValue)",
            "Value 须经 CR/LF 校验（HC-30）");
        code.Should().Contain("\"[MudHttpUtils] HeaderCollection 项包含非法字符（CR/LF），已跳过: \" + __headerKvp.Key",
            "非法项走跳过（Debug 输出头名、不输出值），不得抛出");
        code.Should().Contain("__httpRequest.Headers.Remove(__headerKvp.Key);");
        code.Should().Contain("__httpRequest.Headers.Add(__headerKvp.Key, __headerValue);",
            "合法项才发射（Remove + Add）");
    }

    /// <summary>HC-30：[Header] 非 string 分支 —— ToString 结果存局部变量并经 IsValid 校验，非法跳过、合法才 Add。</summary>
    [Fact]
    public void EndToEnd_NonStringHeader_EmitsValueLocalWithCrLfValidation()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Post("/data")]
                    Task<string> PostAsync([Header("X-Attempt")] int attempt);
                }
            }
            """;

        var code = GetGeneratedCode(source);

        code.Should().Contain("var __headerValue_attempt = attempt.ToString();",
            "非 string 头值先求值到局部变量（HC-30）");
        code.Should().Contain("global::Mud.HttpUtils.HttpHeaderValueValidator.IsValid(__headerValue_attempt)",
            "局部变量须经 CR/LF 校验（HC-30）");
        code.Should().Contain("\"[MudHttpUtils] Header 值包含非法字符（CR/LF），已跳过: X-Attempt\"");
        code.Should().Contain("__httpRequest.Headers.Add(\"X-Attempt\", __headerValue_attempt);",
            "合法值才发射");
    }

    /// <summary>HC-30：接口级 [Header] 非 string 属性 —— 同样经局部变量 + IsValid 校验（可空类型包裹判空）。</summary>
    [Fact]
    public void EndToEnd_InterfaceHeaderPropertyNonString_EmitsValueLocalWithCrLfValidation()
    {
        var source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Header("X-Version")]
                    int Version { get; set; }

                    [Get("/data")]
                    Task<string> GetAsync();
                }
            }
            """;

        var code = GetGeneratedCode(source);

        code.Should().Contain("__ifaceHeaderValue_Version", "接口属性头值须存专用局部变量（HC-30）");
        code.Should().Contain("global::Mud.HttpUtils.HttpHeaderValueValidator.IsValid(__ifaceHeaderValue_Version)",
            "接口属性头值须经 CR/LF 校验（HC-30）");
    }

    #endregion

    #region 诊断 HTTPCLIENT037

    private static ImmutableArray<Diagnostic> RunGeneratorForDiagnostics(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BasicReferenceAssemblies.GetReferences();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new HttpInvokeClassSourceGenerator();
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        return driver.RunGenerators(compilation).GetRunResult().Diagnostics;
    }

    /// <summary>
    /// 参数名含 "header"（大小写不敏感）但无任何 Header 特性 → 报告 HTTPCLIENT037（Info）。
    /// 修复前该场景完全静默（参数既不发射也无提示）。
    /// </summary>
    [Fact]
    public void Diagnostics_ParameterNamedHeaderWithoutAttribute_ReportsHttpclient037AsInfo()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/data")]
                    Task<string> GetDataAsync(string userHeader);
                }
            }
            """;

        var diagnostics = RunGeneratorForDiagnostics(source);

        var hits = diagnostics.Where(d => d.Id == "HTTPCLIENT037").ToList();
        hits.Should().ContainSingle("名字疑似 HTTP 头却无特性的参数必须给出低噪音提示（M6-HC-02）");
        hits[0].Severity.Should().Be(DiagnosticSeverity.Info, "HTTPCLIENT037 定位为 Info（拼写/遗漏提示，不阻断）");
    }

    /// <summary>参数标注 [Header] → 不报告 HTTPCLIENT037。</summary>
    [Fact]
    public void Diagnostics_ParameterWithHeaderAttribute_DoesNotReportHttpclient037()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/data")]
                    Task<string> GetDataAsync([Header("X-User-Header")] string userHeader);
                }
            }
            """;

        var diagnostics = RunGeneratorForDiagnostics(source);

        diagnostics.Where(d => d.Id == "HTTPCLIENT037").Should().BeEmpty("已标注 [Header] 的参数不属遗漏");
    }

    /// <summary>参数标注 [HeaderCollection] → 不报告 HTTPCLIENT037。</summary>
    [Fact]
    public void Diagnostics_ParameterWithHeaderCollectionAttribute_DoesNotReportHttpclient037()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/data")]
                    Task<string> GetDataAsync([HeaderCollection] IDictionary<string, string?> userHeaders);
                }
            }
            """;

        var diagnostics = RunGeneratorForDiagnostics(source);

        diagnostics.Where(d => d.Id == "HTTPCLIENT037").Should().BeEmpty("已标注 [HeaderCollection] 的参数不属遗漏");
    }

    /// <summary>参数名不含 "header" 且无特性（如 userId）→ 不报告 HTTPCLIENT037（低噪音约束）。</summary>
    [Fact]
    public void Diagnostics_ParameterNameWithoutHeaderKeyword_DoesNotReportHttpclient037()
    {
        const string source = """
            using System.Threading.Tasks;
            using Mud.HttpUtils;
            using Mud.HttpUtils.Attributes;

            namespace TestNamespace
            {
                [HttpClientApi]
                public interface IApi
                {
                    [Get("/users/{userId}")]
                    Task<string> GetUserAsync([Path] int userId);
                }
            }
            """;

        var diagnostics = RunGeneratorForDiagnostics(source);

        diagnostics.Where(d => d.Id == "HTTPCLIENT037").Should().BeEmpty("名字不含 header 关键字的参数不触发提示");
    }

    #endregion
}
