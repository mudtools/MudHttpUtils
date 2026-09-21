// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Diagnostics;

namespace Mud.HttpUtils.Generators.Context;

/// <summary>
/// 基类（<c>InheritedFrom</c> 指向的生成器产出抽象类）的运行模式。
/// </summary>
/// <remarks>
/// G8-04：仅用于 <c>base(...)</c> 位置实参分派。<b>不是</b>新的可写状态 ——
/// 由 <see cref="GenerationConfiguration.BaseRuntimeMode"/> 从三个既有 <c>BaseHas*</c> 旗标唯一推导。
/// </remarks>
internal enum BaseRuntimeMode
{
    /// <summary>默认（AppContext）模式：基类构造函数 <c>(IMudAppContext, IAppContextHolder, IHttpRequestExecutor, …)</c>。</summary>
    Default,

    /// <summary>TokenManager 模式：基类构造函数 <c>(TokenManagerType, IAppContextHolder, ITokenProvider, [ICurrentUserContext,] IHttpRequestExecutor, …)</c>。</summary>
    TokenManager,

    /// <summary>HttpClient 模式：基类构造函数 <c>(HttpClientType, IHttpRequestExecutor, …)</c>。</summary>
    HttpClient,
}

/// <summary>
/// 生成配置
/// </summary>
[DebuggerDisplay("TokenManager={TokenManager} HttpClient={HttpClient} IsAbstract={IsAbstract}")]
internal class GenerationConfiguration
{
    public string HttpClientOptionsName { get; set; } = "HttpClientOptions";

    public string DefaultContentType { get; set; } = "application/json";

    // CFG-21：原 Timeout 属性为死字段（赋值于 InterfaceImplementationGenerator，全仓无读取点），已删除。
    // 接口级超时由 HttpInvokeRegistrationGenerator 直接从 [HttpClientApi(Timeout=…)] 读取
    // （默认值 HttpClientGeneratorConstants.DefaultHttpClientTimeoutSeconds = 50）。

    public bool IsAbstract { get; set; }

    public string? InheritedFrom { get; set; }

    public string? TokenManager { get; set; }

    /// <summary>
    /// 从特性中提取的原始 TokenManager 值（未经过互斥处理），
    /// 用于在 ValidateConfiguration 中正确检测 HttpClient 与 TokenManager 的互斥冲突。
    /// </summary>
    public string? RawTokenManager { get; set; }

    public string? TokenManagerType { get; set; }

    /// <summary>
    /// HttpClient接口类型（与TokenManager互斥，优先使用）
    /// </summary>
    public string? HttpClient { get; set; }

    public string? TokenType { get; set; }

    /// <summary>
    /// 是否为用户访问令牌 (UserAccessToken)
    /// </summary>
    public bool IsUserAccessToken { get; set; }

    /// <summary>
    /// 接口级 TokenManagerKey（从 [Token(TokenManagerKey = "...")] 特性获取）。
    /// 当指定时，使用此键而非 TokenType 从 IMudAppContext 中查找令牌管理器。
    /// </summary>
    public string? TokenManagerKey { get; set; }

    /// <summary>
    /// 接口级是否需要 UserId（从 [Token(RequiresUserId = true)] 特性获取）。
    /// 当未显式指定时，根据 IsUserAccessToken 自动推断。
    /// </summary>
    public bool? RequiresUserId { get; set; }

    /// <summary>
    /// 是否有任何方法需要 UserId（包括接口级和方法级）。
    /// 用于决定是否在构造函数中注入 ICurrentUserContext。
    /// </summary>
    public bool AnyMethodRequiresUserId { get; set; }

    /// <summary>
    /// 接口的基础路径前缀（从 [BasePath] 特性获取）
    /// </summary>
    public string? BasePath { get; set; }

    /// <summary>
    /// 基类接口是否具有缓存方法（用于确定 base(...) 构造函数调用参数）。
    /// </summary>
    public bool BaseHasCache { get; set; }

    /// <summary>
    /// 基类接口是否具有弹性策略方法（用于确定 base(...) 构造函数调用参数）。
    /// </summary>
    public bool BaseHasResilience { get; set; }

    /// <summary>
    /// 基类是否具有令牌管理（用于确定派生类是否需要生成自己的令牌字段和方法）。
    /// </summary>
    public bool BaseHasTokenManager { get; set; }

    /// <summary>
    /// FIX-06：基类是否需要 currentUserContext 参数。
    /// 为 true 时派生类 base(...) 调用必须传入 currentUserContext（位置实参顺序敏感），
    /// 否则基类需 userId 时实参错位（CS1503 + CS7036）。
    /// 与 <see cref="AnyMethodRequiresUserId"/>（派生类自身判定）同口径，
    /// 对基接口符号求值并赋值（复用 ComputeAnyMethodRequiresUserId，不得重写第二份判定 —— §0.2 原则 9）。
    /// </summary>
    public bool BaseRequiresUserId { get; set; }

    /// <summary>
    /// 基类是否声明了 <c>_appAuthorizer</c> 字段（即基类为非 HttpClient 模式）。
    /// 为 true 时派生类不得重复声明同名字段（CS0108 隐藏基类成员），
    /// 且因基类字段为 <c>readonly</c>（派生类构造函数无权赋值），须改为在 <c>base(...)</c> 调用中透传 <c>appAuthorizer</c>。
    /// </summary>
    public bool BaseHasAppAuthorizer { get; set; }

    /// <summary>
    /// [G7-01] 基类是否为默认（AppContext）模式：既未设 <c>HttpClient</c> 亦未设 <c>TokenManage</c>。
    /// 为 true 时基类构造函数持有可选参数 <c>IAppManager&lt;IMudAppContext&gt;? appManager</c>
    /// （映射为基类 <c>_appManager</c> 字段），派生类必须在 <c>base(...)</c> 中透传 <c>appManager: appManager</c>，
    /// 否则 DI 注入的 appManager 被丢弃，基类 <c>_appManager</c> 恒为 null，
    /// UseApp/BeginScope(appKey) 抛「当前模式不支持」（多应用不可用，P0）。
    /// 与 <c>BaseHasAppAuthorizer</c>（非 HttpClient 即 true）不同，本旗标仅默认模式为 true。
    /// </summary>
    public bool BaseHasAppManager { get; set; }

    /// <summary>
    /// InheritedFrom 对应的基接口名称（用于排除基接口方法，避免多基接口场景下遗漏方法）。
    /// </summary>
    public string? InheritedFromInterfaceName { get; set; }

    /// <summary>
    /// 基类的运行模式（供 <c>base(...)</c> 位置实参分派，G8-04）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由三个 <c>BaseHas*</c> 旗标<b>唯一推导</b>（<c>HttpClient</c> 模式 ⇔ 基类设了 HttpClient ⇔
    /// <see cref="BaseHasAppAuthorizer"/> 为 false；<c>TokenManager</c> 模式 ⇔ <see cref="BaseHasTokenManager"/>；
    /// 其余为 <c>Default</c> ⇔ <see cref="BaseHasAppManager"/>）：
    /// 任何一方发生变化，本属性随之变化，**不存在第二事实源**，也不可能与旗标不一致。
    /// </para>
    /// <para>
    /// <b>职责边界（勿混淆）</b>：本属性<b>仅</b>用于 <c>base(...)</c> 实参分派；
    /// <c>appManager: appManager</c> 的透传条件必须继续使用 <see cref="BaseHasAppManager"/>（07:192 结论），
    /// 字段归属判定继续使用 <see cref="BaseHasAppAuthorizer"/>。
    /// </para>
    /// <para>
    /// <b>前提</b>：仅当 <see cref="InheritedFromInterfaceName"/> 非空（基类确为生成的抽象类）时才有意义；
    /// <c>InheritedFrom</c> 指向宿主自维护基类时三者皆 false，本属性会推导为 <c>Default</c>，
    /// 此时 <c>ConstructorGenerator</c> 会退回「按派生侧模式」的既有启发式（见该处注释）。
    /// </para>
    /// </remarks>
    public BaseRuntimeMode BaseRuntimeMode
        => !BaseHasAppAuthorizer ? BaseRuntimeMode.HttpClient
         : BaseHasTokenManager ? BaseRuntimeMode.TokenManager
         : BaseRuntimeMode.Default;
}
