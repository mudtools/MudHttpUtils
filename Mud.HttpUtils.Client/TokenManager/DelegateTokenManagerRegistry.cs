// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// SR-M6（P2.4，D9）基于委托的 <see cref="ITokenManagerRegistry"/> 默认实现。
/// </summary>
/// <remarks>
/// 配合 <c>AddTokenManagerRegistry</c> DI 助手注册；委托内通常查询
/// <c>IMudAppContext.GetTokenManager(key)</c> 或自维护的键 → 管理器映射。
/// </remarks>
public sealed class DelegateTokenManagerRegistry : ITokenManagerRegistry
{
    private readonly Func<string, ITokenManager?> _resolver;

    /// <summary>
    /// 初始化委托注册表。
    /// </summary>
    /// <param name="resolver">解析委托（未知键返回 null，由调用方回退）。</param>
    public DelegateTokenManagerRegistry(Func<string, ITokenManager?> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <inheritdoc />
    public ITokenManager? Resolve(string tokenManagerKey)
        => string.IsNullOrEmpty(tokenManagerKey) ? null : _resolver(tokenManagerKey);
}
