// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Generator.Tests;

/// <summary>
/// 诊断标签策略守卫：约束 <see cref="WellKnownDiagnosticTags.NotConfigurable"/> 的使用面。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要守卫</b>：实测 + Roslyn 源码定位（<c>CommonCompiler.CompileAndEmit</c>）确认，
/// 存在「默认级别 Error 且带 <c>NotConfigurable</c>」的诊断时，csc 在声明阶段闸门
/// （<c>HasUnsuppressableErrors</c> → <c>Diagnostic.IsUnsuppressableError()</c>）提前返回，
/// <b>本编译中的分析器驱动永不执行</b>，于是 <c>MUD001</c>/<c>MUD002</c>/<c>MUD004</c>
/// 整体不呈现。故"用户可修复"的诊断不得进入该集合
/// （见 <c>.docs/生成器诊断治理与返回类型完善方案v1.md</c> §3.3）。
/// </para>
/// <para>
/// 本测试即"标签分层决策"的机器可读记录：变更白名单必须显式修改本文件，形成评审摩擦。
/// </para>
/// </remarks>
public class DiagnosticTagPolicyTests
{
    /// <summary>「内部/环境类错误」白名单：允许（且仅允许）它们同时是 Error 且带 NotConfigurable。</summary>
    private static readonly string[] InternalOnlyErrorIds =
    [
        // 生成器兜底/内部异常、语法损坏、注册阶段内部失败、事件/表单生成内部错误。
        "HTTPCLIENT001", "HTTPCLIENT003", "HTTPCLIENTREG001", "EHSG001", "FORM001",
    ];

    /// <summary>「用户可修复」诊断：级别必须保持 Error（仍阻断构建），但不得带 NotConfigurable。</summary>
    private static readonly string[] UserFixableErrorIds =
    [
        "HTTPCLIENT004", "HTTPCLIENT005", "HTTPCLIENT007", "HTTPCLIENT008",
        "HTTPCLIENT013", "HTTPCLIENT015", "HTTPCLIENT016", "HTTPCLIENTREG002",
        "FORM002", "FORM003",
    ];

    private static List<DiagnosticDescriptor> AllDescriptors()
        => typeof(Diagnostics).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToList();

    private static bool HasNotConfigurable(DiagnosticDescriptor d)
        => d.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable);

    /// <summary>
    /// 「Error + NotConfigurable」的集合必须恰好等于内部/环境类错误白名单。
    /// </summary>
    [Fact]
    public void ErrorDiagnosticsWithNotConfigurable_MatchInternalOnlyAllowList()
    {
        var actual = AllDescriptors()
            .Where(d => d.DefaultSeverity == DiagnosticSeverity.Error && HasNotConfigurable(d))
            .Select(d => d.Id)
            .ToHashSet(StringComparer.Ordinal);

        actual.Should().BeEquivalentTo(InternalOnlyErrorIds,
            "「Error + NotConfigurable」会让 csc 跳过整轮分析器执行，从而连坐抑制 MUD001/MUD002/MUD004；" +
            "该集合必须恰好等于内部/环境类错误白名单");
    }

    /// <summary>
    /// 「用户可修复」诊断不得带 NotConfigurable（避免连坐抑制 MUD*）。
    /// </summary>
    [Fact]
    public void UserFixableDiagnostics_MustNotCarryNotConfigurable()
    {
        var descriptors = AllDescriptors();
        foreach (var id in UserFixableErrorIds)
        {
            var descriptor = descriptors.SingleOrDefault(d => d.Id == id);
            descriptor.Should().NotBeNull($"{id} 必须仍存在于 Diagnostics.cs 中");

            HasNotConfigurable(descriptor!).Should().BeFalse(
                $"{id} 属「使用者改一行代码即可修复」的诊断，加 NotConfigurable 会连坐抑制同编译中的 MUD* 诊断");
        }
    }

    /// <summary>
    /// 「用户可修复」诊断去标签后必须保持 Error 级别（不得降级为 Warning）。
    /// </summary>
    [Fact]
    public void UserFixableDiagnostics_KeepErrorSeverity()
    {
        var descriptors = AllDescriptors();
        foreach (var id in UserFixableErrorIds)
        {
            var descriptor = descriptors.Single(d => d.Id == id);
            descriptor.DefaultSeverity.Should().Be(DiagnosticSeverity.Error,
                $"{id} 去标签后必须保持 Error —— 降级为 Warning 会把编译期失败改成运行期故障");
            descriptor.IsEnabledByDefault.Should().BeTrue($"{id} 必须默认启用");
        }
    }
}
