// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace HttpClientApiTest.Api;

/// <summary>
/// SW-01 / SW-14 样例：**按接口编程**使用作用域式应用切换（Default 模式，无需 TokenManage）。
/// </summary>
/// <remarks>
/// <para>
/// 本接口显式继承 <see cref="IAppScopeSwitcher"/>，因此调用方可以完全不依赖生成类的具体类型：
/// </para>
/// <code>
/// IAppScopeSwitcher switcher = serviceProvider.GetRequiredService&lt;IMultiAppSwitchSampleApi&gt;();
/// using (switcher.UseAppScope("app-a"))
/// {
///     // 该作用域内所有请求使用 app-a 的上下文；释放时自动归还
/// }
/// </code>
/// <para>
/// 注意：即使接口**不**显式继承 <see cref="IAppScopeSwitcher"/>，只要它继承了
/// <see cref="IAppContextSwitcher"/>（含 <c>GetTokenAsync</c>，需要 TokenManager 模式），
/// 生成器也会**自动**为生成类附加 <see cref="IAppScopeSwitcher"/>（SW-01 的条件追加）。
/// </para>
/// <para>
/// 与旧入口的区别：<c>UseApp(appKey)</c> / <c>BeginScope(appKey)</c> 已标记 <c>[Obsolete]</c>，
/// 推荐统一改用 <c>UseAppScope(appKey)</c> —— 守卫完全相同，且额外保证上下文自动归还。
/// </para>
/// </remarks>
[HttpClientApi]
public interface IMultiAppSwitchSampleApi : IAppScopeSwitcher
{
    /// <summary>
    /// 按当前作用域中的应用上下文获取用户信息。
    /// </summary>
    /// <param name="id">用户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户信息（示例中为 JSON 文本）。</returns>
    [Get("/api/v1/users/{id}")]
    Task<string> GetUserAsync([Path] string id, CancellationToken cancellationToken = default);
}
