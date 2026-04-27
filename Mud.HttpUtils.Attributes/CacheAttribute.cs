/// <summary>
/// 标记方法启用响应缓存功能。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法上，指示该方法的响应结果应被缓存。支持自定义缓存时长、缓存键模板、
/// 滑动过期和优先级等配置。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 基本缓存（5分钟）
/// [Get("/api/users")]
/// [Cache(300)]
/// Task&lt;List&lt;User&gt;&gt; GetUsersAsync();
/// 
/// // 自定义缓存键和滑动过期
/// [Get("/api/users/{id}")]
/// [Cache(600, CacheKeyTemplate = "user_{id}", UseSlidingExpiration = true)]
/// Task&lt;User&gt; GetUserAsync(int id);
/// 
/// // 按用户区分缓存和高优先级
/// [Get("/api/profile")]
/// [Cache(300, VaryByUser = true, Priority = CachePriority.High)]
/// Task&lt;Profile&gt; GetProfileAsync();
/// </code>
/// </example>
namespace Mud.HttpUtils.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CacheAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="CacheAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="durationSeconds">缓存持续时间（秒），默认为 300 秒（5分钟）。</param>
    public CacheAttribute(int durationSeconds = 300)
    {
        DurationSeconds = durationSeconds;
    }

    /// <summary>
    /// 获取或设置缓存持续时间（秒）。
    /// </summary>
    public int DurationSeconds { get; set; }

    /// <summary>
    /// 获取或设置缓存键模板，支持使用路径参数（如 "user_{id}"）。
    /// </summary>
    public string? CacheKeyTemplate { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示是否按用户区分缓存。
    /// </summary>
    /// <remarks>
    /// 启用后，不同用户的请求将使用不同的缓存键。
    /// </remarks>
    public bool VaryByUser { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示是否使用滑动过期。
    /// </summary>
    /// <remarks>
    /// 启用后，每次访问缓存项都会重置过期时间。
    /// </remarks>
    public bool UseSlidingExpiration { get; set; }

    /// <summary>
    /// 获取或设置缓存项的优先级。
    /// </summary>
    /// <value>默认为 <see cref="CachePriority.Normal"/>。</value>
    public CachePriority Priority { get; set; } = CachePriority.Normal;
}

/// <summary>
/// 缓存优先级枚举，定义缓存项在内存压力下的保留策略。
/// </summary>
public enum CachePriority
{
    /// <summary>
    /// 低优先级，在内存压力下最先被移除。
    /// </summary>
    Low,
    
    /// <summary>
    /// 普通优先级，默认的缓存保留策略。
    /// </summary>
    Normal,
    
    /// <summary>
    /// 高优先级，在内存压力下较晚被移除。
    /// </summary>
    High,
    
    /// <summary>
    /// 永不移除，除非显式删除或过期。
    /// </summary>
    NeverRemove
}
