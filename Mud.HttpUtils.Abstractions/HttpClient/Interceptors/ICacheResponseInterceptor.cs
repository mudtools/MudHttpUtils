// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 缓存响应拦截器接口，继承自 <see cref="IHttpResponseInterceptor"/>，提供HTTP响应的缓存管理能力。
/// </summary>
/// <remarks>
/// 该接口实现了响应缓存的完整生命周期管理，包括缓存的读取、写入和删除操作。
/// 支持绝对过期和滑动过期两种策略，适用于需要减少重复HTTP请求、提高响应速度的场景。
/// <para>
/// 实现注意事项：
/// <list type="bullet">
///   <item>缓存实现应该是线程安全的</item>
///   <item>应考虑内存使用限制，避免缓存过大导致内存溢出</item>
///   <item>对于敏感数据，应谨慎使用缓存或进行加密处理</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 在拦截器中使用缓存
/// public class MyCacheInterceptor : ICacheResponseInterceptor
/// {
///     public async Task InterceptResponseAsync(HttpResponseMessage response, CancellationToken ct)
///     {
///         if (TryGetCached(response.RequestMessage.RequestUri.ToString(), out string? cachedResponse))
///         {
///             // 使用缓存的响应
///             response.Content = new StringContent(cachedResponse);
///         }
///         else
///         {
///             // 发送请求并缓存结果
///             var content = await response.Content.ReadAsStringAsync(ct);
///             Set(response.RequestMessage.RequestUri.ToString(), content, 300); // 缓存5分钟
///         }
///     }
/// }
/// </code>
/// </example>
/// <seealso cref="IHttpResponseInterceptor"/>
/// <seealso cref="IHttpResponseCache"/>
public interface ICacheResponseInterceptor : IHttpResponseInterceptor, IHttpResponseCache
{
}
