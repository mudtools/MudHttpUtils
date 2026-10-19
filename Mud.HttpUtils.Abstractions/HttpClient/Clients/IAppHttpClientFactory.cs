// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 按应用上下文获取（或创建）增强客户端的多应用工厂接缝：
/// 首次访问按约定命名创建并挂接该应用的令牌来源，之后按 appKey 缓存。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IHttpClientResolver"/> 的职责分界：后者按名字解析<b>注册期已注册</b>的命名客户端；
/// 本接口面向<b>多应用动态场景</b>——appKey 在编译期未知，需要"按约定命名创建 + 按 appKey 缓存 + 应用配置热
/// 更新时失效"三态，name-based 缓存解析器无法表达。
/// </para>
/// <para>
/// 默认实现（Client 程序集 <c>AppHttpClientFactory&lt;TAppContext&gt;</c>）：
/// 命名约定 <c>{前缀}-{appKey}</c>；容器中注册了 <c>TokenRecoveryExecutor</c> 时自动挂接令牌恢复客户端
/// （复用 <c>CreateTokenRecoveryClient</c> 既有能力），否则经 <see cref="IEnhancedHttpClientFactory"/> 解析。
/// </para>
/// </remarks>
/// <typeparam name="TAppContext">应用上下文类型（须实现 <see cref="IMudAppContext"/>）。</typeparam>
public interface IAppHttpClientFactory<in TAppContext> where TAppContext : IMudAppContext
{
    /// <summary>获取指定应用的增强客户端（未命中时创建）。</summary>
    /// <param name="appContext">应用上下文（<see cref="IMudAppContext.AppKey"/> 为缓存与命名依据）。</param>
    /// <returns>该应用的 <see cref="IEnhancedHttpClient"/>（同一 appKey 多次获取返回同一实例）。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="appContext"/> 为 null。</exception>
    IEnhancedHttpClient GetClient(TAppContext appContext);

    /// <summary>使指定应用的客户端缓存失效（应用配置热更新时调用）。</summary>
    /// <param name="appKey">应用标识。</param>
    void Invalidate(string appKey);
}
