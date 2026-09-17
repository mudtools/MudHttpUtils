// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// Mud HttpClient 应用程序配置选项
/// </summary>
/// <remarks>
/// <para>用于配置多个命名 HttpClient 实例的全局选项。</para>
/// <para>配置示例：</para>
/// <code>
/// {
///   "MudHttpClients": {
///     "DefaultClientName": "Default",
///     "AllowedDomains": [ "api.example.com", "cdn.example.com" ],
///     "Clients": {
///       "Default": {
///         "BaseAddress": "https://api.example.com",
///         "TimeoutSeconds": 30
///       },
///       "ExternalApi": {
///         "BaseAddress": "https://external.api.com",
///         "TimeoutSeconds": 60
///       }
///     }
///   }
/// }
/// </code>
/// </remarks>
public class MudHttpClientApplicationOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "MudHttpClients";

    /// <summary>
    /// 命名的 HttpClient 配置集合
    /// </summary>
    /// <remarks>
    /// <para>键为客户端名称，值为该客户端的配置选项。</para>
    /// <para>
    /// <b>MT-13（BC-25）</b>：客户端名称<b>区分大小写</b>（<see cref="StringComparer.Ordinal"/>）。
    /// 此前本字典使用 <c>OrdinalIgnoreCase</c>，而命名 HttpClient（<c>AddHttpClient</c>）、
    /// keyed DI 注册、<see cref="IEnhancedHttpClientFactory"/> 缓存与
    /// <see cref="DefaultAppManager{TAppContext}"/> 均使用 <c>Ordinal</c> ——
    /// 于是「配置写 <c>Default</c>、代码传 <c>default</c>」会出现
    /// <b>配置覆盖生效但客户端解析失败</b>的分裂行为。现统一为 <c>Ordinal</c>，语义唯一。
    /// </para>
    /// </remarks>
    public Dictionary<string, MudHttpClientOptions> Clients { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 默认客户端名称
    /// </summary>
    /// <remarks>
    /// 当未指定特定客户端时使用的默认客户端名称。
    /// 如果未设置，将使用第一个配置的客户端。
    /// </remarks>
    public string? DefaultClientName { get; set; }

    /// <summary>
    /// 全局允许的域名白名单
    /// </summary>
    /// <remarks>
    /// <para>配置后会在应用启动时自动调用 <see cref="UrlValidator.ConfigureAllowedDomains"/> 设置白名单。</para>
    /// <para>所有 HttpClient 实例共享此白名单。白名单内的域名无需 <see cref="MudHttpClientOptions.AllowCustomBaseUrls"/> 即可直接访问。</para>
    /// <para>如需在运行时动态修改白名单，可使用 <see cref="UrlValidator.AddAllowedDomain"/> 和 <see cref="UrlValidator.RemoveAllowedDomain"/>。</para>
    /// </remarks>
    public List<string> AllowedDomains { get; set; } = [];

    /// <summary>
    /// MT-10：是否允许白名单域名使用非 HTTPS 协议访问。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 修复前：白名单命中即整体跳过后续校验（含 scheme 检查），因此 <c>AllowedDomains</c> 中的域名
    /// 即使以 <c>http://</c> 访问也会被放行 —— 令牌（Header / Query / Cookie 任一注入模式）将<b>明文上网</b>。
    /// </para>
    /// <para>
    /// 现在白名单域名仍强制 HTTPS（回环地址豁免，保留本地开发）。
    /// 确需在完全受信内网使用 HTTP 时，可显式开启本开关。
    /// </para>
    /// </remarks>
    public bool AllowInsecureWhitelistedDomains { get; set; }

    /// <summary>
    /// HTTP 响应缓存配置
    /// </summary>
    /// <remarks>
    /// <para>HC-01 修复：将原硬编码的 <see cref="MemoryHttpResponseCache"/> 容量(1000)与清理间隔(60秒)抽为可配置项。</para>
    /// <para>仅在未手动注册 <see cref="IHttpResponseCache"/> 实现时生效（TryAddSingleton 语义）。</para>
    /// <para>配置示例：</para>
    /// <code>
    /// {
    ///   "MudHttpClients": {
    ///     "ResponseCache": {
    ///       "MaxCacheSize": 5000,
    ///       "CleanupIntervalSeconds": 30
    ///     }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public ResponseCacheOptions ResponseCache { get; set; } = new();
}

/// <summary>
/// HTTP 响应缓存配置选项
/// </summary>
public class ResponseCacheOptions
{
    /// <summary>
    /// 默认最大缓存条目数。
    /// </summary>
    public const int DefaultMaxCacheSize = 1000;

    /// <summary>
    /// 默认清理间隔（秒）。
    /// </summary>
    public const int DefaultCleanupIntervalSeconds = 60;

    private int _maxCacheSize = DefaultMaxCacheSize;
    private int _cleanupIntervalSeconds = DefaultCleanupIntervalSeconds;

    /// <summary>
    /// 最大缓存条目数，默认 <see cref="DefaultMaxCacheSize"/>（1000）。必须大于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int MaxCacheSize
    {
        get => _maxCacheSize;
        set => _maxCacheSize = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxCacheSize), "最大缓存条目数必须大于 0。");
    }

    /// <summary>
    /// 清理间隔（秒），默认 <see cref="DefaultCleanupIntervalSeconds"/>（60 秒）。必须大于 0。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">设置小于等于 0 的值时抛出。</exception>
    public int CleanupIntervalSeconds
    {
        get => _cleanupIntervalSeconds;
        set => _cleanupIntervalSeconds = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(CleanupIntervalSeconds), "清理间隔必须大于 0 秒。");
    }
}
