// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net.Http;

namespace Mud.HttpUtils;

/// <summary>
/// 提供源生成 API 客户端的静态工厂方法（AOT 安全，无反射回退）。
/// </summary>
/// <remarks>
/// <para>
/// 提供两种入口：
/// </para>
/// <list type="bullet">
///   <item>
///     <term><see cref="ForGenerated{T}(IServiceProvider)"/></term>
///     <description>从依赖注入容器解析源生成的 API 客户端实现。</description>
///   </item>
///   <item>
///     <term><see cref="ForGenerated{T}(HttpClient, GeneratedClientOptions?)"/></term>
///     <description>不依赖 DI 容器，直接通过 <see cref="HttpClient"/> + 可选 <see cref="GeneratedClientOptions"/> 创建（AOT 场景推荐）。</description>
///   </item>
/// </list>
/// <para>
/// 无 DI 入口通过源生成器在编译期生成的工厂委托（<see cref="RegisterGeneratedFactory{T}"/>）创建实现，
/// 可选通过 <c>[ModuleInitializer]</c> 自动注册（net5.0+）。netstandard2.0 需手动调用注册或使用 DI 入口。
/// </para>
/// <para>
/// 若接口未标记 <c>[HttpClientApi]</c> 特性或未注册源生成实现，<see cref="ForGenerated{T}"/>
/// 将抛出 <see cref="InvalidOperationException"/> 指回生成输出。
/// </para>
/// </remarks>
public static class RestService
{
    /// <summary>
    /// 缓存已解析的生成实现类型映射（预留，当前未直接使用，供未来反射场景兼容）。
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Type> _typeMapping = new();

    /// <summary>
    /// 持有源生成的实现工厂委托（无 DI 路径）。
    /// </summary>
    /// <remarks>
    /// Key 为接口类型，Value 为工厂委托（接收 HttpClient + GeneratedClientOptions，返回实现实例）。
    /// 由源生成器通过 <see cref="RegisterGeneratedFactory{T}"/> 注册。
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, Func<HttpClient, GeneratedClientOptions?, object>> _generatedFactories = new();

    /// <summary>
    /// 获取或设置是否启用「仅生成模式」守护。
    /// </summary>
    /// <value>默认 <c>false</c>。设为 <c>true</c> 时，未注册生成工厂的接口将抛出明确异常而非回退反射。</value>
    /// <remarks>
    /// <para>
    /// 用于 AOT 场景的显式守护：确保所有接口均有源生成实现，避免运行时反射回退导致 AOT 失败。
    /// </para>
    /// <para>
    /// 默认 <c>false</c> 不改变现有行为。AOT 发布场景建议设为 <c>true</c> 以在启动时即发现遗漏。
    /// </para>
    /// </remarks>
    public static bool GeneratedOnlyMode { get; set; }

    /// <summary>
    /// 注册源生成的 API 客户端工厂委托（供源生成器调用）。
    /// </summary>
    /// <typeparam name="T">标记了 <c>[HttpClientApi]</c> 特性的接口类型。</typeparam>
    /// <param name="factory">工厂委托：接收 <see cref="HttpClient"/> 与可选 <see cref="GeneratedClientOptions"/>，返回实现实例。</param>
    /// <remarks>
    /// <para>
    /// 此方法由源生成器生成的 <c>[ModuleInitializer]</c> 代码自动调用（net5.0+），
    /// 也可由消费方手动调用以支持 netstandard2.0。
    /// </para>
    /// <para>
    /// 工厂委托内部负责构造 <c>IHttpRequestExecutor</c> 等依赖，调用方无需提供。
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterGeneratedFactory<T>(
        Func<HttpClient, GeneratedClientOptions?, T> factory)
        where T : class
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        _generatedFactories[typeof(T)] = (client, options) => factory(client, options);
    }

    /// <summary>
    /// 从依赖注入容器解析源生成的 API 客户端实现。
    /// </summary>
    /// <typeparam name="T">标记了 <c>[HttpClientApi]</c> 特性的接口类型。</typeparam>
    /// <param name="serviceProvider">依赖注入服务提供者。</param>
    /// <returns>源生成的 API 客户端实例。</returns>
    /// <exception cref="InvalidOperationException">
    /// 当 <typeparamref name="T"/> 未在容器中注册时抛出。
    /// 请确保：
    /// (1) 接口标记了 <c>[HttpClientApi]</c> 特性；
    /// (2) 已调用 <c>AddMudHttpGeneratedClient&lt;T&gt;()</c> 或 <c>AddWebApiHttpClient()</c> 注册。
    /// </exception>
    public static T ForGenerated<T>(IServiceProvider serviceProvider) where T : class
    {
        if (serviceProvider == null)
            throw new ArgumentNullException(nameof(serviceProvider));

        var service = serviceProvider.GetService(typeof(T)) as T;
        if (service == null)
        {
            if (GeneratedOnlyMode)
            {
                throw new InvalidOperationException(
                    $"This client was created in generated-only mode, but no generated implementation " +
                    $"was found for '{typeof(T).FullName}'. Ensure the source generator ran and " +
                    $"ModuleInitializer registration succeeded, or call AddMudHttpGeneratedClient<T>() / " +
                    $"AddWebApiHttpClient() to register via DI.");
            }

            throw new InvalidOperationException(
                $"无法解析类型 {typeof(T).FullName}。请确保：" +
                "(1) 接口标记了 [HttpClientApi] 特性；" +
                "(2) 已调用 AddMudHttpGeneratedClient<T>() 或 AddWebApiHttpClient() 注册源生成实现。" +
                "若接口为泛型接口，当前不支持代码生成（HTTPCLIENT012）。");
        }

        return service;
    }

    /// <summary>
    /// 不依赖 DI 容器，直接通过 <see cref="HttpClient"/> 创建源生成的 API 客户端实现（AOT 安全入口）。
    /// </summary>
    /// <typeparam name="T">标记了 <c>[HttpClientApi]</c> 特性的接口类型。</typeparam>
    /// <param name="client">用于发送 HTTP 请求的 <see cref="HttpClient"/> 实例。</param>
    /// <param name="options">可选配置（携带序列化器/缓存/弹性等服务依赖）。为 null 时使用默认实现。</param>
    /// <returns>源生成的 API 客户端实例。</returns>
    /// <exception cref="InvalidOperationException">
    /// 当 <typeparamref name="T"/> 未注册生成工厂时抛出。
    /// 请确保：
    /// (1) 接口标记了 <c>[HttpClientApi]</c> 特性；
    /// (2) 源生成器已运行并生成工厂注册代码；
    /// (3) 若目标框架为 netstandard2.0，需手动调用 <see cref="RegisterGeneratedFactory{T}"/>（不支持 ModuleInitializer）。
    /// </exception>
    /// <remarks>
    /// <para>
    /// 此入口不依赖 <see cref="IServiceProvider"/>，适用于无 DI 容器或 AOT 发布场景。
    /// 工厂委托由源生成器在编译期生成，通常通过 <c>[ModuleInitializer]</c> 自动注册（net5.0+）。
    /// </para>
    /// <para>
    /// <b>使用示例</b>：
    /// <code>
    /// using var httpClient = new HttpClient { BaseAddress = new Uri("https://api.example.com") };
    /// var api = RestService.ForGenerated&lt;IUserApi&gt;(httpClient);
    /// var user = await api.GetUserAsync(1);
    /// </code>
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Source generator guarantees factory registration at compile time via ModuleInitializer or manual RegisterAllFactories() call.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "No dynamic code generation; factory delegates are compile-time generated by source generator.")]
    public static T ForGenerated<T>(HttpClient client, GeneratedClientOptions? options = null) where T : class
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (_generatedFactories.TryGetValue(typeof(T), out var factory))
        {
            return (T)factory(client, options);
        }

        // 实例级覆盖优先于全局 GeneratedOnlyMode
        var effectiveGeneratedOnly = options?.GeneratedOnlyMode ?? GeneratedOnlyMode;
        if (effectiveGeneratedOnly)
        {
            throw new InvalidOperationException(
                $"This client was created in generated-only mode, but no generated factory " +
                $"was registered for '{typeof(T).FullName}'. Ensure the source generator ran and " +
                $"ModuleInitializer registration succeeded, or manually call " +
                $"RestService.RegisterGeneratedFactory<T>() (netstandard2.0).");
        }

        throw new InvalidOperationException(
            $"No generated factory registered for type '{typeof(T).FullName}'. " +
            "Ensure that: " +
            "(1) the interface is annotated with [HttpClientApi]; " +
            "(2) the source generator ran and generated the factory registration; " +
            "(3) if targeting netstandard2.0, manually call RestService.RegisterGeneratedFactory<T>() " +
            "(ModuleInitializer is not available on netstandard2.0).");
    }
}
