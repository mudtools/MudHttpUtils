// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 执行相关常量定义。
/// </summary>
public static class HttpExecutionConstants
{
    /// <summary>
    /// 用于标记请求已由方法级弹性策略包装，装饰器应跳过全局弹性策略的属性键。
    /// </summary>
    /// <remarks>
    /// 当执行器使用方法级弹性特性（[Retry]、[CircuitBreaker]、[Timeout]）时，
    /// 会在 HttpRequestMessage 中设置此键，以避免与 ResilientHttpClient 装饰器的全局弹性策略产生双重包装。
    /// 此常量与 Mud.HttpUtils.Resilience.ResilienceConstants.SkipResiliencePropertyKey 保持一致。
    /// </remarks>
    public const string SkipResiliencePropertyKey = "__Mud_HttpUtils_SkipResilience";
}
