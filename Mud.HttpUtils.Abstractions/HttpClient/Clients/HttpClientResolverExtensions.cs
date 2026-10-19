// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="IHttpClientResolver"/> 的扩展方法。
/// </summary>
public static class HttpClientResolverExtensions
{
    /// <summary>
    /// 获取已注册的命名客户端；未注册时按调用方委托创建（"未命中创建"语义）。
    /// </summary>
    /// <remarks>
    /// 以扩展方法而非接口成员提供：向 <see cref="IHttpClientResolver"/> 增加成员对既有实现方是
    /// 破坏性形状变更（netstandard2.0 目标无默认接口方法），扩展方法保持既有形状不变。
    /// </remarks>
    /// <param name="resolver">解析器。</param>
    /// <param name="clientName">命名客户端名称。</param>
    /// <param name="factory">未命中时的创建委托。</param>
    /// <returns>已注册或新建的 <see cref="IEnhancedHttpClient"/>。</returns>
    /// <exception cref="ArgumentNullException">参数为 null 时抛出。</exception>
    public static IEnhancedHttpClient GetOrCreateClient(
        this IHttpClientResolver resolver, string clientName, Func<IEnhancedHttpClient> factory)
    {
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        return resolver.TryGetClient(clientName, out var client) && client != null
            ? client
            : factory();
    }
}
