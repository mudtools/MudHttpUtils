// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// JSON 序列化选项合并工厂。集中管理 <see cref="JsonSerializerOptions"/> 的构建逻辑，
/// 确保库内置 <c>MudHttpJsonContext.Default</c> 在所有路径（DI 默认注册、基类兜底、执行器兜底）中一致合并。
/// </summary>
/// <remarks>
/// <para>
/// 此工厂等价于原 <c>EnhancedHttpClient.BuildJsonOptions</c> 的逻辑，但作为共享工厂供 DI 注册、
/// <see cref="EnhancedHttpClient"/> 构造兜底、<see cref="DefaultHttpRequestExecutor"/> 兜底统一调用。
/// </para>
/// <para>
/// <b>多目标框架守卫</b>：<see cref="System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver"/>
/// 仅在 .NET 8+ 可用，<c>MudHttpJsonContext</c> 仅在 .NET 8+ 可用。
/// 本项目目标框架为 <c>netstandard2.0;net6.0;net8.0;net10.0</c>，
/// 故 resolver 合并逻辑用 <c>#if NET8_0_OR_GREATER</c> 条件编译包裹。
/// </para>
/// </remarks>
public static class HttpContentSerializerFactory
{
    private static readonly JsonSerializerOptions s_defaultJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 构建默认 JSON 序列化选项：合并 消费方 resolver（IOptions 或显式）+ 库内置 MudHttpJsonContext.Default +（JIT）反射兜底。
    /// 等价于原 EnhancedHttpClient.BuildJsonOptions，但作为共享工厂供 DI 与基类统一调用。
    /// </summary>
    /// <param name="injected">消费方通过 DI（<c>IOptions&lt;JsonSerializerOptions&gt;</c>）或编程式注入的选项。</param>
    /// <param name="explicitResolver">编程式注入的类型解析器（来自 <c>EnhancedHttpClientOptions.JsonTypeInfoResolver</c>）。</param>
    /// <returns>合并后的 <see cref="JsonSerializerOptions"/> 实例。</returns>
    public static JsonSerializerOptions BuildOptions(
        JsonSerializerOptions? injected,
#if NET8_0_OR_GREATER
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? explicitResolver = null)
#else
        object? explicitResolver = null)
#endif
    {
#if NET8_0_OR_GREATER
        // 优先使用 EnhancedHttpClientOptions.JsonTypeInfoResolver（编程式注入）
        // 其次使用 IOptions<JsonSerializerOptions>.TypeInfoResolver（DI 注入）
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? resolver = explicitResolver;
        resolver ??= injected?.TypeInfoResolver;

        // 库内置兜底上下文（始终包含，保证内部类型可用）
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver builtIn = MudHttpJsonContext.Default;

        if (resolver != null)
        {
            // 消费方提供了 resolver
            if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                // JIT：再 Combine 一个 DefaultJsonTypeInfoResolver 作反射兜底，兼容未声明类型
                return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
                {
                    TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
                        resolver, builtIn,
                        new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver())
                };
            }
            // AOT：仅源生成，杜绝静默回退反射
            return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
            {
                TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(resolver, builtIn)
            };
        }

        // 未提供 resolver：
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported == false)
        {
            // AOT 且未提供 resolver：用库内置上下文，避免回退反射
            return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
            {
                TypeInfoResolver = builtIn
            };
        }
        // JIT 且未提供 resolver：保留 s_defaultJsonSerializerOptions，默认走反射（DefaultJsonTypeInfoResolver）
#endif
        return s_defaultJsonSerializerOptions;
    }

    /// <summary>
    /// 未注入序列化器时创建默认实例。内部调用 <see cref="BuildOptions"/> 合并库内置 <c>MudHttpJsonContext.Default</c>。
    /// </summary>
    /// <param name="injected">消费方通过 DI 注入的选项。</param>
    /// <param name="explicitResolver">编程式注入的类型解析器。</param>
    /// <returns>带合并 options 的 <see cref="SystemTextJsonContentSerializer"/> 实例。</returns>
    public static IHttpContentSerializer CreateDefault(
        JsonSerializerOptions? injected = null,
#if NET8_0_OR_GREATER
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? explicitResolver = null)
#else
        object? explicitResolver = null)
#endif
        => new SystemTextJsonContentSerializer(BuildOptions(injected, explicitResolver));
}
