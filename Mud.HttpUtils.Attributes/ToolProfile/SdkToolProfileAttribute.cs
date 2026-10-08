// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// SDK 工具生成剖面数据载体：承载工具 Schema 生成引擎所需的<b>全部</b>编译期命名事实常量。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ISdkToolProfile"/>（标记接口）成对使用：标了本特性就必须实现该接口，反之亦然，
/// 由编译期守卫诊断 <c>SDKT001</c> 强制。引擎经 <c>AttributeData</c> 读取本特性的构造参数与命名参数，
/// 产出编译期值对象 <c>SdkToolProfileModel</c>。
/// </para>
/// <para>
/// <b>为什么全部是字符串常量</b>：源生成器运行在编译器进程内，无法执行任何用户代码，
/// 只能读编译期常量。因此「策略」也只能表达为枚举值 + 分隔串标记表，而非委托。
/// </para>
/// <para>
/// 槽位完备性判据（设计文档 §4.2）：上游引擎源码中任何 <c>"Mud.Feishu…"</c> / <c>"Feishu…"</c> /
/// <c>"IFeishu…"</c> 字符串字面量都必须对应本特性的某个槽，遗漏即表现为 golden 漂移或 CS0246/CS0103。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [SdkToolProfile("Feishu",
///     SdkNamespaceRoot = "Mud.Feishu",
///     ProductPrefix = "FeishuTool",
///     DiagnosticPrefix = "MUDFT",
///     DiagnosticCategory = "MudFeishu.AI",
///     ToolAttributeName = "FeishuTool",
///     ToolAttributeNamespace = "Mud.Feishu.AI.Tools",
///     TokenKindStrategy = TokenKindDerivationStrategy.NamePrefix,
///     TokenKindMarkers = "IFeishuTenant=Tenant;IFeishuUser=User",
///     SchemaExtensionKey = "x-feishu",
///     OwnerAssembly = "Mud.Feishu.AI.FeishuTools",
///     /* …其余槽见 §4.2 清单… */)]
/// sealed class FeishuToolProfile : ISdkToolProfile { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SdkToolProfileAttribute : Attribute
{
    /// <summary>
    /// 构造剖面声明。
    /// </summary>
    /// <param name="name">SDK 名（<c>Feishu</c> / <c>Wechat</c>）；同一编译内唯一，用于多剖面排序与诊断定位。</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为 null 或空白。</exception>
    public SdkToolProfileAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("SDK 名不可为空。", nameof(name));
        }

        Name = name;
    }

    /// <summary>SDK 名（<c>Feishu</c> / <c>Wechat</c>），同一编译内唯一。</summary>
    public string Name { get; }

    // ────────── 1. 特性识别（扫描目标） ──────────

    /// <summary>工具特性简单名（如 <c>FeishuTool</c>，引擎按 <c>{Name}Attribute</c> 匹配）。</summary>
    public string ToolAttributeName { get; set; } = string.Empty;

    /// <summary>工具特性所在命名空间（如 <c>Mud.Feishu.AI.Tools</c>，防同名特性误命中）。</summary>
    public string ToolAttributeNamespace { get; set; } = string.Empty;

    /// <summary>执行器特性简单名（如 <c>FeishuToolHandler</c>）。</summary>
    public string ToolHandlerAttributeName { get; set; } = string.Empty;

    /// <summary>
    /// 执行器特性所在命名空间。
    /// </summary>
    /// <remarks>
    /// <b>不得复用 <see cref="ToolAttributeNamespace"/></b>：上游实测 handler 特性
    /// （<c>ToolHandlerScanner.AttributeNamespace = "Mud.Feishu.AI.FeishuTools"</c>）与工具特性
    /// （<c>Mud.Feishu.AI.Tools</c>）<b>不同命名空间</b>。
    /// </remarks>
    public string ToolHandlerAttributeNamespace { get; set; } = string.Empty;

    /// <summary>参数特性简单名（如 <c>ToolParameter</c>）。</summary>
    public string ParameterAttributeName { get; set; } = string.Empty;

    // ────────── 2. 源符号解析 ──────────

    /// <summary>SDK 根命名空间（<c>Mud.Feishu</c> / <c>Mud.Wechat</c>），用于 <c>Source</c> 挂钩的类型名解析。</summary>
    public string SdkNamespaceRoot { get; set; } = string.Empty;

    // ────────── 3. 接口命名范式 ──────────

    /// <summary>接口名解析正则（含 <c>domain</c>/<c>resource</c> 命名组；飞书 <c>^IFeishu(?:Tenant|User)?V\d+…$</c>）。</summary>
    public string InterfaceNameRegex { get; set; } = string.Empty;

    // ────────── 4. 令牌身份推导 ──────────

    /// <summary>令牌身份推导策略（前缀 / 后缀 / 中缀）。</summary>
    public TokenKindDerivationStrategy TokenKindStrategy { get; set; } = TokenKindDerivationStrategy.Unspecified;

    /// <summary>
    /// 「标记串=<see cref="SdkTokenKind"/> 成员名」表，以 <c>;</c> 分隔。
    /// 例：飞书 <c>"IFeishuTenant=Tenant;IFeishuUser=User"</c>；
    /// 微信 <c>"_Provider=ThirdParty;_ThirdParty=ThirdParty;_Internal=Internal"</c>。
    /// </summary>
    public string TokenKindMarkers { get; set; } = string.Empty;

    // ────────── 5-7. 产物前缀 / 诊断 / 危险词 ──────────

    /// <summary>产物前缀（<c>FeishuTool</c>），驱动 <c>{P}Schemas</c>/<c>{P}Args</c> 等产物名。</summary>
    public string ProductPrefix { get; set; } = string.Empty;

    /// <summary>复数产物前缀（<c>FeishuTools</c>，驱动 DI 装配产物名）。<b>不等于</b> <see cref="ProductPrefix"/> + "s" 的推导假设，独立成槽。</summary>
    public string ProductPluralPrefix { get; set; } = string.Empty;

    /// <summary>诊断 ID 前缀（<c>MUDFT</c> / <c>MUDWX</c>），与固定槽位序号拼成最终 ID。</summary>
    public string DiagnosticPrefix { get; set; } = string.Empty;

    /// <summary>诊断类别（<c>MudFeishu.AI</c>）。注意：兜底槽（026）用 <see cref="ToolingDiagnosticCategory"/>。</summary>
    public string DiagnosticCategory { get; set; } = string.Empty;

    /// <summary>生成器内部异常兜底诊断的类别（上游为 <c>MudFeishu.Tooling</c>，与其余诊断不同类别）。</summary>
    public string ToolingDiagnosticCategory { get; set; } = string.Empty;

    /// <summary>危险词表（<c>|</c> 分隔，snake_case 命中即 high-risk-write）。只匹配方法名，不匹配路由。</summary>
    public string WriteVerbKeywords { get; set; } = string.Empty;

    // ────────── 8-11. 描述符与契约产物事实 ──────────

    /// <summary>描述符信封的厂商扩展键（<c>x-feishu</c> / <c>x-wechat</c>）。<see cref="SchemaExtensionKey"/> 进逐字节 golden。</summary>
    public string SchemaExtensionKey { get; set; } = string.Empty;

    /// <summary>
    /// 名字契约 / Contracts / Args / DomainRegistrars 四族产物的 owner 程序集名
    /// （<c>Mud.Feishu.AI.FeishuTools</c>）。<b>不可由 <see cref="ProductPrefix"/> 推导</b>。
    /// </summary>
    public string OwnerAssembly { get; set; } = string.Empty;

    /// <summary>Schemas / Contracts 产物命名空间（<c>Mud.Feishu.AI.Tools.Generated</c>）。</summary>
    public string GeneratedNamespace { get; set; } = string.Empty;

    /// <summary>Names / Args 产物命名空间（<c>Mud.Feishu.AI.FeishuTools</c>）。</summary>
    public string ContractNamespace { get; set; } = string.Empty;

    /// <summary>域注册器命名空间（<c>Mud.Feishu.AI.FeishuTools.Registration</c>）。</summary>
    public string RegistrationNamespace { get; set; } = string.Empty;

    /// <summary>风险枚举的消费方全名（<c>Mud.Feishu.AI.Tools.FeishuToolRisk</c>），契约表字面量引用。</summary>
    public string RiskEnumFullName { get; set; } = string.Empty;

    // ────────── 12-17. 执行器 / 输出 / 聚合事实 ──────────

    /// <summary>执行器返回类型简单名（签名契约 <c>Task&lt;{ResultTypeName}&gt;</c>，如 <c>FeishuToolResult</c>）。</summary>
    public string ResultTypeName { get; set; } = string.Empty;

    /// <summary>绑定类型名（域注册器构造实参，如 <c>FeishuToolBinding</c>）。</summary>
    public string BindingTypeName { get; set; } = string.Empty;

    /// <summary>返回包装类型所在命名空间（output schema 解包表，如 <c>Mud.Feishu.DataModels</c>）。</summary>
    public string OutputWrapperNamespace { get; set; } = string.Empty;

    /// <summary>返回包装泛型类型简单名（<c>|</c> 分隔，如 <c>FeishuApiResult|FeishuApiPageListResult</c>）。</summary>
    public string OutputWrapperTypeNames { get; set; } = string.Empty;

    /// <summary>SDK 接口扫描前缀（能力目录聚合 <c>StartsWith</c> 判定，如 <c>IFeishu</c>）。</summary>
    public string SdkInterfacePrefix { get; set; } = string.Empty;

    // ────────── 开关与资产 ──────────

    /// <summary>能力目录开关的 MSBuild 属性名（如 <c>FeishuToolCatalog</c>，消费方须自行 <c>CompilerVisibleProperty</c> 注册）。</summary>
    public string CapabilityCatalogPropertyName { get; set; } = string.Empty;

    /// <summary>golden 快照文件名（<b>全名</b>匹配，如 <c>FeishuToolSchemas.golden.txt</c>；后缀匹配会误吞同工程其他 golden）。</summary>
    public string GoldenFileName { get; set; } = string.Empty;

    /// <summary>golden 更新属性名（如 <c>FeishuToolGoldenUpdate</c>，供 <c>-p:…=true</c> 重新固化）。</summary>
    public string GoldenUpdatePropertyName { get; set; } = string.Empty;

    /// <summary>guidance 资产目录片段（如 <c>/Guidance/</c>，AdditionalFiles 路径匹配）。</summary>
    public string GuidanceDirectory { get; set; } = string.Empty;
}
