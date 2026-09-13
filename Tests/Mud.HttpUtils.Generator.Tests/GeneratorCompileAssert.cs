// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 生成代码编译断言工具（M0 基建）：跑生成器并断言「输入 + 生成产物」整体无 Error 级编译诊断。
/// <para>
/// 背景（审计 F15）：快照测试只负责「形状」，无法发现「生成代码在合法消费配置下不可编译」。
/// 本工具显式不注入 ImplicitUsings 的 global using（<see cref="CreateCompilation"/> 仅用最小显式引用），
/// 从而能捕捉 F2 类「缺 using」缺陷。
/// </para>
/// </summary>
internal static class GeneratorCompileAssert
{
    /// <summary>
    /// 跑生成器并断言「输入 + 生成产物」整体无 Error 级编译诊断。
    /// </summary>
    /// <param name="source">被测接口源代码。</param>
    /// <param name="extraReferences">额外元数据引用（默认 null）。</param>
    /// <param name="nullable">nullable 上下文；F1 触发条件与 nullable 上下文相关，需双向覆盖。</param>
    /// <param name="languageVersion">目标语言版本。</param>
    /// <param name="description">用例描述（用于失败消息）。</param>
    /// <returns>更新后的编译单元（含生成代码），供需要进一步断言的调用方使用。</returns>
    public static Compilation RunAndAssertNoErrors(
        string source,
        IEnumerable<MetadataReference>? extraReferences = null,
        NullableContextOptions nullable = NullableContextOptions.Disable,
        LanguageVersion languageVersion = LanguageVersion.Latest,
        string? description = null)
    {
        var generator = new HttpInvokeClassSourceGenerator();
        return RunAndAssertNoErrors(generator, source, extraReferences, nullable, languageVersion, description);
    }

    /// <summary>
    /// 跑指定生成器并断言「输入 + 生成产物」整体无 Error 级编译诊断。
    /// </summary>
    public static Compilation RunAndAssertNoErrors(
        IIncrementalGenerator generator,
        string source,
        IEnumerable<MetadataReference>? extraReferences = null,
        NullableContextOptions nullable = NullableContextOptions.Disable,
        LanguageVersion languageVersion = LanguageVersion.Latest,
        string? description = null)
    {
        var compilation = CreateCompilation(source, extraReferences, nullable, languageVersion);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        errors.Should().BeEmpty(
            $"{description ?? "生成代码"} 必须可编译；错误：{string.Join("\n", errors.Select(e => e.ToString()))}");

        return output;
    }

    /// <summary>
    /// 只加「最小显式引用」——刻意不注入 ImplicitUsings 的 global using，用于捕捉缺失 using。
    /// </summary>
    private static Compilation CreateCompilation(
        string source,
        IEnumerable<MetadataReference>? extraReferences,
        NullableContextOptions nullable,
        LanguageVersion languageVersion)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(languageVersion));
        return CSharpCompilation.Create(
            "GeneratorCompileAssert",
            new[] { tree },
            BasicReferenceAssemblies.GetReferences().Concat(extraReferences ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(nullable));
    }
}