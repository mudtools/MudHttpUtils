// -----------------------------------------------------------------------
//  M5-HC-06：策略作用域解析
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Resilience;

/// <summary>
/// M5-HC-06：根据请求与配置解析策略作用域键。
/// </summary>
internal static class ResiliencePolicyScopeResolver
{
    /// <summary>client_name 属性键（与 MudHttpObservability.ClientNamePropertyKey 同值）。</summary>
    private const string ClientNamePropertyKey = "__mud_client_name";

    public static string Resolve(HttpRequestMessage request, ResiliencePolicyScope scope)
    {
        if (scope == ResiliencePolicyScope.Global)
            return "global";

        var clientName = GetClientName(request);
        var host = request.RequestUri is { IsAbsoluteUri: true } u ? u.Host : "(relative)";

        return scope switch
        {
            ResiliencePolicyScope.PerClient => clientName ?? host,
            // PerHost（默认）：clientName + host，Named Client 在同一 host 上也可区分
            _ => $"{clientName ?? "(default)"}|{host}",
        };
    }

    private static string? GetClientName(HttpRequestMessage request)
    {
#if NETSTANDARD2_0
        return request.Properties.TryGetValue(ClientNamePropertyKey, out var v) ? v as string : null;
#else
        if (request.Options.TryGetValue(new HttpRequestOptionsKey<string>(ClientNamePropertyKey), out var name))
            return name;
        return request.Properties.TryGetValue(ClientNamePropertyKey, out var v) ? v as string : null;
#endif
    }
}
