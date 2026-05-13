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
/// <para>此实现使用 AsyncLocal 确保用户 ID 在异步上下文中正确传播。</para>
/// <para>适用于非 Web 场景或需要手动设置用户 ID 的场景。</para>
/// <para>在 ASP.NET Core 应用中，建议替换为基于 HttpContext 的实现。</para>
/// <para>每个实例拥有独立的 AsyncLocal 存储，支持多实例并行使用而互不干扰。
/// 如需跨实例共享用户上下文，请在 DI 容器中注册为 Singleton。</para>
/// </remarks>
public class DefaultCurrentUserContext<TUser> : ICurrentUserContext
    where TUser : CurrentUserInfo, new()
{
    private readonly AsyncLocal<TUser?> _user = new();

    /// <summary>
    /// 获取当前用户信息对象。
    /// </summary>
    public TUser? User => _user.Value;

    /// <inheritdoc />
    public string? UserId => _user.Value?.UserId;

    /// <inheritdoc />
    public void SetUserId(string? userId)
    {
        if (userId != null)
        {
            var user = _user.Value ?? new TUser();
            user.UserId = userId;
            _user.Value = user;
        }
        else
        {
            _user.Value = default;
        }
    }

    /// <summary>
    /// 设置当前用户信息。
    /// </summary>
    /// <param name="user">用户信息对象。</param>
    public void SetUser(TUser? user)
    {
        _user.Value = user;
    }
}
