// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// G8-12：<c>HttpClientGeneratorConstants</c> 中不得存在「死常量」（仅在定义处出现、无任何消费点）。
/// </summary>
/// <remarks>
/// <para>
/// <b>动机</b>：死常量比「缺失常量」更危险 —— 它让人以为某处判定由该常量驱动（例如
/// <c>DefaultTokenManageInterface = "ITokenManage"</c> 会让人以为默认令牌管理器接口名是 <c>ITokenManage</c>，
/// 而真实约定是 <c>ITokenManager</c>），从而误导后续维护并诱发静默失配。
/// </para>
/// <para>
/// <b>判据</b>：反射枚举常量类型的全部 <c>public static</c> 字段（含 <c>const</c> 与
/// <c>static readonly</c>），对每个字段名在<b>生成器源码</b>（排除定义文件与 obj/bin）中检索引用；
/// 零引用即失败（白名单为空 —— 任何新死常量都应删除或补上消费点）。
/// </para>
/// <para>
/// <b>注</b>：仅搜索<b>定义文件之外</b>的引用，故「为构造另一常量而在同文件内引用」不算消费点
/// （如 <c>SupportedHttpMethodsSet</c> 引用 <c>SupportedHttpMethods</c>）—— 这类组合常量若对外无引用，
/// 应连同其被组合项一并清理或补消费点。
/// </para>
/// </remarks>
public class ConstantUsageGuardTests
{
    private const string GeneratorRoot = @"../../../../../Mud.HttpUtils.Generator";
    private const string ConstantsFileName = "HttpClientGeneratorConstants.cs";

    [Fact]
    public void EveryConstant_IsReferencedOutsideItsDeclarationFile()
    {
        var constantsType = typeof(HttpInvokeClassSourceGenerator).Assembly
            .GetType("Mud.HttpUtils.HttpClientGeneratorConstants")
            ?? throw new InvalidOperationException("无法找到 Mud.HttpUtils.HttpClientGeneratorConstants 类型");

        var sourceFiles = EnumerateGeneratorSources().ToList();
        sourceFiles.Should().NotBeEmpty("守卫自身必须能枚举到生成器源码，否则扫描逻辑失效");

        var deadConstants = new List<string>();

        foreach (var field in constantsType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var referenced = sourceFiles
                .Where(f => !string.Equals(Path.GetFileName(f), ConstantsFileName, StringComparison.Ordinal))
                .Any(f => File.ReadAllText(f).Contains(field.Name, StringComparison.Ordinal));

            if (!referenced)
                deadConstants.Add($"{field.Name}（{field.FieldType.Name}）");
        }

        deadConstants.Should().BeEmpty(
            "G8-12：以下常量在全仓（除定义文件外）零引用，属死常量，应删除或补上消费点：" +
            string.Join("、", deadConstants));
    }

    /// <summary>生成器源码全部 *.cs 文件路径（排除 obj/bin）。</summary>
    private static IEnumerable<string> EnumerateGeneratorSources()
    {
        var root = Path.GetFullPath(GeneratorRoot);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return file;
        }
    }
}
