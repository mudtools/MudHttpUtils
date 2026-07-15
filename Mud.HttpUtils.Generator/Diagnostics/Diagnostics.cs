// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 统一诊断描述符集合，用于集中管理所有代码生成过程中的诊断信息
/// <para>错误码命名规范：前缀(3-6字符) + 三位数字 (如 DTO001, HTTPCLIENT001)</para>
/// <para>所有生成器必须使用此统一类中的诊断描述符，确保错误码唯一且格式一致</para>
/// </summary>
internal static class Diagnostics
{
    #region 错误码前缀规范说明
    /*
     * 错误码命名规范：
     * - 前缀使用大写字母，长度3-6字符
     * - 后缀使用3位数字 (001-999)
     * - 前缀分配：
     *   - HTTPCLIENT*: HttpClient API生成器 (HTTPCLIENT001-005)
     *   - HTTPCLIENTREG*: HttpClient注册生成器 (HTTPCLIENTREG001-002)
     *   - MCG*: 通用代码生成器 (MCG001-002)
     *   - EHSG*: 事件处理器生成器 (EHSG001)
     *   - COMWRAP*: COM包装生成器 (COMWRAP001)
     *   - SG*: 源代码生成器通用 (SG001)
     *   - EG*: 实体生成器通用 (EG001-002)
     *   - AOT*: AOT JSON 序列化诊断 (AOT001-005)
     */
    #endregion 

    #region HttpClient API生成器诊断信息 (HTTPCLIENT001-005)
    public static readonly DiagnosticDescriptor HttpClientApiGenerationError = new(
        id: "HTTPCLIENT001",
        title: "HttpClient API生成错误",
        messageFormat: "生成接口 {0} 的实现时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientApiSyntaxError = new(
        id: "HTTPCLIENT003",
        title: "HttpClient API语法错误",
        messageFormat: "接口 {0} 的语法分析失败: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientApiParameterError = new(
        id: "HTTPCLIENT004",
        title: "HttpClient API参数错误",
        messageFormat: "接口 {0} 的参数配置错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientInvalidUrlTemplate = new(
        id: "HTTPCLIENT005",
        title: "Invalid URL Template",
        messageFormat: "接口 {0} 的URL模板 '{1}' 格式无效: {2}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientAndTokenManagerMutuallyExclusive = new(
        id: "HTTPCLIENT007",
        title: "HttpClient 与 TokenManage 互斥",
        messageFormat: "接口 {0} 同时指定了 HttpClient 和 TokenManage 属性，两者互斥。请只设置其中一个。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientEncryptNotSupported = new(
        id: "HTTPCLIENT008",
        title: "HttpClient 类型不支持加密",
        messageFormat: "接口 {0} 的方法 {1} 启用了加密（EnableEncrypt=true），但指定的 HttpClient 类型 '{2}' 未实现 IEncryptableHttpClient 接口。请使用同时实现了 IEncryptableHttpClient 的类型（如 IEnhancedHttpClient），或移除加密配置。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientXmlNotSupported = new(
        id: "HTTPCLIENT009",
        title: "HttpClient 类型不支持 XML 请求",
        messageFormat: "接口 {0} 的方法 {1} 需要 XML 请求/响应，但指定的 HttpClient 类型 '{2}' 未实现 IXmlHttpClient 接口。请使用同时实现了 IXmlHttpClient 的类型（如 IEnhancedHttpClient），或修改方法的内容类型配置。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientApiBaseAddressObsolete = new(
        id: "HTTPCLIENT010",
        title: "HttpClientApiAttribute.BaseAddress 已弃用",
        messageFormat: "接口 {0} 使用了 HttpClientApiAttribute 的 BaseAddress 参数，该参数已弃用。请改用 AddMudHttpClient(clientName, baseAddress) 配置基地址。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CacheWithResponseTypeWarning = new(
        id: "HTTPCLIENT011",
        title: "Cache 与 Response<T> 返回类型组合使用警告",
        messageFormat: "接口 {0} 的方法 {1} 同时使用了 [Cache] 特性和 Response<T> 返回类型。缓存会存储整个 Response<T> 对象（包括 StatusCode 和 Headers），可能导致后续请求返回过期的状态码和响应头。建议使用普通返回类型或移除缓存。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientApiGenericInterfaceNotSupported = new(
    id: "HTTPCLIENT012",
    title: "泛型接口代码生成",
    messageFormat: "接口 {0} 是泛型接口，源生成器将转发类型参数与约束。",
    category: "代码生成",
    DiagnosticSeverity.Info,
    isEnabledByDefault: true,
    customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientPathParameterMismatch = new(
        id: "HTTPCLIENT013",
        title: "路径参数不匹配",
        messageFormat: "接口 {0} 的方法 {1} 的 URL 模板 '{2}' 中的路径参数与方法的 [Path] 参数不匹配。{3}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientTypeNotFound = new(
        id: "HTTPCLIENT014",
        title: "HttpClient 类型未找到",
        messageFormat: "接口 {0} 指定的 HttpClient 类型 '{1}' 在当前编译中未找到。请确认类型名称是否正确，或确保已通过 AddMudHttpClient 注册了对应的命名客户端。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TokenManagerTypeNotFound = new(
        id: "HTTPCLIENT015",
        title: "TokenManage 类型未找到",
        messageFormat: "接口 {0} 的 TokenManage 属性指定的类型 '{1}' 在当前编译中未找到。请确认类型名称正确，或确保包含该类型的项目已正确引用。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor TokenManagerMissingMethod = new(
        id: "HTTPCLIENT016",
        title: "TokenManage 类型缺少必需方法",
        messageFormat: "接口 {0} 的 TokenManage 属性指定的类型 '{1}' 缺少必需的方法 '{2}'。TokenManage 类型必须提供 'IMudAppContext GetDefaultApp()' 和 'IMudAppContext GetApp(string appKey)' 方法，或实现 IAppManager<TAppContext> 接口。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientTypeUnresolved = new(
        id: "HTTPCLIENT017",
        title: "HttpClient 类型无法解析，兼容性校验被跳过",
        messageFormat: "接口 {0} 的方法 {1} 指定的 HttpClient 类型 '{2}' 无法在编译中解析，加密/XML 兼容性校验已被跳过。请确保类型名称正确（使用完全限定名），否则可能导致生成代码在运行时失败。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TokenManagerKeyInferredFromDefault = new(
        id: "HTTPCLIENT018",
        title: "TokenManagerKey 使用默认推断值",
        messageFormat: "接口 {0} 未显式指定 TokenManagerKey 或 TokenType，生成器将使用默认值 '{1}'。在多接口共享同一 TokenManager 的场景下，可能导致令牌管理器注册冲突。如需隔离不同接口的令牌，请通过 [Token] 特性显式指定 TokenManagerKey 或 TokenType。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // NEW-GEN-03/08 修复：CacheAttribute 的 UseSlidingExpiration 和 Priority 属性当前被生成器忽略。
    // 当用户显式设置这些属性时，发出信息性诊断提示用户这些配置不会生效。
    public static readonly DiagnosticDescriptor CacheAttributePropertyIgnored = new(
        id: "HTTPCLIENT019",
        title: "CacheAttribute 属性被生成器忽略",
        messageFormat: "接口 {0} 的方法 {1} 设置了 [Cache] 特性的 {2} 属性，但该属性当前被生成器忽略，不会在生成的代码中生效。如需此功能，请关注后续版本更新。",
        category: "代码生成",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);
    #endregion

    #region HttpClient注册生成器诊断信息 (HTTPCLIENTREG001-002)
    public static readonly DiagnosticDescriptor HttpClientRegistrationGenerationError = new(
        id: "HTTPCLIENTREG001",
        title: "HttpClient API注册生成错误",
        messageFormat: "为接口 {0} 生成HttpClient API注册时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor HttpClientInvalidRegistryGroupName = new(
        id: "HTTPCLIENTREG002",
        title: "Invalid RegistryGroupName",
        messageFormat: "RegistryGroupName '{0}' 不是有效的C#标识符。RegistryGroupName必须以字母或下划线开头，只能包含字母、数字和下划线。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);
    #endregion

    #region 事件处理器生成器诊断信息 (EHSG001)
    public static readonly DiagnosticDescriptor EventHandlerGenerationError = new(
        id: "EHSG001",
        title: "事件处理器生成器错误",
        messageFormat: "为类 {0} 生成事件处理器代码时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);
    #endregion  

    #region FormContent生成器诊断信息 (FORM001-003)
    public static readonly DiagnosticDescriptor FormContentGenerationError = new(
        id: "FORM001",
        title: "FormContent代码生成错误",
        messageFormat: "为类 {0} 生成FormContent代码时发生错误: {1}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor FormContentNoFilePathAttribute = new(
        id: "FORM002",
        title: "FormContent缺少FilePath属性",
        messageFormat: "类 {0} 标记了 [FormContent] 特性，但没有找到任何标记了 [FilePath] 特性的属性。必须且只能有一个属性标记 [FilePath] 特性。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    public static readonly DiagnosticDescriptor FormContentMultipleFilePathAttributes = new(
        id: "FORM003",
        title: "FormContent存在多个FilePath属性",
        messageFormat: "类 {0} 标记了 [FormContent] 特性，但发现了多个标记了 [FilePath] 特性的属性: {1}。必须且只能有一个属性标记 [FilePath] 特性。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);
    #endregion

    #region AOT JSON 序列化诊断信息 (AOT001-005)
    // 诊断由 HttpJsonContextScaffolder（pre-build 工具）或独立分析器报告。
    // 前缀 AOT 遵循仓库 XXXNNN 约定（3-6 字符前缀 + 3 位数字）。

    public static readonly DiagnosticDescriptor AotDuplicateSerializerClassName = new(
        id: "AOT001",
        title: "AOT JSON Context 类名冲突",
        messageFormat: "SerializerClassName '{0}' 存在冲突的 NamingPolicy 配置。同一 Context 内只能使用一个命名策略。建议统一配置或拆分为不同分组。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AotOpenGenericOnLegacyTfm = new(
        id: "AOT002",
        title: "开放泛型类型在低版本 TFM 上标注 [HttpJsonSerializable]",
        messageFormat: "类型 '{0}' 是开放泛型，在 net8.0 以下不支持源生成开放泛型。低版本将走反射兜底，AOT 下不可用。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AotPolymorphismWithoutJsonDerivedType = new(
        id: "AOT003",
        title: "多态类型缺少 [JsonDerivedType] 标注",
        messageFormat: "类型 '{0}' 存在基类（多态序列化），但未标注 [JsonDerivedType]。以基类反序列化派生类时源生成不含派生类型，可能丢字段。建议在同程序集内补充 [JsonDerivedType] 或由 Scaffolder 自动补全。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AotDtoNotCoveredByContext = new(
        id: "AOT004",
        title: "HttpClient API 方法的 DTO 未被任何 JsonSerializerContext 覆盖",
        messageFormat: "接口 {0} 的方法 {1} 使用的请求/响应 DTO '{2}' 未被任何已引用的 JsonSerializerContext 覆盖。AOT 下序列化将抛 NotSupportedException。修复：在 DTO 类型上标注 [HttpJsonSerializable] 并运行 'dotnet mud-jsonctx --project <path>' 生成上下文，或将类型手动加入现有 JsonSerializerContext。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AotQueryParameterNotInContext = new(
        id: "AOT005",
        title: "查询参数类型使用 JSON 序列化但未被 Context 覆盖",
        messageFormat: "接口 {0} 的方法 {1} 的查询参数 '{2}' 标注了 JSON 序列化，但其类型 '{3}' 未被任何 JsonSerializerContext 覆盖。AOT 下查询参数 JSON 序列化可能失败。建议将此类型纳入 JsonSerializerContext 或实现 IQueryParameter 接口。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AotJsonSerializableNotCovered = new(
        id: "AOT006",
        title: "[HttpJsonSerializable] 类型未被任何 JsonSerializerContext 覆盖",
        messageFormat: "类型 '{0}' 标注了 [HttpJsonSerializable]，但未被任何已引用的 JsonSerializerContext 覆盖。若未运行 HttpJsonContextScaffolder 或将其纳入手写 JsonSerializerContext，AOT 下序列化可能返回空对象或失败。请运行 `dotnet mud-jsonctx` 或将此类型加入 JsonSerializerContext。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// AOT007：Native AOT 上下文下使用 XML 序列化。
    /// <para>
    /// <c>XmlSerializer</c> 构造函数在 .NET 7+ Native AOT 下需要动态代码生成，
    /// 会在类首次访问时抛 <see cref="System.PlatformNotSupportedException"/>。
    /// </para>
    /// <para>
    /// 语义说明：AOT007 <b>仅</b>在 AOT 上下文下报告（分析器通过 isAotEnabled 提前 return）。
    /// 非 AOT 项目（未设置 IsAotCompatible=true 且未设置 PublishAot=true）即使引用了本诊断分析器，
    /// 也不会收到 AOT007 错误，因此不会阻止非 AOT 项目使用 XML 序列化。这是设计意图，
    /// 不应被误读为"全局阻止 XML 使用"——XML 路径在 JIT/非 AOT 部署场景仍完全可用。
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor AotXmlNotSupportedInAot = new(
        id: "AOT007",
        title: "XML 序列化在 Native AOT 下不支持",
        messageFormat: "接口 {0} 的方法 {1} 使用 XML 序列化，Native AOT 下 XmlSerializer 需要动态代码生成，会在运行时抛 PlatformNotSupportedException。请改用 [SerializationMethod(SerializationMethod.Json)]，或在非 AOT 部署场景使用 XML。",
        category: "AOT",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "XmlSerializer 在 Native AOT 下不支持。请将方法改为 JSON 序列化，或在非 AOT 部署场景使用 XML。此诊断仅在 AOT 上下文（IsAotCompatible=true 或 PublishAot=true）下报告。",
        helpLinkUri: "https://learn.microsoft.com/dotnet/core/deploying/native-aot");
    #endregion
}
