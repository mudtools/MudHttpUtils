// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client;

using Microsoft.Extensions.Logging;

/// <summary>
/// ITokenProvider 的默认实现，通过 IMudAppContext 获取令牌管理器并获取令牌。
/// </summary>
/// <remarks>
/// 此实现不持有 IMudAppContext 引用，而是通过方法参数逐调用接收，
/// 以确保生成代码中 UseApp()/UseDefaultApp() 上下文切换的正确性。
/// </remarks>
public class DefaultTokenProvider : ITokenProvider
{
    private readonly ILogger<DefaultTokenProvider> _logger;

    public DefaultTokenProvider(ILogger<DefaultTokenProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(IMudAppContext appContext, TokenRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        appContext.ThrowIfNull(nameof(appContext));

        var tokenManager = appContext.GetTokenManager(request.TokenManagerKey);
        if (tokenManager == null)
        {
            throw new InvalidOperationException(
                $"TokenManager '{request.TokenManagerKey}' 未找到，请确认已正确注册。");
        }

        if (!string.IsNullOrEmpty(request.UserId))
        {
            return await GetUserTokenAsync(tokenManager, request, cancellationToken).ConfigureAwait(false);
        }

        return await GetTenantTokenAsync(tokenManager, request, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateRequest(TokenRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrEmpty(request.TokenManagerKey))
            throw new ArgumentException("TokenManagerKey 不能为空。", nameof(request));
    }

    private async Task<string> GetUserTokenAsync(
        ITokenManager tokenManager, TokenRequest request, CancellationToken cancellationToken)
    {
        if (tokenManager is not IUserTokenManager userTokenManager)
        {
            throw new InvalidOperationException(
                $"TokenManager '{request.TokenManagerKey}' 未实现 IUserTokenManager，无法获取用户令牌。");
        }

        string? token;

        if (request.Scopes?.Length > 0)
        {
            token = await userTokenManager.GetOrRefreshTokenAsync(
                request.UserId, request.Scopes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            token = await userTokenManager.GetOrRefreshTokenAsync(
                request.UserId, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("获取用户令牌失败，UserId: '{UserId}'，TokenManagerKey: '{TokenManagerKey}'。",
                request.UserId, request.TokenManagerKey);
            throw new InvalidOperationException(
                $"获取用户令牌失败，UserId: '{request.UserId}'，TokenManagerKey: '{request.TokenManagerKey}'。");
        }

        return token!;
    }

    private async Task<string> GetTenantTokenAsync(
        ITokenManager tokenManager, TokenRequest request, CancellationToken cancellationToken)
    {
        string? token;

        if (request.Scopes?.Length > 0)
        {
            token = await tokenManager.GetOrRefreshTokenAsync(
                request.Scopes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            token = await tokenManager.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("获取令牌失败，TokenManagerKey: '{TokenManagerKey}'。", request.TokenManagerKey);
            throw new InvalidOperationException(
                $"获取令牌失败，TokenManagerKey: '{request.TokenManagerKey}'。");
        }

        return token!;
    }
}
