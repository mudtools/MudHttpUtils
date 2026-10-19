// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;
// 纯移动拆分（原 MudHttpClientLog.cs 的 Config 模块 region）。
internal static partial class MudHttpClientLog
{
    #region Config 模块 (EventId: 113-120)

#if NET6_0_OR_GREATER
    // 收尾）：起「缺 BaseAddress」的客户端**仍会被注册**（超时/默认头/自定义 URL 开关照常生效），
    // 原文案「该客户端不会被注册，…配置将被忽略」与实际行为相反，会把宿主引向错误的修复方向。
    // 方法名同步去掉 Skipped（EventId 113 保持不变，避免日志消费方的 EventId 映射断裂）。
    [LoggerMessage(EventId = 113, Level = LogLevel.Warning,
    Message = "MudHttpClients:Clients:{ClientName} 未配置 BaseAddress。该客户端仍会注册，TimeoutSeconds/DefaultHeaders/AllowCustomBaseUrls 均生效，但仅能接受绝对 URL 请求（相对 URL 将按 HttpClient 语义失败）。若该客户端本不应存在，请从配置节中移除。")]
    public static partial void ClientMissingBaseAddress(ILogger logger, string clientName);

    [LoggerMessage(EventId = 114, Level = LogLevel.Information,
    Message = "已应用 UrlValidator 域名白名单（{Count} 项）。")]
    public static partial void AllowedDomainsApplied(ILogger logger, int count);

    [LoggerMessage(EventId = 115, Level = LogLevel.Warning,
    Message = "Retry.AllowNonIdempotentRetry = true，RetryableHttpMethods 将被忽略（所有 HTTP 方法均允许重试）。如需仅重试幂等方法，请将其设为 false。")]
    public static partial void RetryableHttpMethodsIgnored(ILogger logger);

    // EventId 116 已废弃：原 AesEncryptionOptions.EnableAuthenticatedEncryption=false 安全警告，
    // 触发点随「AES 信封版本前缀歧义消除方案（OPT-C，移除裸 CBC 路径）」一并移除。编号冻结，不再复用。

    [LoggerMessage(EventId = 119, Level = LogLevel.Information,
    Message = "AesEncryptionProvider：当前运行时不支持 AesGcm，已使用 CBC + HMAC-SHA256（信封版本 0x03）进行认证加密。如需密文可跨 netstandard2.0/net6.0 运行时解密，可显式设置 AesEncryptionOptions.RequireCrossRuntimePortable = true。")]
    public static partial void AesGcmUnavailableFallbackToCbcHmac(ILogger logger);

    [LoggerMessage(EventId = 117, Level = LogLevel.Warning,
    Message = "检测到响应缓存双入口同时配置：AddHttpResponseCache 已显式注册 IHttpResponseCache，配置节 MudHttpClients:ResponseCache 将被忽略（TryAddSingleton 先注册者生效）。")]
    public static partial void ResponseCacheConfigurationIgnored(ILogger logger);

    [LoggerMessage(EventId = 118, Level = LogLevel.Debug,
    Message = "客户端 {ClientName} 的 AllowCustomBaseUrls 被覆盖为 {NewValue}（原值 {OldValue}）。")]
    public static partial void AllowCustomBaseUrlsOverridden(ILogger logger, string clientName, bool newValue, bool oldValue);
#else
 private static readonly Action<ILogger, string, Exception?> s_clientMissingBaseAddress =
 LoggerMessage.Define<string>(LogLevel.Warning, new EventId(113, nameof(ClientMissingBaseAddress)),
 "MudHttpClients:Clients:{ClientName} 未配置 BaseAddress。该客户端仍会注册，TimeoutSeconds/DefaultHeaders/AllowCustomBaseUrls 均生效，但仅能接受绝对 URL 请求（相对 URL 将按 HttpClient 语义失败）。若该客户端本不应存在，请从配置节中移除。");
 public static void ClientMissingBaseAddress(ILogger logger, string clientName)
 => s_clientMissingBaseAddress(logger, clientName, null);

 private static readonly Action<ILogger, int, Exception?> s_allowedDomainsApplied =
 LoggerMessage.Define<int>(LogLevel.Information, new EventId(114, nameof(AllowedDomainsApplied)),
 "已应用 UrlValidator 域名白名单（{Count} 项）。");
 public static void AllowedDomainsApplied(ILogger logger, int count)
 => s_allowedDomainsApplied(logger, count, null);

 private static readonly Action<ILogger, Exception?> s_retryableHttpMethodsIgnored =
 LoggerMessage.Define(LogLevel.Warning, new EventId(115, nameof(RetryableHttpMethodsIgnored)),
 "Retry.AllowNonIdempotentRetry = true，RetryableHttpMethods 将被忽略（所有 HTTP 方法均允许重试）。如需仅重试幂等方法，请将其设为 false。");
 public static void RetryableHttpMethodsIgnored(ILogger logger)
 => s_retryableHttpMethodsIgnored(logger, null);

// EventId 116 已废弃：原 AesEncryptionOptions.EnableAuthenticatedEncryption=false 安全警告，
// 触发点随「AES 信封版本前缀歧义消除方案（OPT-C，移除裸 CBC 路径）」一并移除。编号冻结，不再复用。

 private static readonly Action<ILogger, Exception?> s_aesGcmUnavailableFallbackToCbcHmac =
 LoggerMessage.Define(LogLevel.Information, new EventId(119, nameof(AesGcmUnavailableFallbackToCbcHmac)),
 "AesEncryptionProvider：当前运行时不支持 AesGcm，已使用 CBC + HMAC-SHA256（信封版本 0x03）进行认证加密。如需密文可跨 netstandard2.0/net6.0 运行时解密，可显式设置 AesEncryptionOptions.RequireCrossRuntimePortable = true。");
 public static void AesGcmUnavailableFallbackToCbcHmac(ILogger logger)
 => s_aesGcmUnavailableFallbackToCbcHmac(logger, null);

 private static readonly Action<ILogger, Exception?> s_responseCacheConfigurationIgnored =
 LoggerMessage.Define(LogLevel.Warning, new EventId(117, nameof(ResponseCacheConfigurationIgnored)),
 "检测到响应缓存双入口同时配置：AddHttpResponseCache 已显式注册 IHttpResponseCache，配置节 MudHttpClients:ResponseCache 将被忽略（TryAddSingleton 先注册者生效）。");
 public static void ResponseCacheConfigurationIgnored(ILogger logger)
 => s_responseCacheConfigurationIgnored(logger, null);

 private static readonly Action<ILogger, string, bool, bool, Exception?> s_allowCustomBaseUrlsOverridden =
 LoggerMessage.Define<string, bool, bool>(LogLevel.Debug, new EventId(118, nameof(AllowCustomBaseUrlsOverridden)),
 "客户端 {ClientName} 的 AllowCustomBaseUrls 被覆盖为 {NewValue}（原值 {OldValue}）。");
 public static void AllowCustomBaseUrlsOverridden(ILogger logger, string clientName, bool newValue, bool oldValue)
 => s_allowCustomBaseUrlsOverridden(logger, clientName, newValue, oldValue, null);
#endif

    #endregion
}
