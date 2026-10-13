// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Mud.HttpUtils;
// F-8：纯移动拆分（原 MudHttpClientLog.cs 的 Cache 模块 region）。
internal static partial class MudHttpClientLog
{
    #region Cache 模块 (EventId: 121-130)

#if NET6_0_OR_GREATER
    [LoggerMessage(EventId = 121, Level = LogLevel.Debug,
        Message = "从缓存返回: {CacheKey}")]
    public static partial void CacheHit(ILogger logger, string cacheKey);

    [LoggerMessage(EventId = 122, Level = LogLevel.Debug,
        Message = "已缓存: {CacheKey}, 持续 {Duration} 秒, 滑动过期: {UseSliding}")]
    public static partial void CacheSet(ILogger logger, string cacheKey, double duration, bool useSliding);

    [LoggerMessage(EventId = 123, Level = LogLevel.Debug,
        Message = "已移除缓存: {CacheKey}")]
    public static partial void CacheRemoved(ILogger logger, string cacheKey);
#else
    private static readonly Action<ILogger, string, Exception?> s_cacheHit =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(121, nameof(CacheHit)),
            "从缓存返回: {CacheKey}");
    public static void CacheHit(ILogger logger, string cacheKey) => s_cacheHit(logger, cacheKey, null);

    private static readonly Action<ILogger, string, double, bool, Exception?> s_cacheSet =
        LoggerMessage.Define<string, double, bool>(LogLevel.Debug, new EventId(122, nameof(CacheSet)),
            "已缓存: {CacheKey}, 持续 {Duration} 秒, 滑动过期: {UseSliding}");
    public static void CacheSet(ILogger logger, string cacheKey, double duration, bool useSliding)
        => s_cacheSet(logger, cacheKey, duration, useSliding, null);

    private static readonly Action<ILogger, string, Exception?> s_cacheRemoved =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(123, nameof(CacheRemoved)),
            "已移除缓存: {CacheKey}");
    public static void CacheRemoved(ILogger logger, string cacheKey) => s_cacheRemoved(logger, cacheKey, null);
#endif

    #endregion
}
