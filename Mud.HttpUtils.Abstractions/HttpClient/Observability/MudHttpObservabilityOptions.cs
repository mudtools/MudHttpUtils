// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷与责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 可观测性（Span tag / 日志 / 诊断事件）的全局开关。
/// </summary>
/// <remarks>
/// <para>
/// 本类以静态属性形式提供模块级配置点。原因：可观测性采集入口（<c>MudHttpObservability.StartRequestActivity</c> 等）
/// 为静态方法，无法经构造注入实例选项；沿用 <c>UrlValidator</c> 静态门面的同一模式。
/// 测试如需翻转开关，应在 <c>finally</c> 中恢复默认值，避免影响并行用例。
/// </para>
/// <para>
/// 项目未发布：本类确立的是首版行为基线，不存在"变更"概念。
/// </para>
/// </remarks>
public static class MudHttpObservabilityOptions
{
    /// <summary>
    /// 是否对 Span tag、日志与诊断事件中的 URL 进行脱敏（掩码 <c>access_token</c> 等敏感 query 值）。
    /// 默认 <c>true</c>（安全默认）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 设为 <c>false</c> 时保留完整 URL，仅供已自行治理日志下游、明确需要完整 URL 排障的场景。
    /// 关闭后不影响 <see cref="ApiException.RequestUri"/>（始终保留完整 URI，由 <c>IExceptionRedactor</c> 兜底擦除）
    /// 与 URL 安全校验（始终使用原始 URL）。
    /// </para>
    /// <para>
    /// 脱敏实现见 <c>SensitiveUrlRedactor</c>，敏感键词表复用 <c>MessageSanitizer</c> 的字段词表。
    /// </para>
    /// </remarks>
    public static bool RedactUrlInTelemetry { get; set; } = true;
}
