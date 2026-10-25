// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 客户端身份接缝：为缓存 / 弹性作用域键提供「客户端维度」。
/// </summary>
/// <remarks>
/// <para>
/// 新增独立接口而非给 <see cref="IBaseHttpClient"/> 追加成员：netstandard2.0 消费面没有默认接口成员，
/// 给既有接口加抽象成员会破坏所有外部实现方。本接口由执行器以模式匹配（<c>as</c>）探测，
/// 实现方缺省时缓存键退化为历史格式（<c>{appKey}␟{CacheKey}</c>），零行为漂移。
/// </para>
/// <para>
/// Client 程序集的抽象客户端基类（<c>EnhancedHttpClient</c>）的既有 virtual 属性 <c>ClientName</c>
/// 天然满足本契约（命名客户端返回注册名，其余返回 <c>null</c>），客户端侧无需新增成员。
/// </para>
/// <para>
/// 动因：<see cref="IHttpResponseCache"/> 为进程级单例，而缓存键此前只携带应用维度。
/// HttpClient 模式（以及任何不切换应用上下文的装配，含 <see cref="IAppHttpClientFactory{TAppContext}"/>
/// 产出的每应用客户端）下应用维度恒为 <c>"default"</c>，不同客户端（不同 BaseAddress / 凭据）
/// 的同方法同参请求会命中同一键 ⇒ 跨客户端串缓存。
/// </para>
/// </remarks>
public interface IHttpClientIdentity
{
    /// <summary>
    /// 客户端名称（命名客户端注册名）。
    /// </summary>
    /// <value>命名客户端返回注册名；未命名客户端返回 <c>null</c>。</value>
    string? ClientName { get; }
}
