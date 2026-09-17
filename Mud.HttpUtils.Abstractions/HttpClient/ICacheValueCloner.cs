// -----------------------------------------------------------------------
//  M5-HC-08：缓存值克隆钩子
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// M5-HC-08：缓存值可插拔克隆器。
/// </summary>
/// <remarks>
/// 默认缓存按引用存储并返回调用方提供的值（<c>ByReference</c>，零拷贝）。
/// 注册本接口后，<c>CacheValueSharing = Clone</c> 模式下命中时返回克隆实例，
/// 避免多个调用方共享同一可变对象导致跨请求数据串扰。
/// 未注册时回退 <c>ByReference</c> 语义。
/// </remarks>
public interface ICacheValueCloner
{
    /// <summary>克隆缓存值。返回与入参内容相等但引用不同的实例；null 原样返回。</summary>
    T? Clone<T>(T? value);
}
