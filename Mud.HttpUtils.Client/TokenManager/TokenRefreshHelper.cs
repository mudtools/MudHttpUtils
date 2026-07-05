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
                MudHttpClientLog.TokenRefreshStarting(logger, kvp.Key);
                var token = await kvp.Value.GetOrRefreshTokenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    MudHttpClientLog.TokenRefreshCompleted(logger, kvp.Key);
                }
            }
            catch (ObjectDisposedException)
            {
                MudHttpClientLog.TokenManagerDisposed(logger, kvp.Key);
                tokenManagers.TryRemove(kvp.Key, out _);
            }
            catch (Exception ex)
            {
                MudHttpClientLog.TokenRefreshFailed(logger, kvp.Key, ex);

                if (options.StopOnError)
                {
                    MudHttpClientLog.TokenRefreshFailedAndStopped(logger, kvp.Key);
                    return false;
                }
            }
        }

        return true;
    }
}
