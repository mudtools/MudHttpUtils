using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Mud.HttpUtils;

internal static class TokenRefreshHelper
{
    public static async Task<bool> RefreshAllTokenManagersAsync(
        ConcurrentDictionary<string, ITokenManager> tokenManagers,
        ILogger logger,
        TokenRefreshBackgroundOptions options,
        CancellationToken cancellationToken)
    {
        if (tokenManagers.IsEmpty)
            return true;

        foreach (var kvp in tokenManagers)
        {
            try
            {
                logger.LogDebug("开始主动刷新令牌管理器 {Name}", kvp.Key);
                var token = await kvp.Value.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    logger.LogDebug("令牌管理器 {Name} 主动刷新完成", kvp.Key);
                }
            }
            catch (ObjectDisposedException)
            {
                logger.LogWarning("令牌管理器 {Name} 已释放，移除并停止刷新", kvp.Key);
                tokenManagers.TryRemove(kvp.Key, out _);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "令牌管理器 {Name} 主动刷新失败", kvp.Key);

                if (options.StopOnError)
                {
                    logger.LogCritical("令牌管理器 {Name} 主动刷新失败且配置为停止服务，后台服务将终止", kvp.Key);
                    return false;
                }
            }
        }

        return true;
    }
}
