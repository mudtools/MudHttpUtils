// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌存储的<b>原生 CAS（Compare-And-Swap）</b>能力探测式可选接口 ——
/// 「比对旧令牌相等才写入/删除」的原子原语（组件级可选接口，不进 <see cref="ITokenStore"/> 最小 SPI）。
/// </summary>
/// <remarks>
/// <para><b>动机（飞书 TMF-05）</b>：refresh token 轮换需「比对旧 refresh token 相等才删除/替换」——
/// 因相对秒编码值随时刻漂移，整串比较必失配，下游被迫解码明文比较后再 <see cref="ITokenStore.RemoveAsync"/>，
/// 两步之间存在竞态窗口（并发轮换可双删或漏删）。本契约把比较与写入合并为一次原子操作。</para>
/// <para><b>语义约定</b>：
/// <list type="bullet">
/// <item><description><paramref name="expectedToken"/> 按<b>解码后明文令牌值</b>匹配 ——
/// codec 参与与否由实现方决定，契约只约定明文比较（同令牌不同编码形态命中）。</description></item>
/// <item><description><paramref name="newToken"/> 为 null/空时为<b>compare-and-delete</b> 语义
/// （匹配则删键）；非空时为 compare-and-set（匹配则写入经 codec 编码的新值）。</description></item>
/// <item><description>返回 <c>true</c> = 匹配且写入/删除成功；<c>false</c> = 键不存在/不匹配（未做任何写入）。</description></item>
/// <item><description><b>原子性边界</b>：实现方仅在「默认 codec 家族」下保证原子
/// （<c>{seconds}|{token}</c> 形态可在服务端脚本内拆分提取令牌段）；自定义 codec 时实现方
/// 应退化为「读取 → 明文比较 → 写入」非原子路径并文档化（不得许诺实现不了的原子性）。</description></item>
/// </list></para>
/// </remarks>
public interface ITokenStoreCompareExchange
{
    /// <summary>访问令牌维度的 CAS：比对旧访问令牌（明文）相等才写入/删除。</summary>
    /// <param name="tokenType">令牌类型标识。</param>
    /// <param name="expectedToken">期望的旧令牌明文（不得为空）。</param>
    /// <param name="newToken">新令牌明文；null/空 = compare-and-delete。</param>
    /// <param name="expiresInSeconds">新令牌有效秒数（&gt; 0 时生效；≤ 0 = 不设 TTL，宁缺勿滥）；compare-and-delete 时忽略。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配且写入/删除成功返回 <c>true</c>；键不存在或不匹配返回 <c>false</c>。</returns>
    Task<bool> CompareExchangeAccessTokenAsync(string tokenType, string expectedToken, string? newToken, long expiresInSeconds = 0, CancellationToken cancellationToken = default);

    /// <summary>刷新令牌维度的 CAS：比对旧刷新令牌（明文）相等才写入/删除（refresh 轮换主场景）。</summary>
    /// <param name="tokenType">令牌类型标识。</param>
    /// <param name="expectedToken">期望的旧刷新令牌明文（不得为空）。</param>
    /// <param name="newToken">新刷新令牌明文；null/空 = compare-and-delete。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配且写入/删除成功返回 <c>true</c>；键不存在或不匹配返回 <c>false</c>。</returns>
    Task<bool> CompareExchangeRefreshTokenAsync(string tokenType, string expectedToken, string? newToken, CancellationToken cancellationToken = default);
}
