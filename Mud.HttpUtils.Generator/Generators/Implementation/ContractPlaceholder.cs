// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Generators.Context;

namespace Mud.HttpUtils.Generators.Implementation;

/// <summary>
/// 契约占位实现的共用支持（诊断报告）。
/// </summary>
/// <remarks>
/// <para>
/// 占位成员在运行期调用会抛 <see cref="System.NotSupportedException"/>。
/// 因此「发射占位实现」<b>必须</b>有编译期诊断陪跑，否则会把原本的编译期错误
/// （CS0535）降级为运行期故障 —— 这是本缺陷修复过程中刻意避免的安全退化。
/// </para>
/// <para>
/// 调用约定：仅当该成员的问题<b>没有</b>由其它诊断（MUD001/MUD002/HTTPCLIENT004/005/008/009/017）
/// 说明时才调用本方法，避免对同一问题重复报告两条错误。
/// </para>
/// </remarks>
internal static class ContractPlaceholder
{
    /// <summary>
    /// 报告「接口成员未生成实现，已发射占位实现」诊断（HTTPCLIENT024，Warning）。
    /// </summary>
    /// <param name="context">生成上下文。</param>
    /// <param name="member">未被实现的接口成员。</param>
    /// <param name="reason">原因描述（用于拼接诊断消息）。</param>
    public static void ReportUnsupportedMember(GeneratorContext context, ISymbol member, string reason)
    {
        var location = member.Locations.FirstOrDefault() ?? context.InterfaceDeclaration.GetLocation();

        context.ProductionContext.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.HttpClientMemberNotGeneratedPlaceholder,
            location,
            context.InterfaceSymbol.Name,
            member.Name,
            reason));
    }
}
