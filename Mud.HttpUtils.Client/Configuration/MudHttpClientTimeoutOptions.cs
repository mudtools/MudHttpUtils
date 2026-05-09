// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HttpClient 超时策略配置选项
/// </summary>
/// <remarks>
/// <para>配置 HTTP 请求的超时时间限制，作为弹性策略的一部分。</para>
/// <para>与 <see cref="MudHttpClientOptions.TimeoutSeconds"/> 不同，这是 Polly 弹性策略层面的超时控制，
/// 可以与重试、熔断策略组合使用。</para>
/// <para>配置示例：</para>
/// <code>
/// "Timeout": {
///   "Enabled": true,
///   "TimeoutMilliseconds": 30000
/// }
/// </code>
/// <para>超时策略优先级：</para>
/// <list type="number">
///   <item><description>方法级 [Timeout] 特性（最高优先级）</description></item>
///   <item><description>弹性策略超时（TimeoutMilliseconds）</description></item>
///   <item><description>HttpClient 超时（TimeoutSeconds）</description></item>
///   <item><description>系统默认值（最低优先级）</description></item>
/// </list>
/// </remarks>
[Obsolete("此配置类用于配置文件绑定，运行时策略构建使用 TimeoutOptions。将在未来版本中移除，请迁移至 ResilienceOptions。")]
public class MudHttpClientTimeoutOptions
{
    /// <summary>
    /// 是否启用超时策略
    /// </summary>
    /// <remarks>
    /// 设置为 true 启用 Polly 超时策略，false 则禁用。
    /// 默认值为 false。
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    /// <remarks>
    /// 请求的最大允许执行时间，单位为毫秒。
    /// 超过此时间将触发 <see cref="System.TimeoutException"/>。
    /// 默认值为 30000（30 秒）。
    /// </remarks>
    public int TimeoutMilliseconds { get; set; } = 30000;
}
