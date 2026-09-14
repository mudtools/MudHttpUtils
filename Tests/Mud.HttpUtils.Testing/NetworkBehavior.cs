// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net;

namespace Mud.HttpUtils.Testing;

/// <summary>
/// 网络行为模拟器，用于在测试中模拟延迟、丢包等网络异常。
/// </summary>
/// <remarks>
/// <para>
/// 通过 <see cref="StubHttp"/> 的 <see cref="StubHttp.Respond"/> 方法返回的 <see cref="StubResponse"/>
/// 可链式调用 <c>.WithBehavior(networkBehavior)</c> 来附加网络行为。
/// </para>
/// <para>
/// 行为在 <see cref="StubHttp.SendAsync"/> 匹配到响应后、返回响应前执行。
/// </para>
/// </remarks>
public sealed class NetworkBehavior
{
    /// <summary>
    /// 获取或设置模拟延迟（毫秒）。0 表示不延迟。
    /// </summary>
    public int DelayMs { get; set; }

    /// <summary>
    /// 获取或设置丢包概率（0.0 ~ 1.0）。0 表示不丢包，1 表示总是丢包。
    /// </summary>
    /// <value>默认为 <c>0</c>（不丢包）。</value>
    public double FailureRate { get; set; }

    /// <summary>
    /// 获取或设置丢包时返回的 HTTP 状态码。
    /// </summary>
    /// <value>默认为 <see cref="HttpStatusCode.ServiceUnavailable"/>。</value>
    public HttpStatusCode FailureStatusCode { get; set; } = HttpStatusCode.ServiceUnavailable;

    private readonly Random _random = new();

    /// <summary>
    /// 创建一个指定延迟的行为。
    /// </summary>
    /// <param name="delayMs">延迟毫秒数。</param>
    /// <returns>配置好的 <see cref="NetworkBehavior"/> 实例。</returns>
    public static NetworkBehavior WithDelay(int delayMs) => new() { DelayMs = delayMs };

    /// <summary>
    /// 创建一个指定丢包概率的行为。
    /// </summary>
    /// <param name="failureRate">丢包概率（0.0 ~ 1.0）。</param>
    /// <param name="failureStatusCode">丢包时返回的状态码（默认 503）。</param>
    /// <returns>配置好的 <see cref="NetworkBehavior"/> 实例。</returns>
    public static NetworkBehavior WithFailure(double failureRate, HttpStatusCode failureStatusCode = HttpStatusCode.ServiceUnavailable)
        => new() { FailureRate = failureRate, FailureStatusCode = failureStatusCode };

    /// <summary>
    /// 创建一个同时包含延迟和丢包的行为。
    /// </summary>
    /// <param name="delayMs">延迟毫秒数。</param>
    /// <param name="failureRate">丢包概率（0.0 ~ 1.0）。</param>
    /// <param name="failureStatusCode">丢包时返回的状态码。</param>
    /// <returns>配置好的 <see cref="NetworkBehavior"/> 实例。</returns>
    public static NetworkBehavior WithDelayAndFailure(int delayMs, double failureRate,
        HttpStatusCode failureStatusCode = HttpStatusCode.ServiceUnavailable)
        => new() { DelayMs = delayMs, FailureRate = failureRate, FailureStatusCode = failureStatusCode };

    /// <summary>
    /// 判断本次请求是否应触发丢包。
    /// </summary>
    /// <returns>如果应丢包则返回 <c>true</c>。</returns>
    public bool ShouldFail()
    {
        return FailureRate > 0 && _random.NextDouble() < FailureRate;
    }
}
