// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记方法启用响应缓存功能。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法上，指示该方法的响应结果应被缓存。支持自定义缓存时长、缓存键模板、
/// 按用户区分与滑动过期等配置。
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
/// // 按用户区分缓存
/// [Get("/api/profile")]
/// [Cache(300, VaryByUser = true)]
/// Task&lt;Profile&gt; GetProfileAsync();
/// </code>
/// </example>
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
    /// 生成器已支持该属性 —— 经 <see cref="Mud.HttpUtils.CacheOptions.UseSlidingExpiration"/>
    /// 传递至缓存层（<c>IHttpResponseCache.Set(key, value, expiration, useSlidingExpiration)</c>）。
    /// </remarks>
    public bool UseSlidingExpiration { get; set; }

    /// <summary>
    // CFG-27：原 Priority 属性与 CachePriority 枚举已移除 —— 生成器从未处理该属性
    // （此前为 [Obsolete] 警告，设置时产生 HTTPCLIENT019 提示；移除后使用将报 CS0117/CS0246）。
    // 该属性无运行时消费点，属静默失效配置，故直接移除而非保留。
}
