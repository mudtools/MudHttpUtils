// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 委托式异常擦除器：将 <see cref="IExceptionRedactor.Redact(ApiException)"/> 委托给 <see cref="Action{T}"/>。
/// </summary>
public sealed class DelegateExceptionRedactor : IExceptionRedactor
{
    private readonly Action<ApiException> _action;

    /// <summary>
    /// 初始化 <see cref="DelegateExceptionRedactor"/> 实例。
    /// </summary>
    /// <param name="action">擦除委托。</param>
    public DelegateExceptionRedactor(Action<ApiException> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    /// <inheritdoc/>
    public void Redact(ApiException exception) => _action(exception);
}
