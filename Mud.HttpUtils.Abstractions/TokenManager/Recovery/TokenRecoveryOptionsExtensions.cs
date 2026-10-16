// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// B6：把 <see cref="TokenRecoveryConfiguration"/> 应用到 <see cref="TokenRecoveryOptions"/>。
/// </summary>
public static class TokenRecoveryOptionsExtensions
{
    /// <summary>
    /// 逐字段应用配置（赋值经目标 setter 校验，非法值抛出与直接赋值一致的异常）。
    /// </summary>
    /// <param name="options">目标选项（通常由 <c>Configure&lt;TokenRecoveryOptions&gt;</c> 提供）。</param>
    /// <param name="configuration">配置投影。</param>
    /// <returns>同一 <paramref name="options"/> 实例（便于链式调用）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> 或 <paramref name="configuration"/> 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException">数值字段越界（由 <see cref="TokenRecoveryOptions"/> 的 setter 抛出）。</exception>
    /// <exception cref="ArgumentException"><see cref="TokenRecoveryConfiguration.TokenScheme"/> 为 null/空。</exception>
    /// <remarks>
    /// <b>不触碰</b> <see cref="TokenRecoveryOptions.TokenInvalidationDetector"/> 与
    /// <see cref="TokenRecoveryOptions.AdditionalTokenInvalidationDetectors"/>：二者只能编程式注入。
    /// 典型用法：
    /// <code>
    /// var section = configuration.GetSection(TokenRecoveryOptions.SectionName);
    /// services.Configure&lt;TokenRecoveryConfiguration&gt;(section);      // 源生成器可完整绑定
    /// services.AddOptions&lt;TokenRecoveryOptions&gt;()
    ///         .Configure&lt;IOptions&lt;TokenRecoveryConfiguration&gt;&gt;((o, c) =&gt; o.Apply(c.Value));
    /// </code>
    /// </remarks>
    public static TokenRecoveryOptions Apply(
        this TokenRecoveryOptions options,
        TokenRecoveryConfiguration configuration)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        options.Enabled = configuration.Enabled;
        options.RecoveryMaxRetries = configuration.RecoveryMaxRetries;
        options.TokenScheme = configuration.TokenScheme;
        options.RefreshTimeoutSeconds = configuration.RefreshTimeoutSeconds;
        options.RefreshWaitHardTimeoutSeconds = configuration.RefreshWaitHardTimeoutSeconds;
        options.MaxCachedRequestBodyBytes = configuration.MaxCachedRequestBodyBytes;
        options.BufferingMode = configuration.BufferingMode;
        options.AllowNonIdempotentRecovery = configuration.AllowNonIdempotentRecovery;
        options.RefreshDedupWindowSeconds = configuration.RefreshDedupWindowSeconds;
        options.MaxDedupEntries = configuration.MaxDedupEntries;
        options.MaxCapturedResponseBodyBytes = configuration.MaxCapturedResponseBodyBytes;

        return options;
    }
}
