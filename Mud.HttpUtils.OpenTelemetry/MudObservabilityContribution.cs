// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.OpenTelemetry;

/// <summary>
/// 产品线对 Mud 可观测性管道的贡献描述。内核据此注册源与 Meter，不认识任何产品类型。
/// </summary>
/// <remarks>
/// <para>每个产品线（如 <c>Mud.HttpUtils</c> / <c>Mud.Feishu</c> / <c>Mud.Wechat</c>）构造一个本类型实例，
/// 填入该产品的 <see cref="ActivitySourceName"/>、<see cref="MeterName"/> 或 <see cref="MeterWildcard"/>，
/// 然后传递给 <see cref="O:MudObservabilityBootstrap.AddMudObservability"/>。</para>
/// <para>全部属性为普通 <c>set</c>（非 <c>init</c>）：下游仓库未注入 <c>IsExternalInit</c> polyfill，
/// <c>init</c> 会让下游在 <c>netstandard2.0</c> 下用对象初始化器时 CS9058。本类型禁止改为 record。</para>
/// </remarks>
public sealed class MudObservabilityContribution
{
    /// <summary>
    /// 产品名（如 <c>Mud.HttpUtils</c> / <c>Mud.Feishu</c> / <c>Mud.Wechat</c>）。
    /// 用于重复入口守卫的错误信息；必填。
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// 产品根源 ActivitySource 名（如 <c>Mud.HttpUtils.HttpClient</c> / <c>Mud.Feishu</c> / <c>Mud.Wechat</c>）；必填。
    /// </summary>
    public string ActivitySourceName { get; set; } = string.Empty;

    /// <summary>
    /// 精确 Meter 名（如 <c>Mud.HttpUtils.HttpClient</c> / <c>Mud.Feishu</c>）。
    /// 与 <see cref="MeterWildcard"/> 至少填一个。
    /// </summary>
    public string? MeterName { get; set; }

    /// <summary>
    /// 通配 Meter 名（如 <c>Mud.Wechat*</c>，覆盖该产品线全部子 Meter）。
    /// 与 <see cref="MeterName"/> 至少填一个。
    /// </summary>
    public string? MeterWildcard { get; set; }

    /// <summary>
    /// 默认 ServiceName（<see cref="MudObservabilityOptions.ServiceName"/> 为空时回填）。
    /// </summary>
    public string DefaultServiceName { get; set; } = string.Empty;

    /// <summary>
    /// 默认 ServiceVersion（<see cref="MudObservabilityOptions.ServiceVersion"/> 为空时回填）。
    /// </summary>
    public string? DefaultServiceVersion { get; set; }

    /// <summary>
    /// 是否额外注册 Mud.HttpUtils 的源与 Meter（承担原各产品 <c>IncludeMudHttpUtils</c> 开关的落地）。
    /// </summary>
    /// <remarks>
    /// 当产品线自身不是 Mud.HttpUtils 但需要同时采集 Mud.HttpUtils 的 Tracing/Metrics 时设为 <c>true</c>。
    /// Mud.HttpUtils 自身应设为 <c>false</c>（避免自我重复注册）。
    /// </remarks>
    public bool IncludeMudHttpSources { get; set; }
}
