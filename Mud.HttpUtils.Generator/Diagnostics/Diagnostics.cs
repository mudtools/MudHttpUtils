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
    #region 诊断标签分层准则（WellKnownDiagnosticTags.NotConfigurable）
    /*
     * NotConfigurable 标签的语义（含一个必须避开的编译期"连坐"效应）：
     *
     * 1. 语义：标记为 NotConfigurable 的诊断"不可配置"——既不能被 #pragma / NoWarn /
     *    .editorconfig 抑制或关闭，也不能被改变级别。用于表达"构建契约"性质的诊断。
     *
     * 2. 连坐效应（实测 + Roslyn 源码定位）：
     *    csc 的 CommonCompiler.CompileAndEmit 在 Parse 与 Declare 两个阶段各有一道闸门：
     *        if (HasUnsuppressableErrors(diagnostics)) { ... return; }
     *    其中 Diagnostic.IsUnsuppressableError() := DefaultSeverity == Error && IsNotConfigurable()，
     *    而 IsNotConfigurable() 即"CustomTags 含 NotConfigurable"。
     *    声明阶段（Declare）闸门在"源生成器已运行、生成器诊断已并入同一 DiagnosticBag"之后求值，
     *    因此只要存在一条 **默认级别为 Error 且带 NotConfigurable 的生成器诊断**，csc 便提前 return，
     *    分析器驱动（AnalyzerDriver）的 GetDiagnosticsAsync 永不被调用 —— 同一编译中的
     *    **全部分析器诊断**（MUD001/MUD002/MUD004）整体不再呈现。
     *    （注：该闸门只覆盖 Parse/Declare 阶段；方法体内的绑定错误如 CS0029 属 Compile 阶段，
     *      不参与该判定，故"无关编译错误"不会触发连坐。而 CS0535 属声明阶段错误，会触发。）
     *
     * 3. 判定准则（新增诊断必须遵循）：
     *    NotConfigurable 仅用于**使用者无法通过修改自身源码/配置按预期修复的错误**
     *    （生成器内部异常、语法损坏、注册阶段内部失败）。
     *    凡"使用者改一行代码即可修复"的诊断（参数修饰符、URL 模板、互斥配置、类型名/方法缺失、
     *    特性标记缺失等），**不加**该标签，以保证同编译中的分析器诊断（MUD*）不被连坐抑制。
     *
     * 4. 去标签**不降级**：被去标签的诊断级别仍保持 Error（默认行为不变，仍阻断构建），
     *    只是"允许"使用者显式抑制。请勿顺手降级为 Warning —— 那会把编译期失败变成运行期故障。
     *
     * 守卫：Tests/Mud.HttpUtils.Generator.Tests/DiagnosticTagPolicyTests.cs 断言
     * "Error + NotConfigurable" 的集合恰好等于内部/环境类错误白名单（本文件中即为
     * HTTPCLIENT001、HTTPCLIENT003、HTTPCLIENTREG001、EHSG001、FORM001）。
     *
     * 详见 .docs/生成器诊断治理与返回类型完善方案v1.md §1.2 / §3.3。
     */
    #endregion

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
     *   - AOT*: AOT JSON 序列化诊断 (AOT001-007)
     *   - MUD*: 接口规范 / DI 生命周期分析器诊断 (MUD001/MUD002/MUD004)
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
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientInvalidUrlTemplate = new(
        id: DiagnosticIds.HttpClientInvalidUrlTemplate,
        title: "Invalid URL Template",
        messageFormat: "接口 {0} 的URL模板 '{1}' 格式无效: {2}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientAndTokenManagerMutuallyExclusive = new(
        id: DiagnosticIds.HttpClientAndTokenManagerMutuallyExclusive,
        title: "HttpClient 与 TokenManage 互斥",
        messageFormat: "接口 {0} 同时指定了 HttpClient 和 TokenManage 属性，两者互斥。请只设置其中一个。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientEncryptNotSupported = new(
        id: "HTTPCLIENT008",
        title: "HttpClient 类型不支持加密",
        messageFormat: "接口 {0} 的方法 {1} 启用了加密（EnableEncrypt=true），但指定的 HttpClient 类型 '{2}' 未实现 IEncryptableHttpClient 接口。请使用同时实现了 IEncryptableHttpClient 的类型（如 IEnhancedHttpClient），或移除加密配置。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientXmlNotSupported = new(
        id: "HTTPCLIENT009",
        title: "HttpClient 类型不支持 XML 请求",
        messageFormat: "接口 {0} 的方法 {1} 需要 XML 请求/响应，但指定的 HttpClient 类型 '{2}' 未实现 IXmlHttpClient 接口。请使用同时实现了 IXmlHttpClient 的类型（如 IEnhancedHttpClient），或修改方法的内容类型配置。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // CFG-23：原 HTTPCLIENT010（HttpClientApiAttribute.BaseAddress 已弃用）为死诊断 ——
    // 该属性/构造函数已标注 [Obsolete(error: true)]，使用处直接产生编译错误 CS0619，生成器无需重复提示。
    // 描述符已删除，ID HTTPCLIENT010 保留为未使用占位（不重新分配）。

    public static readonly DiagnosticDescriptor CacheWithResponseTypeWarning = new(
        id: "HTTPCLIENT011",
        title: "Cache 与 Response<T> 返回类型组合使用警告",
        messageFormat: "接口 {0} 的方法 {1} 同时使用了 [Cache] 特性和 Response<T> 返回类型。缓存会存储整个 Response<T> 对象（包括 StatusCode 和 Headers），可能导致后续请求返回过期的状态码和响应头。建议使用普通返回类型或移除缓存。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// 直达返回类型（<c>HttpResponseMessage</c> / <c>Stream</c>）与 Cache/Resilience 编排组合时报告。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 直达返回绕过 <c>IHttpRequestExecutor</c>，直接调用客户端原始 API
    /// （<c>SendRawAsync</c> / <c>SendStreamAsync</c>），故 Cache / Retry / CircuitBreaker / Timeout
    /// 等编排配置**不会生效**。这在语义上是刻意的（用户选择直达返回即表明自管后续逻辑），
    /// 但"配置静默失效"必须编译期可见 —— 否则用户会误以为 <c>[Cache]</c> 已生效。
    /// </para>
    /// <para>
    /// 级别为 Warning（非 Error）：代码可编译且语义正确，仅编排未按预期参与，不影响既有工程构建。
    /// 不加 <see cref="WellKnownDiagnosticTags.NotConfigurable"/>（用户可通过改返回类型修复，
    /// 且需要避免连坐抑制分析器诊断，见本文件顶部的标签分层准则）。
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor CacheWithDirectReturnTypeWarning = new(
        id: "HTTPCLIENT025",
        title: "直达返回类型不参与 Cache/Resilience 编排",
        messageFormat: "接口 {0} 的方法 {1} 的返回类型 '{2}' 属直达返回（HttpResponseMessage/Stream/IAsyncEnumerable<T> 流式返回），生成代码将绕过请求执行器直接调用客户端 API，因此 [Cache]、[Retry]、[CircuitBreaker]、[Timeout] 等编排配置不会生效。如需缓存/弹性编排，请改用 Task<T> 等普通响应体返回类型。",
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

    /// <summary>
    /// 文件下载方法（<c>[FilePath]</c>）与 <c>[Cache]</c> 组合时报告。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 文件下载（<c>DownloadLargeAsync</c> 编排）语义是"写入本地文件"，不存在可复用的响应体，
    /// 故缓存对文件下载不适用。此警告使"<c>[Cache]</c> 静默失效"编译期可见。
    /// 级别为 Warning（非 Error）：代码可编译且语义正确，仅缓存未参与，不影响既有工程构建。
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor CacheWithFileDownloadWarning = new(
        id: "HTTPCLIENT030",
        title: "[Cache] 不适用于文件下载方法",
        messageFormat: "接口 {0} 的方法 {1} 使用了 [Cache] 特性，但其返回类型为文件下载（[FilePath]），文件下载已写入本地文件且不存在可复用的响应体，缓存不会生效。请移除 [Cache]。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// M5-HC-04：[Cache] 方法的默认缓存键包含无法稳定表达的参数（复杂对象/[Body]/数组元素非标量等）。
    /// 无 CacheKeyTemplate 时报告 Error —— 静默串键比编译失败更危险。
    /// </summary>
    public static readonly DiagnosticDescriptor CacheKeyUnsafeParameterError = new(
        id: "HTTPCLIENT031",
        title: "[Cache] 方法的缓存键包含无法稳定表达的参数",
        messageFormat: "接口 {0} 的方法 {1} 使用了 [Cache]，但参数 '{2}'（类型 {3}）无法稳定映射为缓存键。默认键会退化为类型名，导致不同请求命中同一缓存并返回错误数据。请改用 [Cache(..., CacheKeyTemplate = \"…\")] 显式声明键模板，或移除 [Cache]。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// M5-HC-04：CacheKeyTemplate 未引用某 Unsafe 参数，可能串键（尽力而为的字面检查）。
    /// </summary>
    public static readonly DiagnosticDescriptor CacheKeyTemplateMissingParameterWarning = new(
        id: "HTTPCLIENT032",
        title: "[Cache] CacheKeyTemplate 未覆盖参数",
        messageFormat: "接口 {0} 的方法 {1} 的 CacheKeyTemplate 未引用参数 '{2}'，不同取值可能命中同一缓存。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// F-03：[Cache(VaryByUser=true)] 但接口既未继承 ICurrentUserId 也无 [Token(RequiresUserId=true)]，
    /// 用户维度退化为 "user:anonymous"（全体用户共享同一缓存）。
    /// <para>
    /// 级别为 Warning（非 Error）：实现类的可写属性 CurrentUserId 是合法的宿主手动赋值通道，
    /// 属「可用但易错」路径，故仅编译期提示，不阻断构建。
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor VaryByUserWithoutIdentitySourceWarning = new(
        id: DiagnosticIds.HttpClientVaryByUserWithoutIdentity,
        title: "[Cache] VaryByUser 缺少用户身份来源",
        messageFormat: "接口 {0} 的方法 {1} 启用了 Cache(VaryByUser=true)，但接口未继承 ICurrentUserId 且无 [Token(RequiresUserId=true)]，缓存键将退化为 \"user:anonymous\"（全体用户共享）。请为接口继承 ICurrentUserId、为方法添加 [Token(RequiresUserId=true)]，或移除 VaryByUser。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// G7-04a：同一编译 ≥2 个 <c>[HttpClientApi]</c> 接口共存时，提示命名客户端与实现类解析脱节。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 注册端生成 <c>AddMudHttpClient("{Interface}_HttpClient", ...)</c> 命名客户端，但实现类构造函数
    /// 注入的是<b>类型级</b> <c>IEnhancedHttpClient</c> / <c>IHttpRequestExecutor</c>（<c>RegisterNamedClient</c>
    /// 以 <c>TryAddTransient</c> 注册，先注册者胜）。因此各接口的命名客户端与 <c>[HttpClientApi(Timeout)]</c>
    /// 配置仅在对应名称被注册为默认 <c>IEnhancedHttpClient</c> 时才生效，多接口场景下不按命名隔离。
    /// </para>
    /// <para>
    /// 级别为 Info（非 Error/Warning）：代码可编译且多数单接口场景语义正确，提示语引导阅读 README；
    /// 仅当同一编译 ≥2 个接口时报告，避免噪音。<b>不加</b> <see cref="WellKnownDiagnosticTags.NotConfigurable"/>
    /// （用户可通过注册命名客户端为默认实例修复，且需避免连坐抑制分析器诊断，见本文件顶部标签分层准则）。
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor HttpClientNamedClientBindingMismatch = new(
        id: DiagnosticIds.HttpClientNamedClientBindingMismatch,
        title: "多个 [HttpClientApi] 接口共存时命名客户端配置可能未按命名隔离",
        messageFormat: "检测到 {0} 个 [HttpClientApi] 接口共存。实现类通过类型级 IEnhancedHttpClient 解析客户端，各接口的命名客户端（{{接口名}}_HttpClient）与其 [HttpClientApi(Timeout)] 配置仅在对应名称被注册为默认 IEnhancedHttpClient 时生效，接口之间不按命名隔离。请阅读 README「生成客户端命名」章节。",
        category: "代码生成",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientPathParameterMismatch = new(
        id: "HTTPCLIENT013",
        title: "路径参数不匹配",
        messageFormat: "接口 {0} 的方法 {1} 的 URL 模板 '{2}' 中的路径参数与方法的 [Path] 参数不匹配。{3}",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HttpClientTypeNotFound = new(
        id: "HTTPCLIENT014",
        title: "HttpClient 类型未找到",
        // F-07：双错可诊断性——014 不阻断生成（可抑制策略），类型缺失必然在后续产生指向生成文件的
        // CS0246。追加引导语句把两个错误显式关联，避免用户把它们当独立问题分别排查。
        messageFormat: "接口 {0} 指定的 HttpClient 类型 '{1}' 在当前编译中未找到。请确认类型名称是否正确，或确保已通过 AddMudHttpClient 注册了对应的命名客户端。实现类生成将继续进行，随后可能出现指向生成文件的 CS0246（类型 '{1}' 未找到），两者为同一根因，无需分别排查。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TokenManagerTypeNotFound = new(
        id: "HTTPCLIENT015",
        title: "TokenManage 类型未找到",
        messageFormat: "接口 {0} 的 TokenManage 属性指定的类型 '{1}' 在当前编译中未找到。请确认类型名称正确，或确保包含该类型的项目已正确引用。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TokenManagerMissingMethod = new(
        id: "HTTPCLIENT016",
        title: "TokenManage 类型缺少必需方法",
        messageFormat: "接口 {0} 的 TokenManage 属性指定的类型 '{1}' 缺少必需的方法 '{2}'。TokenManage 类型必须提供 'IMudAppContext GetDefaultApp()' 和 'IMudAppContext GetApp(string appKey)' 方法，或实现 IAppManager<TAppContext> 接口。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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

    // CFG-27：原 HTTPCLIENT019（CacheAttribute 属性被生成器忽略）描述符已删除 ——
    // 其唯一触发点 CacheAttribute.Priority 已随 [Obsolete] 残留清理一并移除；
    // UseSlidingExpiration 早已受支持。[Cache] 当前无被忽略的属性。
    // ID HTTPCLIENT019 保留为未使用占位（不重新分配）。

    // M2-#12：非幂等方法声明 [Retry] 但未显式 AllowNonIdempotent 时，重试将被静默跳过。
    // 发出 Warning 提示（生成器侧编译期闭环，原方案 12.4）。
    public static readonly DiagnosticDescriptor RetryNonIdempotentWithoutAllow = new(
        id: "HTTPCLIENT020",
        title: "非幂等方法的 [Retry] 默认不生效",
        messageFormat: "接口 {0} 的方法 {1} 使用 HTTP {2}（非幂等方法）声明了 [Retry]，但未设置 AllowNonIdempotent = true。运行时将跳过重试（保留超时与熔断）以防止重复提交。如该操作在服务端可安全重复执行，请显式设置 [Retry(AllowNonIdempotent = true)]。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // CFG-07：方法级 [Timeout(ms)] 超过接口级 [HttpClientApi(Timeout=秒)] 声明的 HttpClient 超时。
    // HttpClient.Timeout 是硬上限，会使 Polly 方法级超时永不触发（配置静默失效）。
    // 仅在「方法级显式声明 [Timeout]」且「接口级 Timeout 显式声明」时报告（R-9 抑制误报）。
    public static readonly DiagnosticDescriptor MethodTimeoutExceedsHttpClientTimeout = new(
        id: "HTTPCLIENT021",
        title: "方法级 [Timeout] 超过 HttpClient 超时，将不会生效",
        messageFormat: "接口 {0} 的方法 {1} 声明了 [Timeout({2}ms)]，但接口级 HttpClient 超时为 {3} 秒。" +
                       "HttpClient.Timeout 是硬上限，方法级 Polly 超时将在其之后才可能触发（实际永不触发）。" +
                       "请将 [Timeout] 调整为小于 {3} 秒，或提高 [HttpClientApi(Timeout = …)]。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // P3.3（TK-18）：Path / HmacSignature 注入模式不被 TokenRecoveryDelegatingHandler /
    // TokenRecoveryEnhancedClient 的恢复执行器支持（ApplyTokenToRequest 对这两种模式直接返回 false），
    // 令牌过期触发 401 后刷新出的新令牌无法重新注入，恢复将静默失败。编译期以 Warning 提醒开发者。
    public static readonly DiagnosticDescriptor TokenRecoveryUnsupportedInjectionMode = new(
        id: "HTTPCLIENT022",
        title: "Path/HmacSignature 令牌注入模式不支持令牌恢复",
        messageFormat: "接口 {0} 的方法 {1} 使用令牌注入模式 '{2}'。该模式不被令牌恢复处理器（TokenRecoveryDelegatingHandler / TokenRecoveryEnhancedClient）支持：令牌过期触发 401 后，刷新得到的新令牌无法重新注入（Path 无法重写 URL 中的令牌，HmacSignature 无法用新令牌重算签名）。恢复将静默失败并返回 401。如需令牌恢复能力，请改用 Header/Query/ApiKey/Cookie/BasicAuth 注入模式。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // [F4] 强制失效逃生舱提示：-p:ForceHttpGenerator=true 时下游增量步骤必然 Modified，产出一行可观测提示。
    // 合并注记：本诊断原与 P3.3(TK-18) 的 TokenRecoveryUnsupportedInjectionMode 争用 HTTPCLIENT022；
    // TK-18 先落库并占用 022，故 [F4] 让位改用 023（ID 一经分配即保留，不再回收复用）。
    public static readonly DiagnosticDescriptor IncrementalCacheForcedInvalidation = new(
        id: "HTTPCLIENT023",
        title: "增量缓存已被 ForceHttpGenerator 强制失效",
        messageFormat: "已检测到 -p:ForceHttpGenerator=true，生成器增量缓存被强制失效，本次构建将重新生成全部实现类代码。",
        category: "代码生成",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    /// <summary>
    /// 接口成员未被生成实现，已发射占位实现（运行期调用将抛 NotSupportedException）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用途：保证生成类始终满足接口契约（不再产生 CS0535）之后，占位成员的存在必须编译期可见，
    /// 否则会把编译期错误静默降级为运行期故障。
    /// </para>
    /// <para>
    /// <b>级别为 Error</b>：占位成员在运行期必然抛 <see cref="System.NotSupportedException"/>，
    /// 属「接口声明的成员实际不可用」。修复前该情形表现为 <c>CS0535</c>（编译失败），
    /// 若降级为 Warning 则构建转为成功，等于把原本的编译期失败改成运行期故障 ——
    /// 对未标注 <c>[IgnoreGenerator]</c> 的属性/索引器/事件尤为危险（该情形没有其它 Error 级诊断兜底）。
    /// </para>
    /// <para>
    /// <b>不得添加 <see cref="WellKnownDiagnosticTags.NotConfigurable"/> 标签</b>：实测规律为
    /// 「生成器报出 <c>Error</c> 且带 <c>NotConfigurable</c> 标签的诊断时，同一编译中的<b>分析器</b>诊断
    /// （<c>MUD001</c>/<c>MUD002</c>/<c>MUD004</c>）整体不再呈现」（<c>HTTPCLIENT004</c>/<c>HTTPCLIENT005</c> 均可复现；
    /// 去掉标签或降为 Warning 后立即恢复）。本诊断是占位实现唯一可靠的可见性来源，必须始终呈现，故不加该标签。
    /// </para>
    /// <para>
    /// <b>报告约定：每次发射占位实现都报告</b>（不再沿用「有更具体诊断就不报」的旧约定）。
    /// 原因有二：其一，本诊断承载其它诊断无法替代的信息（占位已发射、运行期将抛异常）；
    /// 其二，兜底诊断可能是分析器诊断（MUD001/MUD002），而它受上一条规律影响可能整体消失，
    /// 一旦消失而本诊断又未报，占位实现就变成<b>静默</b>的运行期故障。
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor HttpClientMemberNotGeneratedPlaceholder = new(
        id: "HTTPCLIENT024",
        title: "接口成员未生成实现（已发射占位实现）",
        messageFormat: "接口 {0} 的成员 {1} 未生成实现（{2}）——已发射占位实现，运行期调用将抛 NotSupportedException。请在修复对应配置后重新生成，或为该成员标注 [IgnoreGenerator] 自行实现。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CFG-29（v3.1）：<c>[CircuitBreaker]</c> 特性参数取值超出该字段的有效域。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本诊断承载四个<b>同为 Error 级</b>的条件，避免为同一语义拆分多个 ID
    /// （<c>DocumentationContractTests</c> 按 ID 建索引，同一 ID 只能登记一个 <c>DefaultSeverity</c>；
    /// 与既有 <c>HTTPCLIENT025</c> 同一描述符覆盖 4 种编排的风格一致）：
    /// </para>
    /// <list type="number">
    ///   <item><c>FailureThreshold &lt; 1</c>（无条件）——<c>SamplingDurationSeconds = 0</c> 时该值直传
    ///     <c>CircuitBreakerAsync(exceptionsAllowedBeforeBreaking:)</c>，<c>&lt;= 0</c> 会使 Polly 抛 <c>ArgumentOutOfRangeException</c>；</item>
    ///   <item><c>SamplingDurationSeconds &gt; 0 &amp;&amp; FailureThreshold &gt; 100</c> —— 高级模式下该值为失败率百分比，
    ///     <c>PollyResiliencePolicyProvider</c> 会把 <c>&gt; 100</c> <b>静默压成 100%</b>（配置静默失效）；</item>
    ///   <item><c>SamplingDurationSeconds &gt; 0 &amp;&amp; MinimumThroughput &lt; 2</c> —— Polly <c>AdvancedCircuitBreakerAsync</c> 下限；</item>
    ///   <item><c>BreakDurationSeconds &lt;= 0</c> —— 熔断时长必须为正。</item>
    /// </list>
    /// <para>
    /// <b>为何必须落在生成器而非 Attribute setter</b>：Roslyn <b>从不实例化</b> Attribute
    /// （特性以元数据形式存在于编译产物中，生成器读到的是 <see cref="AttributeData"/>），
    /// 因此写在 <c>CircuitBreakerAttribute</c> setter 中的校验在任何编译路径下都不会执行 ——
    /// 这与 <c>CircuitBreakerOptions</c>（运行期由 DI/委托实例化，setter 会执行）形成根本差异。
    /// </para>
    /// <para>
    /// <b>不加 <see cref="WellKnownDiagnosticTags.NotConfigurable"/></b>：使用者改一行即可修复，
    /// 且需避免"Error + NotConfigurable 连坐抑制同编译内全部分析器诊断"（见本文件顶部标签分层准则）。
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor CircuitBreakerAttributeValueOutOfRange = new(
        id: "HTTPCLIENT026",
        title: "[CircuitBreaker] 特性参数取值超出有效域",
        messageFormat: "接口 {0} 的方法 {1} 的 [CircuitBreaker] 参数取值非法：{2}。该取值在运行时无法按预期工作，请修正。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// CFG-32（v3.1）：<c>[Timeout]</c> 特性有效值 <c>&lt;= 0</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 旧行为：<c>[Timeout(0)]</c> 会生成 <c>TimeoutEnabled = true, TimeoutMilliseconds = 0</c>，
    /// 进而构造 <c>Policy.TimeoutAsync(TimeSpan.Zero, …)</c> —— 一个语义不可预期的策略，
    /// 且与 <c>TimeoutOptions</c>（setter 抛 <c>ArgumentOutOfRangeException</c>）形成保护等级差。
    /// </para>
    /// <para>
    /// <b>不误报「未声明」</b>：仅在特性<b>存在</b>时校验；未声明 <c>[Timeout]</c> 仍表示
    /// <c>MethodTimeoutEnabled = false</c>（既有语义，由 <c>MethodAnalyzer.AnalyzeTimeoutAttribute</c> 的
    /// <c>null</c> 分支返回）。
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor TimeoutAttributeNonPositive = new(
        id: "HTTPCLIENT027",
        title: "[Timeout] 特性参数必须为正毫秒数",
        messageFormat: "接口 {0} 的方法 {1} 的 [Timeout] 取值非法：{2}。[Timeout] 必须为正毫秒数；如需取消方法级超时请移除该特性。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// A4：继承模式下派生类使用 <c>new</c> 隐藏基类的应用切换成员（UseApp/BeginScope/UseDefaultApp 等）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 当派生接口使用 TokenManager 模式而基接口使用默认模式（或反过来）时，两级的切换来源不同
    /// （<c>_tokenManager</c> vs <c>_appManager</c>），生成器必须使用 <c>new</c> 而非 <c>override</c>。
    /// 这意味着通过基类引用调用切换方法会走基类实现，可能指向错误的应用上下文来源。
    /// </para>
    /// <para>
    /// 级别为 Warning：代码可编译且语义正确（<c>new</c> 是合法的 C# 修饰符），但存在调用路径分叉风险。
    /// 不加 <see cref="WellKnownDiagnosticTags.NotConfigurable"/>（用户可通过统一两级的 TokenManage 配置修复）。
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor AppSwitchMemberHidden = new(
        id: "HTTPCLIENT028",
        title: "继承模式下应用切换成员被隐藏",
        messageFormat: "接口 {0} 继承自 {1} 且两者应用切换来源不同（TokenManage 与默认模式混合），生成的 UseApp/BeginScope 使用 new 隐藏基类成员。请通过派生接口调用切换方法，或统一两级的 TokenManage 配置。",
        category: "代码生成",
        DiagnosticSeverity.Warning,
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
        isEnabledByDefault: true);
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
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FormContentMultipleFilePathAttributes = new(
        id: "FORM003",
        title: "FormContent存在多个FilePath属性",
        messageFormat: "类 {0} 标记了 [FormContent] 特性，但发现了多个标记了 [FilePath] 特性的属性: {1}。必须且只能有一个属性标记 [FilePath] 特性。",
        category: "代码生成",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
    #endregion

    #region AOT JSON 序列化诊断信息 (AOT001-007)
    // 诊断由 HttpJsonContextScaffolder（pre-build 工具）或独立分析器报告。
    // 前缀 AOT 遵循仓库 XXXNNN 约定（3-6 字符前缀 + 3 位数字）。
    // GEN-15（§8.3）：AOT001/AOT002/AOT003 描述符已迁移到
    // Tools/Mud.HttpUtils.JsonContextScaffolder/ (ScaffolderAotDiagnostics)，由脚手架在生成期报告，
    // 不再参与本生成器/分析器分发。此处不保留其字段，避免「貌似已实现」的错觉（I-20 可达性守卫兜底）。
    // 生成器/分析器侧仅登记 AOT004/AOT005/AOT006/AOT007。

    public static readonly DiagnosticDescriptor AotDtoNotCoveredByContext = new(
        id: DiagnosticIds.AotDtoNotCoveredByContext,
        title: "HttpClient API 方法的 DTO 未被任何 JsonSerializerContext 覆盖",
        messageFormat: "接口 {0} 的方法 {1} 使用的请求/响应 DTO '{2}' 未被任何已引用的 JsonSerializerContext 覆盖。AOT 下序列化将抛 NotSupportedException。修复：在 DTO 类型上标注 [HttpJsonSerializable] 并运行 'dotnet mud-jsonctx --project <path>' 生成上下文，或将类型手动加入现有 JsonSerializerContext。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AotQueryParameterNotInContext = new(
        id: DiagnosticIds.AotQueryParameterNotInContext,
        title: "查询参数类型使用 JSON 序列化但未被 Context 覆盖",
        messageFormat: "接口 {0} 的方法 {1} 的查询参数 '{2}' 标注了 JSON 序列化，但其类型 '{3}' 未被任何 JsonSerializerContext 覆盖。AOT 下查询参数 JSON 序列化可能失败。建议将此类型纳入 JsonSerializerContext 或实现 IQueryParameter 接口。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AotJsonSerializableNotCovered = new(
        id: DiagnosticIds.AotJsonSerializableNotCovered,
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
        id: DiagnosticIds.AotXmlNotSupported,
        title: "XML 序列化在 Native AOT 下不支持",
        messageFormat: "接口 {0} 的方法 {1} 使用 XML 序列化，Native AOT 下 XmlSerializer 需要动态代码生成，会在运行时抛 PlatformNotSupportedException。请改用 [SerializationMethod(SerializationMethod.Json)]，或在非 AOT 部署场景使用 XML。",
        category: "AOT",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        // 说明：末尾使用 ASCII 句点而非中文句号 —— RS1033 要求 description 以标点结尾且不识别「。」。
        description: "XmlSerializer 在 Native AOT 下不支持。请将方法改为 JSON 序列化，或在非 AOT 部署场景使用 XML。此诊断仅在 AOT 上下文（IsAotCompatible=true 或 PublishAot=true）下报告.",
        helpLinkUri: "https://learn.microsoft.com/dotnet/core/deploying/native-aot");

    /// <summary>
    /// [F10 修复] AOT007 降级变体：仅 <c>IsAotCompatible=true</c>（但未发布 Native AOT）时使用。
    /// 语义：AOT 分析器已启用但运行期未必 AOT —— 降为 Warning，并提示改用 PublishAot/MudAotRuntimeMode 显式声明。
    /// 严格模式（WarningsAsErrors）下仍可升级为 Error，CI 门禁强度由用户掌控。
    /// </summary>
    /// <remarks>
    /// <b>RS2001 抑制说明</b>：发布跟踪（<c>AnalyzerReleases.*.md</c>）以<b>规则 ID</b> 为键，
    /// 同一 ID 只能登记一个 Severity。本变体与 Error 变体刻意共用 <c>AOT007</c>
    /// （用户侧只感知一个"XML 不支持"规则，级别差异由 <c>MudAotRuntimeMode</c>/<c>PublishAot</c> 决定），
    /// 因此发布跟踪必然与其中一个描述符的级别不符 → RS2001。
    /// 这是有意设计，故就地抑制 RS2001（而非拆分为 AOT008 破坏 F10 的分级语义）。
    /// </remarks>
#pragma warning disable RS2001 // AOT007 分级变体刻意共用 ID，级别差异不影响用户侧的规则认知
    public static readonly DiagnosticDescriptor AotXmlNotSupportedInAotWarning = new(
        id: DiagnosticIds.AotXmlNotSupported,
        title: "XML 序列化在 Native AOT 下可能不支持（AOT 分析器已启用但未声明运行期 AOT）",
        messageFormat: "接口 {0} 的方法 {1} 使用 XML 序列化。当前项目仅设置了 IsAotCompatible=true（启用 AOT 分析器），但未声明以 Native AOT 发布；若以 Native AOT 发布请同时设置 PublishAot=true 或 MudAotRuntimeMode=aot，否则 XML 路径在运行期将抛 PlatformNotSupportedException。",
        category: "AOT",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "仅设置 IsAotCompatible 时的降级提示（F10）.");
#pragma warning restore RS2001
    #endregion

    #region 接口规范 / DI 生命周期分析器诊断信息 (MUD001/MUD002/MUD004)
    // 由本程序集内的 MudHttpInterfaceAnalyzer / TokenManagerLifetimeAnalyzer 报告。
    // 说明：这三个描述符原定义在独立的 Mud.HttpUtils.Analyzers 程序集中（该程序集已合并入本工程），
    // 现集中登记以便统一与 README 诊断表做一致性核对（DocumentationContractTests）。

    public static readonly DiagnosticDescriptor MudMethodMissingHttpMethodAttribute = new(
        id: DiagnosticIds.MudMethodMissingHttpMethodAttribute,
        title: "HttpClientApi 方法缺少 HTTP 方法特性",
        messageFormat: "方法 '{0}' 缺少 HTTP 方法特性（[Get]/[Post]/[Put]/[Delete]/[Patch]/[Head]/[Options]）",
        category: "Mud.HttpUtils.Interface",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "标记了 [HttpClientApi] 的接口中的每个方法必须标注一个 HTTP 方法特性.");

    // 判定口径与生成器一致（共用 ReturnTypeSupport.IsSupported）：
    // 只接受异步形态。裸 byte[]/Stream/HttpResponseMessage 曾被视为合法，
    // 但生成器对它们会产出「非 async 方法体内含 await」的不可编译代码（实测 CS4032），
    // 属「分析器沉默 + 生成坏代码」的漏报，已收紧。
    public static readonly DiagnosticDescriptor MudMethodInvalidReturnType = new(
        id: DiagnosticIds.MudMethodInvalidReturnType,
        title: "HttpClientApi 方法返回类型无效",
        messageFormat: "方法 '{0}' 返回类型 '{1}' 无效，应为异步形态：Task、Task<T>、ValueTask、ValueTask<T> 或 IAsyncEnumerable<T>（响应体类型 T 可为任意类型，含 byte[]/Stream/HttpResponseMessage/自定义类型）",
        category: "Mud.HttpUtils.Interface",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "HttpClientApi 接口方法必须返回生成器支持的返回类型（异步形态）.");

    public static readonly DiagnosticDescriptor MudNonSingletonTokenManager = new(
        id: DiagnosticIds.MudNonSingletonTokenManager,
        title: "ITokenManager 实现应注册为 Singleton",
        messageFormat: "令牌管理器类型 '{0}' 应在 IServiceCollection 中注册为 Singleton（当前使用 '{1}'）。“ITokenManager”的实现内部维护令牌缓存与并发锁，Scoped/Transient 注册会使每个请求持有独立缓存实例，导致并发安全机制失效与重复刷新令牌。请改用 AddSingleton/TryAddSingleton。",
        category: "Mud.HttpUtils.DependencyInjection",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "ITokenManager 实现应注册为 Singleton，以避免并发安全机制失效与冗余令牌刷新.");

    // SR-L6（P3.9，D13）：Query 注入模式令牌进入 URL → 代理 / 访问日志 / 浏览器历史不可控。
    // 库内遥测已由 SensitiveUrlRedactor 脱敏，外部系统不受控；生产环境建议 Header。
    // MT-21（BC-22）：级别由 Info 提升为 Warning（Info 在默认构建下几乎不可见，等于没有提示），
    // 并把覆盖范围从 Query 扩展到 Path（令牌进 URL 路径，留存面与 Query 等同）。
    // 不在生成物发射 #warning（避免污染消费方构建），仍然可抑制。
    public static readonly DiagnosticDescriptor MudQueryTokenInjectionMode = new(
        id: DiagnosticIds.MudQueryTokenInjectionMode,
        title: "Query / Path 令牌注入模式存在泄露面",
        messageFormat: "接口 {0} 使用 {1} 令牌注入模式：令牌将进入请求 URL 或路径，可能被代理 / 访问日志 / 浏览器历史等不受控的外部系统记录。库内遥测已脱敏，但外部系统不受控；生产环境建议改用 Header 注入模式。",
        category: "Mud.HttpUtils.Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Query / Path 注入模式令牌进入 URL，存在日志/历史泄露面；建议生产环境使用 Header 模式.");
    #endregion
}
