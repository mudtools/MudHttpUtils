// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 定义客户端 BaseAddress 与方法相对 URL 的拼接方式。
/// </summary>
/// <remarks>
/// <para>
/// </para>
/// <para>
/// <c>Default</c> 命名更中性，符合 Mud.HttpUtils 独立项目身份。
/// </para>
/// </remarks>
public enum UrlResolutionMode
{
    /// <summary>
    /// Mud 默认行为（向后兼容）：相对路径以 <c>/</c> 开头时拼接 BaseAddress，
    /// 末尾 <c>/</c> 被裁剪以避免双斜杠。
    /// </summary>
    Default,

    /// <summary>
    /// RFC 3986 / <see cref="System.Net.Http.HttpClient"/> 标准拼接：
    /// 使用 <see cref="System.Uri"/> 合并规则。不以 <c>/</c> 开头的相对路径被追加到 BaseAddress 路径，
    /// 以 <c>/</c> 开头则替换路径。末尾 <c>/</c> 保留。
    /// </summary>
    Rfc3986
}
