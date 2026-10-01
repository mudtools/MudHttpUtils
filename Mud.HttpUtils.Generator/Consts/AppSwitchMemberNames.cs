// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Generator.Consts;

/// <summary>
/// 生成器无条件发射的「应用切换 / 上下文持有」成员名清单（SW-11 单一事实源）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要</b>：这些成员名此前散落在 <c>InterfaceImplementationGenerator.RegisterInfrastructureMembers</c> 与
/// <c>ConstructorGenerator</c> 的字符串字面量中，两侧必须手工保持一致 —— 任一处的成员名或门控条件改动而另一处未跟进，
/// 就会出现「登记了但没发射」（占位实现缺失 → CS0535）或「发射了但没登记」（契约补全重复发射 → CS0111/CS0102）。
/// G8-15 的 <c>UseAppScope</c> 漏登记即为该缺陷的真实案例。
/// </para>
/// <para>
/// <b>范围</b>：本清单是<b>非 HttpClient 模式</b>下 <c>GenerateAppContextMembers</c> / <c>GenerateUseAppMethod</c> 发射的全部实例成员。
/// 令牌相关成员（<c>GetTokenAsync</c> 等）由 <c>AccessTokenGenerator</c> 在 TokenManager 模式单独发射，不在本表。
/// </para>
/// <para>
/// <b>守卫</b>：<c>AppSwitchMemberNamesTests</c> 真编译一个 Default 模式接口并反射生成类的 public 成员，
/// 断言其 ⊇ 本表（成员名只来自本表 ⇒ 防漂移）。
/// </para>
/// </remarks>
internal static class AppSwitchMemberNames
{
    /// <summary>受信实例面：当前应用上下文属性（<c>IAppContextHolder.Current</c>）。</summary>
    public const string Current = "Current";

    /// <summary>受信实例面：无作用域切换（<c>IAppContextHolder.SwitchTo</c>）。</summary>
    public const string SwitchTo = "SwitchTo";

    /// <summary>
    /// 两个重载共享的方法名：受信实例面 <c>BeginScope(IMudAppContext)</c> 与
    /// 不可信 appKey 面 <c>BeginScope(string)</c>（后者已标 <c>[Obsolete]</c>，与 <c>UseAppScope</c> 逐行等价）。
    /// </summary>
    public const string BeginScope = "BeginScope";

    /// <summary>不可信 appKey 面：无作用域切换（已标 <c>[Obsolete]</c>，见 <see cref="UseAppScope"/>）。</summary>
    public const string UseApp = "UseApp";

    /// <summary>不可信 appKey 面：作用域式切换的<b>唯一推荐入口</b>（<c>IAppScopeSwitcher.UseAppScope</c>）。</summary>
    public const string UseAppScope = "UseAppScope";

    /// <summary>默认应用：无作用域切换（已标 <c>[Obsolete]</c>，见 <see cref="UseDefaultAppScope"/>）。</summary>
    public const string UseDefaultApp = "UseDefaultApp";

    /// <summary>默认应用：作用域式切换的推荐入口（<c>IAppScopeSwitcher.UseDefaultAppScope</c>）。</summary>
    public const string UseDefaultAppScope = "UseDefaultAppScope";

    /// <summary>
    /// <b>无条件发射</b>的成员（非 HttpClient 模式）：Holder 面（<see cref="Current"/> / <see cref="SwitchTo"/> /
    /// <see cref="BeginScope"/>）被请求执行链路与 <c>DefaultHttpRequestExecutor</c> 依赖，
    /// 作用域面（<see cref="UseAppScope"/> / <see cref="UseDefaultAppScope"/>）是 <c>IAppScopeSwitcher</c> 的实现物（`SW-01`）。
    /// </summary>
    public static readonly string[] Unconditional =
    [
        Current,
        SwitchTo,
        BeginScope,
        UseAppScope,
        UseDefaultAppScope,
    ];

    /// <summary>
    /// <b>条件发射</b>的成员（`BC-27`）：仅当使用方接口（自身或任一基接口）<b>自行声明</b>同名成员时才发射。
    /// </summary>
    /// <remarks>
    /// 若无条件删除发射点，则"接口自行声明 <c>UseApp</c> 并要求生成器实现"的合法场景会落入契约补全
    /// （<c>NotSupportedException</c> 占位 + <c>HTTPCLIENT024</c>（Error））⇒ 原本可编译的代码变为编译失败。
    /// 门控判定见 <c>GeneratorContext.DeclaresUseAppMember</c> / <c>DeclaresUseDefaultAppMember</c>。
    /// </remarks>
    public static readonly string[] Conditional =
    [
        UseApp,
        UseDefaultApp,
    ];

    /// <summary>
    /// 全量清单（无条件 + 条件），顺序与 <c>RegisterInfrastructureMembers</c> 的登记顺序一致，便于人工比对。
    /// </summary>
    public static readonly string[] All =
    [
        Current,
        SwitchTo,
        BeginScope,
        UseApp,
        UseAppScope,
        UseDefaultApp,
        UseDefaultAppScope,
    ];
}
