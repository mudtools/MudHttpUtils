// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="IUrlValidator"/> 的默认实现，承载 <see cref="UrlValidator"/> 静态类的现有逻辑。
/// </summary>
/// <remarks>
/// <para>
/// C4-P2（双轨过渡）：本类将 <see cref="UrlValidator"/> 静态类的方法委托到 DI 实例上。
/// 静态方法保留为<b>门面</b>：内部将调用转发到 <see cref="UrlValidator.Instance"/>（DI 注册时赋值）；
/// 未注册时退化为"进程级私有实例"，行为与现状等价。
/// </para>
/// <para>
/// 当前实现将所有调用委托回 <see cref="UrlValidator"/> 静态方法，保证既有逻辑零变更。
/// 后续迭代可将 DNS 缓存、双桶、stripe 锁整体搬移到本类，逐步消除静态状态。
/// </para>
/// </remarks>
internal sealed class DefaultUrlValidator : IUrlValidator
{
    /// <inheritdoc/>
    public void ConfigureAllowedDomains(IEnumerable<string> domains)
        => UrlValidator.ConfigureAllowedDomains(domains);

    /// <inheritdoc/>
    public void SetConfigurationDomains(IEnumerable<string> domains)
        => UrlValidator.SetConfigurationDomains(domains);

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetAllowedDomains()
        => UrlValidator.GetAllowedDomains();

    /// <inheritdoc/>
    public void AddAllowedDomain(string domain)
        => UrlValidator.AddAllowedDomain(domain);

    /// <inheritdoc/>
    public void RemoveAllowedDomain(string domain)
        => UrlValidator.RemoveAllowedDomain(domain);

    /// <inheritdoc/>
    public void ValidateUrl(string? url, bool allowCustomBaseUrls = false)
        => UrlValidator.ValidateUrl(url, allowCustomBaseUrls);

    /// <inheritdoc/>
    public void ValidateBaseUrl(string? baseUrl, bool allowCustomBaseUrls = false)
        => UrlValidator.ValidateBaseUrl(baseUrl, allowCustomBaseUrls);

    /// <inheritdoc/>
    public void ClearDnsCache()
        => UrlValidator.ClearDnsCache();
}
