// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 集合参数的 URL 序列化格式。
/// </summary>
/// <remarks>
/// 定义集合类型参数（如 <c>List&lt;T&gt;</c>、<c>T[]</c>）在 URL 查询参数或 form-urlencoded 中的序列化方式。
/// 与 Refit 的 <c>CollectionFormat</c> 对齐。
/// </remarks>
public enum CollectionFormat
{
    /// <summary>
    /// 每个元素单独作为一个参数（推荐）。如 <c>?ids=1&amp;ids=2&amp;ids=3</c>。
    /// </summary>
    Multi,

    /// <summary>
    /// 逗号分隔。如 <c>?ids=1,2,3</c>。
    /// </summary>
    Comma,

    /// <summary>
    /// 分号分隔。如 <c>?ids=1;2;3</c>。
    /// </summary>
    Semicolon,

    /// <summary>
    /// 空格分隔。如 <c>?ids=1%202%203</c>。
    /// </summary>
    Space,
}
