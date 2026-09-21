// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-06：<c>[FilePath(BufferSize = …)]</c> 的值域守护（下界归一 + 上界夹取 + HTTPCLIENT036）。
/// </summary>
/// <remarks>
/// <para>
/// BufferSize 最终用于 <c>new byte[bufferSize]</c>（进度回调路径）与
/// <c>new FileStream(…, bufferSize, …)</c> / <c>CopyToAsync(stream, bufferSize)</c>。
/// 特性 setter 不会被 Roslyn 实例化（Attribute 从不执行），运行期<b>没有</b>拦截点 ⇒
/// 生成器是唯一防线：<c>&lt;= 0</c> 归一为默认 81920（静默，保持 07 §G7-B 语义）、
/// <c>&gt; 4 MiB</c> 夹取到上界并报 <c>HTTPCLIENT036</c>（Warning，不阻断构建）。
/// </para>
/// </remarks>
public class FilePathBufferSizeTests
{
    private const int MaxSupported = 4 * 1024 * 1024;
    private const int Default = 81920;

    private static string BuildSource(string bufferSizeLiteral) => $$"""
        using System;
        using System.Threading.Tasks;
        using Mud.HttpUtils;
        using Mud.HttpUtils.Attributes;

        namespace TestNamespace
        {
            [HttpClientApi]
            public interface IFileApi
            {
                [Get("/files/{fileId}")]
                Task DownloadAsync([Path] string fileId, [FilePath(BufferSize = {{bufferSizeLiteral}})] string savePath);
            }
        }
        """;

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "FilePathBufferSizeTests",
            [CSharpSyntaxTree.ParseText(source)],
            BasicReferenceAssemblies.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new HttpInvokeClassSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (output, diagnostics);
    }

    /// <summary>超大值（1 GiB）：夹取到 4 MiB，并报 HTTPCLIENT036。</summary>
    [Fact]
    public void OutOfRange_ClampsToUpperBound_AndWarns()
    {
        var (output, diagnostics) = RunGenerator(BuildSource("1024 * 1024 * 1024"));

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generated.Should().Contain($", {MaxSupported},",
            "超大 BufferSize 必须被夹取到支持上界（否则运行期 new byte[bufferSize] 直接 OOM）");
        generated.Should().NotContain("1073741824", "原始超大值不得进入生成代码");

        diagnostics.Should().ContainSingle(d => d.Id == "HTTPCLIENT036",
            "夹取必须编译期可见（不静默改变用户配置）");
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(
            "夹取后语义安全，不应阻断构建（Warning 级）");
    }

    /// <summary>边界值（恰好等于上界）：不夹取、不告警。</summary>
    [Fact]
    public void AtUpperBound_NoClampNoWarning()
    {
        var (output, diagnostics) = RunGenerator(BuildSource("4194304"));

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generated.Should().Contain($", {MaxSupported},");
        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT036");
    }

    /// <summary>合法值：原样采用。</summary>
    [Fact]
    public void ValidValue_IsUsedAsIs()
    {
        var (output, diagnostics) = RunGenerator(BuildSource("65536"));

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generated.Should().Contain(", 65536,");
        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT036");
    }

    /// <summary>
    /// 非正值：归一为默认 81920，且**不**报 HTTPCLIENT036（保持 07 §G7-B 的既有归一语义）。
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void NonPositive_FallsBackToDefault_WithoutDiagnostic(string literal)
    {
        var (output, diagnostics) = RunGenerator(BuildSource(literal));

        var generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        generated.Should().Contain($", {Default},");
        diagnostics.Should().NotContain(d => d.Id == "HTTPCLIENT036",
            "下界归一属既有语义（已验证安全），不新增噪音");
    }
}
