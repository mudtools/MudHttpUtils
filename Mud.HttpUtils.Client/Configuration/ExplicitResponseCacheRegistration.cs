// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 标记：已通过 <c>AddHttpResponseCache(...)</c> 显式注册 <see cref="IHttpResponseCache"/>（CFG-16）。
/// </summary>
/// <remarks>
/// <para>
/// 响应缓存容量存在两个入口（均为 <c>TryAddSingleton</c>，<b>先注册者生效</b>）：
/// <list type="number">
///   <item><description><c>AddHttpResponseCache(maxCacheSize, cleanupIntervalSeconds)</c>；</description></item>
///   <item><description>配置节 <c>MudHttpClients:ResponseCache</c>（由 <c>AddMudHttpClient</c> 的工厂读取）。</description></item>
/// </list>
/// </para>
/// <para>
/// 本标记用于在两者同时配置时给出启动期警告，避免「配置被静默忽略」。
/// </para>
/// </remarks>
internal sealed class ExplicitResponseCacheRegistration
{
}
