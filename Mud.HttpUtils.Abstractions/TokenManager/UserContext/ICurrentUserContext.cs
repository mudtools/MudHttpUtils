// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 当前用户上下文接口，提供当前登录用户的信息。
/// </summary>
/// <remarks>
/// 此接口用于在生成的 API 实现类中获取当前用户 ID，
/// 替代原有的 CurrentUserId 公共属性，解决并发场景下的线程安全问题。
/// 实现类应确保线程安全，推荐使用 AsyncLocal 或 HttpContext 等机制。
/// </remarks>
public interface ICurrentUserContext
{
    /// <summary>
    /// 获取当前用户的唯一标识符。
    /// </summary>
    /// <remarks>
    /// 如果当前无登录用户，返回 null。
    /// 实现类应确保此属性在异步上下文中正确传播（如使用 AsyncLocal）。
    /// </remarks>
    string? UserId { get; }

    /// <summary>
    /// 设置当前用户的唯一标识符。
    /// </summary>
    /// <param name="userId">用户标识符，为 null 时清除当前用户。</param>
    /// <remarks>
    /// 此方法用于在非 Web 场景下手动设置当前用户 ID。
    /// 在 ASP.NET Core 应用中，通常不需要调用此方法，用户 ID 应从 HttpContext 中自动获取。
    /// 实现类应确保设置的值在异步上下文中正确传播（如使用 AsyncLocal）。
    /// </remarks>
    void SetUserId(string? userId);
}
