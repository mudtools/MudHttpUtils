// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
/// 大多数属性为可选（null 时使用默认实现）。但 <see cref="AppContext"/> 在默认模式接口下为必需——
/// 源生成的工厂委托会在 <see cref="AppContext"/> 为 null 时抛出 <see cref="InvalidOperationException"/>，
/// 因为 <see cref="IMudAppContext"/> 没有通用默认实现。
/// </para>
/// <para>
/// AOT 场景下，建议至少提供 <see cref="ContentSerializer"/>（含 <c>TypeInfoResolver</c>）以确保 JSON 序列化 AOT 安全。
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

    /// <summary>
    /// 获取或设置应用上下文实例。
    /// </summary>
    /// <value>应用上下文实例。默认模式下为必需（为 null 时工厂委托抛出异常），因为 <see cref="IMudAppContext"/> 没有通用默认实现。</value>
    /// <remarks>
    /// <para>
    /// 仅用于源生成的默认模式接口（未声明 <c>TokenManager</c> 或 <c>HttpClient</c> 包装类型）。
    /// 消费方需自行构造 <see cref="IMudAppContext"/> 实现并赋值。
    /// </para>
    /// <para>
    /// HttpClient / TokenManager 模式接口不通过 ModuleInitializer 注册，此属性对它们无意义。
    /// </para>
    /// </remarks>
    public IMudAppContext? AppContext { get; set; }

    /// <summary>
    /// 获取或设置应用上下文持有器。
    /// </summary>
    /// <value>应用上下文持有器实例。为 null 时工厂委托创建默认 <c>AsyncLocalAppContextSwitcher</c> 实例。</value>
    /// <remarks>
    /// 仅用于源生成的默认模式接口。为 null 时由工厂委托内部创建 <c>AsyncLocalAppContextSwitcher</c>（位于 <c>Mud.HttpUtils.Client</c>）。
    /// </remarks>
    public IAppContextHolder? AppContextHolder { get; set; }

    /// <summary>
    /// 获取或设置异常擦除器（在异常传播前清除敏感数据）。
    /// </summary>
    /// <value>默认为 <c>null</c>（不执行擦除）。</value>
    public IExceptionRedactor? ExceptionRedactor { get; set; }

    /// <summary>
    /// 获取或设置错误响应体最大读取字符数（防止恶意/超大错误响应导致 OOM）。
    /// </summary>
    /// <value>默认为 <c>null</c>（无限制）。</value>
    public int? MaxExceptionContentLength { get; set; }

    /// <summary>
    /// 获取或设置是否在发送前捕获请求体字符串（用于异常调试）。
    /// </summary>
    /// <value>默认为 <c>false</c>（不捕获）。</value>
    public bool CaptureRequestContent { get; set; }

    /// <summary>
    /// 获取或设置实例级"仅生成模式"覆盖。
    /// </summary>
    /// <value>
    /// <c>null</c>（默认）= 使用全局 <see cref="RestService.GeneratedOnlyMode"/> 值；
    /// <c>true</c> = 强制仅生成模式（即使全局为 false）；
    /// <c>false</c> = 强制非仅生成模式（即使全局为 true，用于多租户隔离）。
    /// </value>
    /// <remarks>
    /// <para>
    /// 用于多租户场景下覆盖全局 <see cref="RestService.GeneratedOnlyMode"/> 标志。
    /// 例如全局开启 AOT 守护，但特定租户需要回退反射（虽然 Mud.HttpUtils 不支持反射回退，此属性保留用于未来扩展）。
    /// </para>
    /// </remarks>
    public bool? GeneratedOnlyMode { get; set; }

#if NET8_0_OR_GREATER
    /// <summary>
    /// 获取或设置 Native AOT 下的 JSON 类型解析器。
    /// </summary>
    /// <value>
    /// 消费方可通过此属性编程式注入 <c>JsonSerializerContext</c>，
    /// 使源生成实现在 AOT 下使用源生成元数据。
    /// </value>
    public System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? JsonTypeInfoResolver { get; set; }
#endif
}
