// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HttpClient 重试策略配置选项
/// </summary>
/// <remarks>
/// <para>配置 HTTP 请求失败时的自动重试行为。</para>
/// <para>重试策略会在遇到临时性故障（如网络超时、5xx 服务器错误）时自动重试请求，提高请求成功率。</para>
/// <para>配置示例：</para>
/// <code>
/// "Retry": {
///   "Enabled": true,
///   "MaxRetries": 3,
///   "DelayMilliseconds": 1000,
///   "UseExponentialBackoff": true
/// }
/// </code>
/// <para>指数退避策略：</para>
/// <para>当 UseExponentialBackoff 为 true 时，重试间隔时间会指数增长：</para>
/// <para>- 第 1 次重试：1000ms</para>
/// <para>- 第 2 次重试：2000ms</para>
/// <para>- 第 3 次重试：4000ms</para>
/// </remarks>
public class MudHttpClientRetryOptions
{
    /// <summary>
    /// 是否启用重试
    /// </summary>
    /// <remarks>
    /// 设置为 true 启用自动重试机制，false 则禁用。
    /// 默认值为 false。
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// 最大重试次数
    /// </summary>
    /// <remarks>
    /// 请求失败后的最大重试次数。
    /// 默认值为 3。
    /// </remarks>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试延迟时间（毫秒）
    /// </summary>
    /// <remarks>
    /// 每次重试前的等待时间，单位为毫秒。
    /// 当启用指数退避时，这是第一次重试的延迟时间。
    /// 默认值为 1000（1 秒）。
    /// </remarks>
    public int DelayMilliseconds { get; set; } = 1000;

    /// <summary>
    /// 是否使用指数退避策略
    /// </summary>
    /// <remarks>
    /// 设置为 true 时，每次重试的延迟时间会指数增长（1x, 2x, 4x, 8x...）。
    /// 这有助于避免在服务端恢复期间造成流量洪峰。
    /// 默认值为 true。
    /// </remarks>
    public bool UseExponentialBackoff { get; set; } = true;
}
