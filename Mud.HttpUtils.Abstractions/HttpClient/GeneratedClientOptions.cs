// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// 源生成 API 客户端的无 DI 工厂配置选项。
/// </summary>
/// <remarks>
/// <para>
/// 供 <see cref="RestService.ForGenerated{T}(HttpClient, GeneratedClientOptions?)"/> 使用，
/// 携带源生成实现类构造函数所需的可选服务依赖。
/// </para>
/// <para>
/// 所有属性均为可选（null 时使用默认实现）。AOT 场景下，建议至少提供
/// <see cref="ContentSerializer"/>（含 <c>TypeInfoResolver</c>）以确保 JSON 序列化 AOT 安全。
/// </para>
/// <para>
/// 注意：此类型位于 Abstractions 层，仅携带 Abstractions 中定义的接口。
/// <c>ILogger</c> 等需 <c>Microsoft.Extensions.Logging</c> 的依赖不在此处提供，
/// 生成实现类构造函数接受 <c>ILogger?</c> 可选参数，此处传入 null。
/// </para>
/// </remarks>
public sealed class GeneratedClientOptions
{
    /// <summary>
    /// 获取或设置 HTTP 内容序列化器。
    /// </summary>
    /// <value>HTTP 内容序列化器实例。为 null 时使用 <c>SystemTextJsonContentSerializer</c> 默认实例。</value>
    /// <remarks>
    /// AOT 场景下应提供含 <c>TypeInfoResolver</c> 的序列化器实例，确保 JSON 源生成元数据可用。
    /// </remarks>
    public IHttpContentSerializer? ContentSerializer { get; set; }

    /// <summary>
    /// 获取或设置请求拦截器。
    /// </summary>
    public IHttpRequestInterceptor? RequestInterceptor { get; set; }

    /// <summary>
    /// 获取或设置响应拦截器。
    /// </summary>
    public IHttpResponseInterceptor? ResponseInterceptor { get; set; }

    /// <summary>
    /// 获取或设置 HTTP 响应缓存提供器。
    /// </summary>
    public IHttpResponseCache? CacheProvider { get; set; }

    /// <summary>
    /// 获取或设置弹性策略解析器。
    /// </summary>
    public IResiliencePolicyResolver? ResilienceResolver { get; set; }

    /// <summary>
    /// 获取或设置敏感数据掩码器。
    /// </summary>
    public ISensitiveDataMasker? SensitiveDataMasker { get; set; }

#if NET8_0_OR_GREATER
    /// <summary>
    /// 获取或设置 Native AOT 下的 JSON 类型解析器。
    /// </summary>
    /// <value>
    /// 消费方可通过此属性编程式注入 <see cref="JsonSerializerContext"/>，
    /// 使源生成实现在 AOT 下使用源生成元数据。
    /// </value>
    public System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? JsonTypeInfoResolver { get; set; }
#endif
}
