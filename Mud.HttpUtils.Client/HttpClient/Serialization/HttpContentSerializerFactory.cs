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
/// <b>多目标框架说明</b>：<see cref="System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver"/> 等
/// resolver 类型在 netstandard2.0/net6.0 下由 System.Text.Json ≥ 8.0 NuGet 包提供（见 DefaultJsonContext.cs
/// 的 T11 修复），故合并逻辑对所有 TFM 生效；仅 <c>RuntimeFeature.IsDynamicCodeSupported</c>
/// 的 JIT/AOT 分支判断需要 net6.0+（netstandard2.0 无该属性，且不参与 Native AOT，恒走 JIT 合并路径）。
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
    /// <remarks>
    /// AOT 分支只组合源生成上下文；<c>DefaultJsonTypeInfoResolver</c> 仅在
    /// <c>RuntimeFeature.IsDynamicCodeSupported</c> 为 true 的
    /// JIT 分支实例化。Roslyn AOT 分析器不做跨运行时布尔的流分析，故此处显式压制并注明理由。
    /// 返回值始终为新实例（或安全副本），避免消费方改写共享静态状态。
    /// </remarks>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "DefaultJsonTypeInfoResolver 仅在 RuntimeFeature.IsDynamicCodeSupported==true 的 JIT 分支实例化；AOT 分支只组合源生成 context，永不执行该行。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：JIT 专属分支，Native AOT 运行时不可达。")]
#endif
    public static JsonSerializerOptions BuildOptions(
        JsonSerializerOptions? injected,
#if NET8_0_OR_GREATER
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? explicitResolver = null)
#else
        object? explicitResolver = null)
#endif
    {
        // 优先使用 EnhancedHttpClientOptions.JsonTypeInfoResolver（编程式注入）
        // 其次使用 IOptions<JsonSerializerOptions>.TypeInfoResolver（DI 注入）。
        // netstandard2.0/net6.0 下 explicitResolver 形参为 object?（公共 API 兼容），按接口强转。
        // [遗留修复] 旧实现以 #if NET8_0_OR_GREATER 包裹整个合并逻辑，net6/netstandard2.0 走 #else
        // 直接返回默认选项副本，静默丢弃消费方注入的 resolver（JsonOptionsAotTests 在 net6 上确定性失败）。
        // T11 已证明 resolver 类型由 System.Text.Json ≥ 8.0 包覆盖所有 TFM，故合并逻辑不再按 TFM 裁剪。
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? resolver =
            explicitResolver as System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver;
        resolver ??= injected?.TypeInfoResolver;

        // 库内置兜底上下文（始终包含，保证内部类型可用）
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver builtIn = MudHttpJsonContext.Default;

        if (resolver != null)
        {
            // 消费方提供了 resolver
#if NET6_0_OR_GREATER
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
#else
            // netstandard2.0：无 RuntimeFeature.IsDynamicCodeSupported，且不参与 Native AOT，恒走 JIT 合并路径
            return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
            {
                TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
                    resolver, builtIn,
                    new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver())
            };
#endif
        }

        // 未提供 resolver：
#if NET6_0_OR_GREATER
        if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported == false)
        {
            // AOT 且未提供 resolver：仅组合库内置上下文，避免回退反射
            return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
            {
                TypeInfoResolver = builtIn
            };
        }
#endif

        // JIT（或 netstandard2.0）且未提供 resolver：必须合并库内置上下文（builtIn）+ 反射兜底（DefaultJsonTypeInfoResolver）。
        // 若此处只返回无 resolver 的裸副本，库内部类型（MudHttpJsonContext 覆盖的类型）在消费方
        // 未注入 resolver 时将无法解析，默认序列化器在“零配置”场景下会退化为不可用。
        // 返回副本而非共享静态实例，避免消费方通过 Options 属性改写库级共享状态。
        return new JsonSerializerOptions(s_defaultJsonSerializerOptions)
        {
            TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
                builtIn,
                new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver())
        };
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
