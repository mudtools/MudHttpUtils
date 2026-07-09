// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mud.HttpUtils;

/// <summary>
/// 令牌恢复委托处理器，在收到 401 Unauthorized 响应时自动刷新令牌并重试请求。
/// </summary>
/// <remarks>
/// <para>此处理器应添加到 HttpClient 的消息处理管道中，位于所有其他 DelegatingHandler 之后（最靠近网络层）。</para>
/// <para>
/// 此类是 <see cref="TokenRecoveryExecutor"/> 的薄包装器，保留以支持通过
/// <c>AddHttpMessageHandler</c> 注册的向后兼容场景。
/// 新的代码应优先使用 <see cref="TokenRecoveryEnhancedClient"/>。
/// </para>
/// <para>工作流程：</para>
/// <list type="number">
///   <item>保存请求体内容（在发送前读取，避免流被消耗后无法重试）</item>
///   <item>发送请求到内部处理器</item>
///   <item>如果收到 401 响应：使缓存令牌失效 → 强制刷新令牌 → 构建新请求并应用令牌 → 重试</item>
///   <item>根据 <see cref="TokenRecoveryOptions.RecoveryMaxRetries"/> 配置重复步骤 3，直到成功或达到最大重试次数</item>
/// </list>
/// <para>令牌注入模式感知：</para>
/// <list type="bullet">
///   <item>生成代码通过 <see cref="TokenRecoveryContext"/> 在请求属性中传递注入模式信息</item>
///   <item>恢复处理器根据注入模式将新令牌应用到正确的位置（Header/Cookie/Query）</item>
///   <item>若无 <see cref="TokenRecoveryContext"/>，回退到默认的 Authorization Header 行为</item>
/// </list>
/// <para>用户级令牌支持：</para>
/// <list type="bullet">
///   <item>当提供 <see cref="IUserTokenManager"/> 时，恢复流程使用用户级令牌管理器</item>
///   <item>用户 ID 从 <see cref="TokenRecoveryContext.UserId"/> 或 <see cref="ICurrentUserContext"/> 获取</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 在注册 HttpClient 时添加令牌恢复处理器
/// services.AddHttpClient("MyApi")
///     .AddHttpMessageHandler&lt;TokenRecoveryDelegatingHandler&gt;();
/// </code>
/// </example>
public class TokenRecoveryDelegatingHandler : DelegatingHandler
{
    private readonly TokenRecoveryExecutor _recoveryExecutor;

    /// <summary>
    /// 初始化令牌恢复委托处理器。
    /// </summary>
    /// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
    /// <param name="options">令牌恢复配置选项（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRecoveryDelegatingHandler(
        ITokenManager tokenManager,
        TokenRecoveryOptions? options = null,
        ILogger<TokenRecoveryDelegatingHandler>? logger = null)
    {
        _recoveryExecutor = new TokenRecoveryExecutor(
            tokenManager, options: options, logger: logger);
    }

    /// <summary>
    /// 初始化令牌恢复委托处理器（支持用户级令牌恢复）。
    /// </summary>
    /// <param name="tokenManager">令牌管理器，用于刷新和失效令牌。</param>
    /// <param name="userTokenManager">用户令牌管理器，用于用户级令牌恢复（可选）。</param>
    /// <param name="currentUserContext">当前用户上下文，用于获取用户 ID（可选）。</param>
    /// <param name="options">令牌恢复配置选项（可选）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public TokenRecoveryDelegatingHandler(
        ITokenManager tokenManager,
        IUserTokenManager? userTokenManager,
        ICurrentUserContext? currentUserContext = null,
        TokenRecoveryOptions? options = null,
        ILogger<TokenRecoveryDelegatingHandler>? logger = null)
    {
        _recoveryExecutor = new TokenRecoveryExecutor(
            tokenManager, userTokenManager, currentUserContext, options, logger);
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return _recoveryExecutor.ExecuteAsync(
            request,
            (req, ct) => base.SendAsync(req, ct),
            cancellationToken);
    }
}
