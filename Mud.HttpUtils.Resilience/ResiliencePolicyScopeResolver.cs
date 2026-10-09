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

    /// <summary>
    /// P2-4：应用维度属性键（<see cref="ResilienceConstants.AppKeyPropertyKey"/>）。
    /// 由执行器在调用弹性包装前写入；存在时追加进作用域键，使多应用共用同名客户端与
    /// 同一 host 时熔断/超时计数不跨应用共享（F-01 层B 同口径）。
    /// </summary>
    private const string AppKeyPropertyKey = ResilienceConstants.AppKeyPropertyKey;

    public static string Resolve(HttpRequestMessage request, ResiliencePolicyScope scope)
    {
        if (scope == ResiliencePolicyScope.Global)
            return "global";

        var clientName = GetClientName(request);
        var host = request.RequestUri is { IsAbsoluteUri: true } u ? u.Host : "(relative)";
        var appKey = GetAppKey(request);

        return scope switch
        {
            ResiliencePolicyScope.PerClient => AppendAppKey(clientName ?? host, appKey),
            // PerHost（默认）：clientName + host，Named Client 在同一 host 上也可区分；
            // AppKey 存在时再追加应用维度（无应用上下文时键与历史格式逐字节一致）。
            _ => AppendAppKey($"{clientName ?? "(default)"}|{host}", appKey),
        };
    }

    /// <summary>P2-4：AppKey 存在时把作用域键扩为 <c>{baseKey}|app:{appKey}</c>（跨应用隔离熔断计数）。</summary>
    private static string AppendAppKey(string baseKey, string? appKey)
        => string.IsNullOrEmpty(appKey) ? baseKey : $"{baseKey}|app:{appKey}";

    private static string? GetAppKey(HttpRequestMessage request)
    {
#if NETSTANDARD2_0
        return request.Properties.TryGetValue(AppKeyPropertyKey, out var a) ? a as string : null;
#else
        if (request.Options.TryGetValue(new HttpRequestOptionsKey<string>(AppKeyPropertyKey), out var appKey))
            return appKey;
        return null;
#endif
    }

    private static string? GetClientName(HttpRequestMessage request)
    {
#if NETSTANDARD2_0
        return request.Properties.TryGetValue(ClientNamePropertyKey, out var v) ? v as string : null;
#else
        if (request.Options.TryGetValue(new HttpRequestOptionsKey<string>(ClientNamePropertyKey), out var name))
            return name;
        // 兼容历史写入路径：客户端名可能仍写在已过时的 Properties 上。
#pragma warning disable CS0618 // HttpRequestMessage.Properties 已过时
        return request.Properties.TryGetValue(ClientNamePropertyKey, out var v) ? v as string : null;
#pragma warning restore CS0618 // HttpRequestMessage.Properties 已过时
#endif
    }
}
