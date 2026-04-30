// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client;

/// <summary>
/// ICurrentUserContext 的默认实现，使用 AsyncLocal 实现线程安全的用户 ID 传播。
/// </summary>
/// <remarks>
/// 此实现使用 AsyncLocal 确保用户 ID 在异步上下文中正确传播。
/// 适用于非 Web 场景或需要手动设置用户 ID 的场景。
/// 在 ASP.NET Core 应用中，建议替换为基于 HttpContext 的实现。
/// </remarks>
public class DefaultCurrentUserContext : ICurrentUserContext
{
    private static readonly AsyncLocal<string?> _userId = new();

    /// <inheritdoc />
    public string? UserId => _userId.Value;

    /// <summary>
    /// 设置当前用户 ID。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    public static void SetUserId(string? userId)
    {
        _userId.Value = userId;
    }
}
