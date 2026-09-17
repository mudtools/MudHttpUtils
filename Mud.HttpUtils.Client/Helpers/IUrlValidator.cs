// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace Mud.HttpUtils;

/// <summary>
/// URL 验证器接口（C4-P2：DI 化过渡）。
/// </summary>
/// <remarks>
/// <para>
/// 将 <see cref="UrlValidator"/> 静态类的全局状态迁移为可注入的接口，
/// 支持按应用隔离白名单与配置热更新。
/// </para>
/// <para>
/// <see cref="UrlValidator"/> 静态类保留为<b>门面</b>：静态方法内部把调用转发到
/// "最近一次由 DI 配置的实例"（静态弱引用桥接）。未注册时退化为"进程级单例私有实例"，
/// 行为与现状等价。
/// </para>
/// </remarks>
public interface IUrlValidator
{
    /// <summary>配置允许的域名集合（整体替换语义：调用后白名单恰好等于传入集合）。</summary>
    void ConfigureAllowedDomains(IEnumerable<string> domains);

    /// <summary>仅替换配置来源白名单桶，保留运行期新增域名。</summary>
    void SetConfigurationDomains(IEnumerable<string> domains);

    /// <summary>获取当前允许的域名集合快照。</summary>
    IReadOnlyCollection<string> GetAllowedDomains();

    /// <summary>添加一个运行期允许的域名。</summary>
    void AddAllowedDomain(string domain);

    /// <summary>移除一个运行期允许的域名。</summary>
    void RemoveAllowedDomain(string domain);

    /// <summary>验证 URL 是否安全（在白名单域名内且不包含私有 IP 地址）。</summary>
    void ValidateUrl(string? url, bool allowCustomBaseUrls = false);

    /// <summary>M5-HC-07：异步验证 URL 是否安全（DNS 解析不阻塞调用线程）。</summary>
    System.Threading.Tasks.ValueTask ValidateUrlAsync(string? url, bool allowCustomBaseUrls = false, System.Threading.CancellationToken cancellationToken = default);

    /// <summary>验证基础 URL 是否安全。</summary>
    void ValidateBaseUrl(string? baseUrl, bool allowCustomBaseUrls = false);

    /// <summary>清空 DNS 缓存。</summary>
    void ClearDnsCache();
}
