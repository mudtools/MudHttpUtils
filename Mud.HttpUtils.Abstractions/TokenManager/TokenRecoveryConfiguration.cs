// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// B6：<see cref="TokenRecoveryOptions"/> 的<b>纯 DTO 投影</b>——只含可被配置绑定源生成器
/// （<c>EnableConfigurationBindingGenerator=true</c>）完整支持的成员（基元 / 字符串 / 枚举）。
/// </summary>
/// <remarks>
/// <para>
/// <b>解决什么</b>：<see cref="TokenRecoveryOptions.TokenInvalidationDetector"/> 是<b>接口类型</b>属性，
/// 下游启用绑定源生成器后，为 <c>TokenRecoveryOptions</c> 生成绑定代码会在<b>下游编译单元</b>产出
/// <c>SYSLIB1100</c> / <c>SYSLIB1101</c>（生成器无法为不可绑定成员生成代码），且该诊断无法用
/// <c>#pragma</c> 抑制，只能项目级 <c>NoWarn</c>。
/// 下游改为"绑定本类型 + <see cref="TokenRecoveryOptionsExtensions.Apply"/>"即可消除诊断，
/// 同时不再依赖运行时反射绑定（AOT 友好）。
/// </para>
/// <para>
/// <b>契约</b>：本类型与 <see cref="TokenRecoveryOptions"/> 的基元成员<b>逐字段同名同默认值</b>；
/// <c>Apply</c> 逐字段赋值（赋值经 <see cref="TokenRecoveryOptions"/> 的 setter 校验，
/// 非法值仍抛 <see cref="ArgumentOutOfRangeException"/> / <see cref="ArgumentException"/>）。
/// </para>
/// <para>
/// <b>不包含</b>：<see cref="TokenRecoveryOptions.TokenInvalidationDetector"/> 与
/// <see cref="TokenRecoveryOptions.AdditionalTokenInvalidationDetectors"/> —— 二者属<b>编程式注入面</b>，
/// 不参与配置绑定（与 <see cref="EnhancedHttpClientOptions"/> 的既有约定一致）。
/// </para>
/// </remarks>
public sealed class TokenRecoveryConfiguration
{
    /// <summary>是否启用令牌恢复机制，默认 <c>true</c>。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>最大重试次数，默认 1。必须 ≥ 0。</summary>
    public int RecoveryMaxRetries { get; set; } = 1;

    /// <summary>认证方案（如 "Bearer"），默认 "Bearer"。不得为 null/空。</summary>
    public string TokenScheme { get; set; } = "Bearer";

    /// <summary>刷新超时兜底（秒），默认 30。必须 &gt; 0。</summary>
    public double RefreshTimeoutSeconds { get; set; } = 30;

    /// <summary>等待刷新的硬墙钟（秒），默认 0 = 自动（<see cref="RefreshTimeoutSeconds"/> + 5）。必须 ≥ 0。</summary>
    public double RefreshWaitHardTimeoutSeconds { get; set; }

    /// <summary>请求体缓冲上限（字节），默认 1MB。0 = 流式优先模式。必须 ≥ 0。</summary>
    public long MaxCachedRequestBodyBytes { get; set; } = 1 * 1024 * 1024;

    /// <summary>请求体缓冲策略，默认 <see cref="RequestBodyBufferingMode.Auto"/>。</summary>
    public RequestBodyBufferingMode BufferingMode { get; set; } = RequestBodyBufferingMode.Auto;

    /// <summary>是否允许对非幂等方法执行 401 重放，默认 <c>false</c>。</summary>
    public bool AllowNonIdempotentRecovery { get; set; }

    /// <summary>刷新去重窗口（秒），默认 2。必须 ≥ 0。</summary>
    public double RefreshDedupWindowSeconds { get; set; } = 2;

    /// <summary>去重表条目上限，默认 1024。必须 &gt; 0。</summary>
    public int MaxDedupEntries { get; set; } = 1024;

    /// <summary>判定器响应体捕获上限（字节），默认 4096。必须 ≥ 0。</summary>
    public int MaxCapturedResponseBodyBytes { get; set; } = 4096;
}

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
