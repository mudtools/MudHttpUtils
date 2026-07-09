// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 按应用解析弹性策略的解析器工厂接口。
/// </summary>
/// <remarks>
/// 支持为不同应用配置不同的弹性策略（重试、超时、熔断）。
/// 在多应用场景下，各应用可拥有独立的 <see cref="IResiliencePolicyResolver"/> 实例，
/// 从而实现 per-app 弹性策略隔离。
///
/// 使用示例：
/// <code>
/// public class MyService
/// {
///     private readonly IAppResiliencePolicyResolver _appResolver;
///
///     public MyService(IAppResiliencePolicyResolver appResolver)
///     {
///         _appResolver = appResolver;
///     }
///
///     public IResiliencePolicyResolver? GetResolverForApp(string appKey)
///     {
///         return _appResolver.ResolveResolver(appKey);
///     }
/// }
/// </code>
/// </remarks>
public interface IAppResiliencePolicyResolver
{
    /// <summary>
    /// 根据应用键解析对应的弹性策略解析器。
    /// </summary>
    /// <param name="appKey">应用的唯一标识符。</param>
    /// <returns>该应用专属的 <see cref="IResiliencePolicyResolver"/> 实例；如果应用未配置专属策略则返回 null。</returns>
    IResiliencePolicyResolver? ResolveResolver(string appKey);
}
