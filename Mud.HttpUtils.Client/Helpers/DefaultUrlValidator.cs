// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace Mud.HttpUtils;

/// <summary>
/// <see cref="IUrlValidator"/> 的默认实现，承载 <see cref="UrlValidator"/> 静态类的现有逻辑。
/// </summary>
/// <remarks>
/// <para>
/// 本类把全部成员委托给 <see cref="UrlValidator"/> 静态实现 —— 它是"默认策略的适配器"，
/// 不持有任何自己的状态。容器注册本类后，主链路会经
/// <see cref="EnhancedHttpClientOptions.UrlValidator"/> 调用它，效果与直连静态门面逐位一致。
/// </para>
/// <para>
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
    public System.Threading.Tasks.ValueTask ValidateUrlAsync(
    string? url, bool allowCustomBaseUrls = false, System.Threading.CancellationToken cancellationToken = default)
    => UrlValidator.ValidateUrlAsync(url, allowCustomBaseUrls, cancellationToken);

    /// <inheritdoc/>
    public void ValidateBaseUrl(string? baseUrl, bool allowCustomBaseUrls = false)
    => UrlValidator.ValidateBaseUrl(baseUrl, allowCustomBaseUrls);

    /// <inheritdoc/>
    public void ClearDnsCache()
    => UrlValidator.ClearDnsCache();
}
