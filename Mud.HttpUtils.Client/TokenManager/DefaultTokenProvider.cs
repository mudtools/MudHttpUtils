// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Client;

/// <summary>
/// ITokenProvider 的默认实现，通过 IMudAppContext 获取令牌管理器并获取令牌。
/// </summary>
/// <remarks>
/// 此实现复刻了原有代码生成中的 Token 获取逻辑，
/// 将其统一到运行时服务中，便于维护和测试。
/// </remarks>
public class DefaultTokenProvider : ITokenProvider
{
    /// <inheritdoc />
    public async Task<string> GetTokenAsync(IMudAppContext appContext, TokenRequest request, CancellationToken cancellationToken = default)
    {
        appContext.ThrowIfNull(nameof(appContext));
        request.ThrowIfNull(nameof(request));

        if (string.IsNullOrEmpty(request.TokenManagerKey))
            throw new ArgumentException("TokenManagerKey 不能为空。", nameof(request));

        var tokenManager = appContext.GetTokenManager(request.TokenManagerKey);
        if (tokenManager == null)
            throw new InvalidOperationException($"无法找到当前服务的令牌管理器，TokenManagerKey: {request.TokenManagerKey}");

        if (!string.IsNullOrEmpty(request.UserId) && tokenManager is IUserTokenManager userTokenManager)
        {
            if (request.Scopes != null && request.Scopes.Length > 0)
            {
                var token = await userTokenManager.GetOrRefreshTokenAsync(request.UserId, request.Scopes, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(token))
                    throw new InvalidOperationException($"无法获取到有效的用户访问令牌，TokenManagerKey: {request.TokenManagerKey}");
                return token!;
            }

            var userToken = await userTokenManager.GetOrRefreshTokenAsync(request.UserId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(userToken))
                throw new InvalidOperationException($"无法获取到有效的用户访问令牌，TokenManagerKey: {request.TokenManagerKey}");
            return userToken!;
        }

        if (request.Scopes != null && request.Scopes.Length > 0)
        {
            var scopedToken = await tokenManager.GetOrRefreshTokenAsync(request.Scopes, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(scopedToken))
                throw new InvalidOperationException($"无法获取到有效的访问令牌，TokenManagerKey: {request.TokenManagerKey}");
            return scopedToken!;
        }

        var defaultToken = await tokenManager.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(defaultToken))
            throw new InvalidOperationException($"无法获取到有效的访问令牌，TokenManagerKey: {request.TokenManagerKey}");
        return defaultToken!;
    }
}
