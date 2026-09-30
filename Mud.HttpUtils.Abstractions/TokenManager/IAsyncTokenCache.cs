// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌缓存的<b>异步</b>扩展契约：为能执行真正异步 I/O 的缓存实现（如经
/// <see cref="TokenStoreBackedTokenCache{T}"/> 桥接的 Redis / 数据库 store）提供读穿透 / 写穿透能力，
/// 消除方案 B"内存镜像 + 异步写穿"的多实例镜像滞后（治理方案 §5.3 方案 C / S2）。
/// </summary>
/// <typeparam name="T">缓存值类型。</typeparam>
/// <remarks>
/// <para>
/// <b>为什么是派生接口而不是给 <see cref="ITokenCache{T}"/> 加异步成员</b>：
/// <c>netstandard2.0</c> 不支持默认接口实现（DIM）—— 给既有接口加成员会破坏所有现有实现
/// （本仓 3 个 + 下游自定义实现）。派生接口 + 能力探测（<c>is IAsyncTokenCache&lt;T&gt;</c>）
/// 与本仓已确立的 <c>IFeishuUserTokenStorePurge</c> 式探测手法同构，对既有实现零破坏（纯加法）。
/// </para>
/// <para>
/// <b>消费方式</b>：管理器在<b>本就是异步</b>的 <c>GetOrRefreshTokenAsync</c> /
/// <c>GetOrRefreshUserTokenAsync</c> 管线路径上做能力探测 —— 命中则走异步读穿透
/// （真穿透到 store），未命中则走既有同步路径（零改动、零行为变化）。
/// </para>
/// <para>
/// <b>语义约束</b>：
/// <list type="bullet">
/// <item><see cref="GetAsync"/> 返回 null 表示未命中（异步方法不能携带 <c>out</c> 参数，故与
/// <see cref="ITokenCache{T}.TryGet"/> 的 bool + out 形态不同）；命中值的有效性判定仍归管理器管线（不变量 #4 的延续）。</item>
/// <item>实现必须同时保证 <see cref="ITokenCache{T}"/> 同步成员的可用性（管理器在非异步路径仍会调用它们）。</item>
/// <item>同步实现（<see cref="ConcurrentDictionaryTokenCache{T}"/> / <see cref="MemoryCacheTokenCache{T}"/>）
/// <b>无需</b>实现本接口 —— 其同步成员已是最优路径。</item>
/// </list>
/// </para>
/// </remarks>
public interface IAsyncTokenCache<T> : ITokenCache<T> where T : class
{
    /// <summary>
    /// 异步读取指定键的缓存值（读穿透）。返回 null 表示未命中。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中则返回缓存值；未命中返回 null。</returns>
    ValueTask<T?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步写入指定键的缓存值（写穿透）。value 为 null 表示失效（移除）语义。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <param name="value">缓存值；null = 失效。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask SetAsync(string key, T? value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步移除指定键的缓存值。返回被移除的值；不存在返回 null。
    /// </summary>
    /// <param name="key">缓存键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>被移除的缓存值；不存在则 null。</returns>
    ValueTask<T?> RemoveAsync(string key, CancellationToken cancellationToken = default);
}
