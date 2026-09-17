// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// SR-M6（P2.4，D9）令牌管理器注册表：按查找键（<see cref="TokenRecoveryContext.TokenManagerKey"/>）
/// 解析令牌管理器，支持 401 恢复执行器跨应用共享时按请求路由到正确的管理器实例。
/// </summary>
/// <remarks>
/// <para>生成器在未显式指定 TokenManagerKey 时会写入由 TokenType 推断的默认键
/// （见 TokenMethodHelper），因此<b>解析失败返回 null（由调用方决定回退策略）而非 fail-fast</b>，
/// 避免击穿所有默认场景的 401 恢复可用性。</para>
/// <para>默认实现：<see cref="DelegateTokenManagerRegistry"/>（Client 包，配合
/// <c>AddTokenManagerRegistry</c> DI 助手）。</para>
/// </remarks>
public interface ITokenManagerRegistry
{
    /// <summary>
    /// 按查找键解析令牌管理器；未知键返回 null（由调用方决定回退策略）。
    /// </summary>
    /// <param name="tokenManagerKey">令牌管理器查找键（生成代码经 TokenRecoveryContext 贯通）。</param>
    /// <returns>对应的令牌管理器实例；未注册时返回 null。</returns>
    ITokenManager? Resolve(string tokenManagerKey);
}
