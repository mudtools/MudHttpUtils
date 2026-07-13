// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标注在需纳入 JSON 源生成的实体/DTO 上（支持 Native AOT）。
/// Scaffolder 会按其聚合为 <c>JsonSerializerContext</c> 继承类（真实 .cs 文件）。
/// </summary>
/// <remarks>
/// <para>
/// 此特性仅用于编译期标记，消费方通过 <c>HttpJsonContextScaffolder</c> 工具扫描标注类型并产出
/// <c>JsonSerializerContext</c> 源文件（<c>#if NET8_0_OR_GREATER</c> 包裹）。
/// STJ 原生源生成器处理该文件后，产出 <c>Default</c> 实例与类型元数据。
/// </para>
/// <para>
/// 消费方将生成的 <c>XxxJsonContext.Default</c> 注入 <c>EnhancedHttpClientOptions.JsonTypeInfoResolver</c>
/// 或通过 <c>services.Configure&lt;JsonSerializerOptions&gt;(o =&gt; o.TypeInfoResolver = ...)</c> 注入。
/// 库内置 <c>BuildJsonOptions</c> 会自动合并消费方 resolver 与库内置 <c>MudHttpJsonContext.Default</c>。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpJsonSerializable(SerializerClassName = "FeishuAI", NamingPolicy = JsonNamingPolicyHint.SnakeCaseLower)]
/// public class ContractFileUploadRequest { ... }
/// </code>
/// </example>
// Note: AttributeTargets.Record (0x40) is only available in .NET 5+.
// In netstandard2.0, records compile as classes, so AttributeTargets.Class covers them.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class HttpJsonSerializableAttribute : Attribute
{
    /// <summary>
    /// 生成的 Context 类名（不含 "JsonContext" 后缀）。
    /// </summary>
    /// <remarks>
    /// 留空则由 Scaffolder 按 "程序集缩写 + 命名空间" 自动派生，确保唯一。
    /// 同一 <c>SerializerClassName</c> 的实体合并进同一个 Context。
    /// </remarks>
    public string? SerializerClassName { get; set; }

    /// <summary>
    /// JSON 命名策略。设为 <see cref="JsonNamingPolicyHint.Default"/> 时由 Scaffolder 自动推导：
    /// <list type="number">
    ///   <item>程序集中超过 50% 的实体使用 <c>[JsonPropertyName]</c> 且符合 SnakeCaseLower 模式 → 自动选 SnakeCaseLower</item>
    ///   <item>否则默认 CamelCase（与库 <c>s_defaultJsonSerializerOptions</c> 一致）</item>
    /// </list>
    /// 显式指定将覆盖自动推导。
    /// </summary>
    public JsonNamingPolicyHint NamingPolicy { get; set; } = JsonNamingPolicyHint.Default;
}

/// <summary>
/// JSON 命名策略提示，用于 Scaffolder 自动配置 <c>JsonSourceGenerationOptions.PropertyNamingPolicy</c>。
/// 不显式指定时，Scaffolder 自动推导。
/// </summary>
public enum JsonNamingPolicyHint
{
    /// <summary>
    /// 自动推导（默认）。Scaffolder 检测实体上的 <c>[JsonPropertyName]</c> 模式决定策略。
    /// </summary>
    Default,

    /// <summary>
    /// 驼峰命名（如 <c>propertyName</c>），与库默认 <c>JsonNamingPolicy.CamelCase</c> 一致。
    /// </summary>
    CamelCase,

    /// <summary>
    /// 小写下划线命名（如 <c>property_name</c>），对应 <c>JsonNamingPolicy.SnakeCaseLower</c>。
    /// 常用于飞书等 API 的 JSON 合约。
    /// </summary>
    SnakeCaseLower,

    /// <summary>
    /// 大写下划线命名（如 <c>PROPERTY_NAME</c>），对应 <c>JsonNamingPolicy.SnakeCaseUpper</c>。
    /// </summary>
    SnakeCaseUpper,

    /// <summary>
    /// 小写短横线命名（如 <c>property-name</c>），对应 <c>JsonNamingPolicy.KebabCaseLower</c>。
    /// </summary>
    KebabCaseLower,

    /// <summary>
    /// 大写短横线命名（如 <c>PROPERTY-NAME</c>），对应 <c>JsonNamingPolicy.KebabCaseUpper</c>。
    /// </summary>
    KebabCaseUpper
}
