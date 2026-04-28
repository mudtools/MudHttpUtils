// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 应用上下文切换器接口，用于在不同的应用上下文之间切换和管理令牌。
/// </summary>
public interface IAppContextSwitcher
{
    /// <summary>
    /// 切换到指定的应用上下文。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <returns>切换后的应用上下文实例。</returns>
    IMudAppContext UseApp(string appKey);

    /// <summary>
    /// 切换到默认的应用上下文。
    /// </summary>
    /// <returns>默认的应用上下文实例。</returns>
    IMudAppContext UseDefaultApp();

    /// <summary>
    /// 创建一个应用上下文作用域，切换到指定的应用上下文，并在作用域结束时自动恢复之前的上下文。
    /// 使用 <c>using</c> 语句确保上下文恢复，避免 <see cref="UseApp"/> 导致的 <see cref="AsyncLocal{T}"/> 上下文泄漏。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <returns>一个 <see cref="IDisposable"/> 对象，释放时恢复之前的上下文。</returns>
    /// <example>
    /// <code>
    /// using (api.BeginScope("AppA"))
    /// {
    ///     // 在此作用域内，所有请求使用 AppA 的上下文
    ///     await api.GetDataAsync();
    /// } // 作用域结束，自动恢复之前的上下文
    /// </code>
    /// </example>
    IDisposable BeginScope(string appKey);

    /// <summary>
    /// 异步获取当前应用上下文的访问令牌。
    /// </summary>
    /// <returns>包含访问令牌的字符串任务。</returns>
    Task<string> GetTokenAsync();
}
