// -----------------------------------------------------------------------
// 作者：Mud Studio  版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

// 仅 net6+：被检测的能力（连接期 SSRF 校验）与检测逻辑均只存在于 net6+ 分支。
#if NET6_0_OR_GREATER

namespace Mud.HttpUtils;

/// <summary>
/// SSRF 严格模式 + 空白名单组合的启动期提示组件（fail-closed 默认值的可观测性兜底）。
/// </summary>
/// <remarks>
/// <para>
/// 背景：严格模式（<c>AllowCustomBaseUrls=false</c>，默认）客户端在连接期以
/// <see cref="SsrfSafeSocketsHttpHandler"/> fail-closed 运行；宿主未登记任何域名白名单时，
/// 其全部请求将在建连前被拦下且无任何提示（纯编程式注册宿主曾实证该静默故障面）。
/// </para>
/// <para>
/// 触发时机与 <see cref="AllowedDomainsReloader"/> 等同惯例：由首个客户端创建时
/// （<see cref="HttpClientServiceCollectionExtensions.CreateEnhancedClient"/>）的强制解析驱动。
/// 按<b>容器单例</b>保证「每宿主只提示一次」——不用进程级静态门控，避免多宿主/测试场景串味。
/// 仅 net6+ 注册与解析（连接期 SSRF 校验本身仅 net6+ 存在）。
/// </para>
/// </remarks>
internal sealed class SsrfStrictModeAllowlistStartupCheck
{
    /// <summary>构造时执行组合检测（单例语义 ⇒ 每宿主一次）。</summary>
    /// <param name="serviceProvider">服务提供程序。</param>
    public SsrfStrictModeAllowlistStartupCheck(IServiceProvider serviceProvider)
    {
        HttpClientServiceCollectionExtensions.WarnIfStrictModeWithoutAllowlist(serviceProvider);
    }
}

#endif
