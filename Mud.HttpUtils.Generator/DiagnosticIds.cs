// -----------------------------------------------------------------------
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 诊断 ID 的单一事实来源（纯字符串常量，无任何 Roslyn 依赖）。
/// </summary>
/// <remarks>
/// <para>
/// 本文件是「源码共享」文件，被两处编译：
/// <list type="number">
///   <item>由 <c>Mud.HttpUtils.Generator</c> 直接编译，供 <c>Diagnostics</c> 中的诊断描述符引用；</item>
///   <item>由 <c>Mud.HttpUtils.CodeFixes</c> 通过 <c>&lt;Compile Include="…" Link="…"/&gt;</c> 链接编译，
///         供各 <c>CodeFixProvider</c> 的 <c>FixableDiagnosticIds</c> 引用。</item>
/// </list>
/// </para>
/// <para>
/// 设计动机：代码修复器与生成器分属两个程序集（后者依赖 Microsoft.CodeAnalysis.CSharp.Workspaces，
/// 不可与编译器扩展同程序集），此前只能以字面量字符串约定诊断 ID，ID 变更时两侧静默失配。
/// 共享本文件后，ID 变更在两处编译期同时生效。
/// </para>
/// <para>
/// 约束：本文件<b>不得</b>引入任何 Roslyn 命名空间引用——它会参与编译引用
/// Workspaces 的程序集，任何 Roslyn 依赖都会破坏该程序集的加载隔离。
/// </para>
/// </remarks>
internal static class DiagnosticIds
{
    // ── HttpClient API 生成器（代码修复器需感知） ──

    /// <summary>HTTPCLIENT005：URL 模板格式无效。</summary>
    public const string HttpClientInvalidUrlTemplate = "HTTPCLIENT005";

    /// <summary>HTTPCLIENT007：HttpClient 与 TokenManage 互斥。</summary>
    public const string HttpClientAndTokenManagerMutuallyExclusive = "HTTPCLIENT007";

    /// <summary>HTTPCLIENT033：同一编译 ≥2 个 [HttpClientApi] 接口时提示命名客户端绑定脱节（G7-04a）。</summary>
    public const string HttpClientNamedClientBindingMismatch = "HTTPCLIENT033";

    /// <summary>HTTPCLIENT034：[Cache(VaryByUser=true)] 但接口无用户身份来源，键退化为 user:anonymous。</summary>
    public const string HttpClientVaryByUserWithoutIdentity = "HTTPCLIENT034";

    // ── 生成器面向消费方的 AOT 接入提示 ──

    /// <summary>MUDGEN301：IAsyncEnumerable 流式方法生成的调用恒传 null JsonTypeInfo，AOT 下需接入 JsonSerializerContext。</summary>
    public const string AsyncEnumerableAotJsonTypeInfoMissing = "MUDGEN301";

    // ── AOT JSON 序列化 ──

    /// <summary>AOT004：DTO 未被任何 JsonSerializerContext 覆盖。</summary>
    public const string AotDtoNotCoveredByContext = "AOT004";

    /// <summary>AOT005：查询参数类型使用 JSON 序列化但未被 Context 覆盖。</summary>
    public const string AotQueryParameterNotInContext = "AOT005";

    /// <summary>AOT006：[HttpJsonSerializable] 类型未被任何 JsonSerializerContext 覆盖。</summary>
    public const string AotJsonSerializableNotCovered = "AOT006";

    /// <summary>AOT007：Native AOT 上下文下使用 XML 序列化。</summary>
    public const string AotXmlNotSupported = "AOT007";

    // ── 接口规范 / DI 生命周期（MUD 系列，由本程序集内的分析器报告） ──

    /// <summary>MUD001：接口方法缺少 HTTP 方法特性。</summary>
    public const string MudMethodMissingHttpMethodAttribute = "MUD001";

    /// <summary>MUD002：接口方法返回类型不受生成器支持。</summary>
    public const string MudMethodInvalidReturnType = "MUD002";

    /// <summary>MUD004：ITokenManager 实现未注册为 Singleton。</summary>
    public const string MudNonSingletonTokenManager = "MUD004";

    /// <summary>MUD005：Token 注入模式使用 Query（令牌进入 URL，代理/访问日志/浏览器历史不可控）。</summary>
    public const string MudQueryTokenInjectionMode = "MUD005";

    // ── 载荷字段映射生成器（PAYLOAD*，由 PayloadFieldMapGenerator 报告） ──

    /// <summary>PAYLOAD001：载荷字段映射生成内部错误（生成器兜底，带 NotConfigurable）。</summary>
    public const string PayloadGenerationError = "PAYLOAD001";

    /// <summary>PAYLOAD002：载荷契约类必须为 partial。</summary>
    public const string PayloadContractTypeNotPartial = "PAYLOAD002";

    /// <summary>PAYLOAD003：未指定 Converter 类型。</summary>
    public const string PayloadConverterNotSpecified = "PAYLOAD003";

    /// <summary>PAYLOAD004：转换器上找不到指定的转换方法。</summary>
    public const string PayloadConverterMethodNotFound = "PAYLOAD004";

    /// <summary>PAYLOAD005：转换方法返回值不可赋给目标属性。</summary>
    public const string PayloadConverterReturnTypeMismatch = "PAYLOAD005";

    /// <summary>PAYLOAD006：字段映射声明非法。</summary>
    public const string PayloadFieldDeclarationInvalid = "PAYLOAD006";

    /// <summary>PAYLOAD007：无法按属性类型推断字段形态。</summary>
    public const string PayloadFieldFormatNotInferable = "PAYLOAD007";

    /// <summary>PAYLOAD008：手写 PayloadFieldMap 与 [PayloadContract] 冲突。</summary>
    public const string PayloadHandwrittenMapConflict = "PAYLOAD008";

    /// <summary>PAYLOAD009：载荷契约类形态不受支持（泛型 / 嵌套 / record / 非 class）。</summary>
    public const string PayloadContractTypeShapeUnsupported = "PAYLOAD009";
}
