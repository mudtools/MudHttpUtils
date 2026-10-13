// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// F-4：以<b>外部标识</b>（external id）反查应用标识（appKey）的查询契约。
/// </summary>
/// <remarks>
/// <para>
/// <b>典型场景</b>：第三方回调（如企业微信 SuiteId、开放平台 suite_ticket 推送）只携带外部标识，
/// 宿主需要先映射到 appKey，再走 <c>GetApp(appKey)</c> / <c>UseAppScope(appKey)</c> 的既有路径。
/// </para>
/// <para>
/// <b>语义约定</b>：
/// <list type="bullet">
/// <item>键（外部标识名）与值（外部标识值）均<b>序数、区分大小写</b>（与 appKey 语义一致）。</item>
/// <item>未命中 / 映射歧义 / 输入非法一律<b>fail-closed</b>：返回 <c>false</c> 且 <c>appKey</c> 为
/// <c>null</c>，<b>绝不猜测</b>（防止外部不可信输入被误路由到错误应用）。</item>
/// <item>本契约只做"标识 → appKey"映射，<b>不做</b>任何上下文物化副作用，也<b>不做</b>授权判定 ——
/// 解析出的 appKey 在切换前仍须经 <see cref="IAppAccessAuthorizer"/>（如
/// <see cref="AppKeyAllowListAuthorizer"/>）等授权链路。</item>
/// <item><paramref name="externalIds"/> 中的空值项被忽略；<b>全部</b>提供的键都解析成功且指向<b>同一个</b>
/// appKey 时才返回 <c>true</c>（多键互证）。</item>
/// </list>
/// </para>
/// <para>
/// <b>实现</b>：<see cref="DefaultAppManager{TAppContext}"/> 自动建立反查索引
/// （上下文实现 <see cref="IAppExternalIds"/> 时），宿主也可自实现本接口（如从配置/数据库解析）
/// 并按需替换。
/// </para>
/// </remarks>
public interface IAppLookupProvider
{
    /// <summary>
    /// 以一组外部标识反查应用标识。
    /// </summary>
    /// <param name="externalIds">外部标识集合（如 <c>{"SuiteId": "ww123..."}</c>）；
    /// <c>null</c>、空集合或全空值项视为非法输入（fail-closed）。</param>
    /// <param name="appKey">当此方法返回 <c>true</c> 时，包含解析出的应用标识；否则为 <c>null</c>。</param>
    /// <returns>解析成功返回 <c>true</c>；未命中、映射歧义、输入非法或映射目标已失效时返回 <c>false</c>（fail-closed）。</returns>
    bool TryResolveAppKey(IReadOnlyDictionary<string, string> externalIds, out string? appKey);
}
