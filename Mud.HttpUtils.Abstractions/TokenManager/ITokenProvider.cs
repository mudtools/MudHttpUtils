// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// Token 提供器接口，负责根据请求参数获取访问令牌。
/// </summary>
/// <remarks>
/// 此接口是 Token 获取逻辑的统一抽象层，将 Token 的查找、获取、刷新逻辑
/// 从代码生成的类中剥离到运行时服务。
/// 默认实现为 <see cref="Client.DefaultTokenProvider"/>。
///
/// 重要：appContext 参数由生成代码传入 _appContext.Value，
/// 以确保 UseApp()/UseDefaultApp() 上下文切换的正确性。
/// </remarks>
public interface ITokenProvider
{
    /// <summary>
    /// 根据请求参数获取访问令牌。
    /// </summary>
    /// <param name="appContext">应用上下文，由生成代码传入当前的 _appContext.Value。</param>
    /// <param name="request">Token 请求参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>访问令牌字符串。</returns>
    /// <exception cref="ArgumentException">当 TokenManagerKey 为空时。</exception>
    /// <exception cref="InvalidOperationException">当 TokenManager 未找到或令牌获取失败时。</exception>
    Task<string> GetTokenAsync(IMudAppContext appContext, TokenRequest request, CancellationToken cancellationToken = default);
}
