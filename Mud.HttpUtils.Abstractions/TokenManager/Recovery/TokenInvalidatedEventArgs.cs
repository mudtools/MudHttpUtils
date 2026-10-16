// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷与责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌失效通知事件参数（<c>TokenManagerBase.TokenInvalidated</c>）。
/// </summary>
public sealed class TokenInvalidatedEventArgs : EventArgs
{
    /// <summary>被失效令牌所属管理器声明的令牌类型（<c>TokenType</c> 未声明时为 null）。</summary>
    public string? TokenType { get; }

    /// <summary>失效作用域（null/空数组 = 默认作用域）。</summary>
    public string[]? Scopes { get; }

    /// <summary>失效前的令牌信息（缓存未命中时为 <c>TokenResult.Empty</c>）。</summary>
    public TokenResult Removed { get; }

    /// <summary>
    /// 初始化事件参数。
    /// </summary>
    /// <param name="tokenType">令牌类型；未声明时为 null。</param>
    /// <param name="scopes">失效作用域；null/空数组 = 默认作用域。</param>
    /// <param name="removed">失效前的令牌信息。</param>
    public TokenInvalidatedEventArgs(string? tokenType, string[]? scopes, TokenResult removed)
    {
        TokenType = tokenType;
        Scopes = scopes;
        Removed = removed;
    }
}
