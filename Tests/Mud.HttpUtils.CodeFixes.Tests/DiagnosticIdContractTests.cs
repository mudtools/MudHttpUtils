// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Reflection;
using Microsoft.CodeAnalysis.CodeFixes;
using Mud.HttpUtils.CodeFixes;

namespace Mud.HttpUtils.CodeFixes.Tests;

/// <summary>
/// 诊断 ID 契约守卫（生成器 ↔ 代码修复器）。
/// </summary>
/// <remarks>
/// <para>
/// 代码修复器与源生成器（含接口规范分析器）<b>必须</b>分属两个程序集：
/// 生成器是编译器扩展，不得引用 <c>Microsoft.CodeAnalysis.CSharp.Workspaces</c>（RS1038），
/// 而 <c>CodeFixProvider</c> 必须依赖 Workspaces。因此两侧无法通过程序集引用共享诊断描述符。
/// </para>
/// <para>
/// 二者以「共享源码 + 字符串 ID」约定契约：ID 常量集中在
/// <c>Mud.HttpUtils.Generator/DiagnosticIds.cs</c>，并由 CodeFixes 以源码链接方式复用。
/// 本测试把该契约固化为自动化守卫——生成器侧删除/改名诊断 ID 时，代码修复器会静默失配
/// （IDE 灯泡消失，且编译与既有测试均无提示），必须由测试拦截。
/// </para>
/// </remarks>
public class DiagnosticIdContractTests
{
    /// <summary>
    /// Generator 程序集。<c>Diagnostics</c> / <c>DiagnosticIds</c> 均为 internal，
    /// 仅对 <c>Mud.HttpUtils.Generator.Tests</c> 开放 InternalsVisibleTo，
    /// 故此处按名称加载后以反射读取。
    /// </summary>
    private static readonly Assembly GeneratorAssembly = Assembly.Load("Mud.HttpUtils.Generator");

    /// <summary>反射 Generator 程序集，收集 <c>Mud.HttpUtils.Diagnostics</c> 描述符的全部 ID。</summary>
    private static HashSet<string> ReadGeneratorDiagnosticIds()
    {
        var diagnosticsType = GeneratorAssembly.GetType("Mud.HttpUtils.Diagnostics")
            ?? throw new InvalidOperationException("无法找到 Mud.HttpUtils.Diagnostics 类型");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in diagnosticsType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(DiagnosticDescriptor)) continue;
            if (field.GetValue(null) is DiagnosticDescriptor descriptor)
                ids.Add(descriptor.Id);
        }

        return ids;
    }

    /// <summary>反射共享源码文件 <c>Mud.HttpUtils.DiagnosticIds</c> 的全部字符串常量。</summary>
    private static IReadOnlyList<(string Name, string Value)> ReadSharedDiagnosticIdConstants()
    {
        var idsType = GeneratorAssembly.GetType("Mud.HttpUtils.DiagnosticIds")
            ?? throw new InvalidOperationException(
                "无法找到 Mud.HttpUtils.DiagnosticIds 类型（共享源码 DiagnosticIds.cs 未参与编译？）");

        return idsType.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
            .ToList();
    }

    /// <summary>反射 CodeFixes 程序集，实例化全部 <see cref="CodeFixProvider"/> 并读取其可修复诊断 ID。</summary>
    private static IReadOnlyList<(string Provider, string DiagnosticId)> ReadCodeFixProviderDiagnosticIds()
    {
        var result = new List<(string, string)>();

        foreach (var type in typeof(AotXmlCodeFixProvider).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(CodeFixProvider).IsAssignableFrom(type)) continue;
            if (Activator.CreateInstance(type) is not CodeFixProvider provider) continue;

            foreach (var id in provider.FixableDiagnosticIds)
                result.Add((type.Name, id));
        }

        return result;
    }

    [Fact]
    public void CodeFixProviders_FixableDiagnosticIds_AllExistInGeneratorDiagnostics()
    {
        var generatorIds = ReadGeneratorDiagnosticIds();
        var codeFixIds = ReadCodeFixProviderDiagnosticIds();

        codeFixIds.Should().NotBeEmpty("应至少发现一个 CodeFixProvider");
        foreach (var (provider, id) in codeFixIds)
        {
            generatorIds.Should().Contain(id,
                $"{provider} 声明可修复 {id}，该 ID 必须在 Generator 的 Diagnostics 描述符中存在（否则 IDE 灯泡静默失效）");
        }
    }

    [Fact]
    public void SharedDiagnosticIdConstants_AllBackedByGeneratorDescriptors()
    {
        var generatorIds = ReadGeneratorDiagnosticIds();
        var constants = ReadSharedDiagnosticIdConstants();

        constants.Should().NotBeEmpty();
        foreach (var (name, value) in constants)
        {
            generatorIds.Should().Contain(value,
                $"共享常量 DiagnosticIds.{name} = \"{value}\" 必须在 Generator 的 Diagnostics 中有对应描述符");
        }
    }

    [Fact]
    public void CodeFixProviders_AllDeclaredInSharedIdConstants()
    {
        var sharedValues = ReadSharedDiagnosticIdConstants().Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        foreach (var (provider, id) in ReadCodeFixProviderDiagnosticIds())
        {
            sharedValues.Should().Contain(id,
                $"{provider} 硬编码了 {id}，应改用共享常量 DiagnosticIds.*，避免与生成器漂移");
        }
    }

    [Fact]
    public void CodeFixProviderCount_MatchesExpectedDistribution()
    {
        var providers = ReadCodeFixProviderDiagnosticIds()
            .Select(x => x.Provider)
            .Distinct()
            .ToList();

        providers.Count.Should().BeGreaterThanOrEqualTo(4,
            "当前随包分发 4 个 CodeFixProvider（AOT004/005/006、AOT007、HTTPCLIENT005、HTTPCLIENT007）");
    }
}
