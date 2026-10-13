// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// F-2（TK-1）令牌归属键的公开契约：<b>tokenType + appKey + scopeKey</b> 三段式归属模型，
/// 把下游自拼 <c>{tokenType}:{appKey}:{scopeKey}</c> 的需求收敛为一等类型。
/// </summary>
/// <remarks>
/// <para>
/// <b>段语义</b>：<c>tokenType</c> = 令牌类型标识（与 <c>IMudAppContext.GetTokenManager(tokenType)</c>
/// 注册键同口径）；<c>appKey</c> = 应用归属；<c>scopeKey</c> = <b>归属键</b>（如企业微信
/// <c>authCorpId</c>，「一企一份」的路由键）——<b>不是 OAuth scope</b>。
/// </para>
/// <para>
/// <b>双形态（评审修订 #2，关键）</b>：
/// </para>
/// <list type="number">
/// <item><description><b>定位符形态</b>（驱动缓存互通）：作为
/// <see cref="TokenManagerBase.GetOrRefreshTokenAsync(TokenScopeKey, CancellationToken)"/> /
/// <see cref="TokenManagerBase.InvalidateTokenAsync(TokenScopeKey, CancellationToken)"/> 的入参时，
/// 按段映射到既有 <c>string[] scopes</c> 路径——<c>ForScope(t, a, s)</c> → <c>scopes = [s]</c>、
/// <c>ForApp(t, a)</c> / <c>ForShared(t)</c> → <c>scopes = null</c>（"default"）。
/// <c>tokenType</c> / <c>appKey</c> 段<b>只做一致性校验</b>（与管理器声明的令牌类型 / 上下文 AppKey
/// 不符即抛 <see cref="ArgumentException"/>），<b>绝不参与缓存键</b>——与
/// <c>ScopeKeyBuilder</c> 旧路径单缓存互通（同一归属键 ⇒ 同一缓存条目）。</description></item>
/// <item><description><b>编码键形态</b>（替换下游自拼键）：<see cref="ToCompositeKey"/> /
/// <see cref="ToString"/> 输出长度前缀三段式文本（编码委托既有 internal <c>CompositeKey</c>，保持内部实现），
/// 仅供下游当字典键 / 分布式键用，<b>不进</b> <see cref="TokenManagerBase"/> 缓存键。</description></item>
/// </list>
/// <para>
/// 三种归属形态经命名工厂构造：<see cref="ForShared"/>（全租户共享）、<see cref="ForApp"/>（每应用一份）、
/// <see cref="ForScope"/>（按归属键细分，如一企一份）。工厂为唯一构造入口，保证段取值合法；
/// <c>default(TokenScopeKey)</c> 为未构造形态，仅作字段默认值，不应参与比较或编码。
/// </para>
/// </remarks>
public readonly struct TokenScopeKey : IEquatable<TokenScopeKey>
{
    private readonly string _tokenType;
    private readonly string? _appKey;
    private readonly string? _scopeKey;

    private TokenScopeKey(string tokenType, string? appKey, string? scopeKey)
    {
        _tokenType = tokenType;
        _appKey = appKey;
        _scopeKey = scopeKey;
    }

    /// <summary>令牌类型标识（与 <c>IMudAppContext.GetTokenManager(tokenType)</c> 注册键同口径）。</summary>
    public string TokenType => _tokenType;

    /// <summary>应用归属键；<see cref="ForShared"/> 形态为 null（全租户共享，无应用段）。</summary>
    public string? AppKey => _appKey;

    /// <summary>
    /// 归属键（如企业微信 <c>authCorpId</c>）——<b>不是 OAuth scope</b>；
    /// 仅 <see cref="ForScope"/> 形态非 null。
    /// </summary>
    public string? ScopeKey => _scopeKey;

    /// <summary>
    /// 每应用一份归属：<c>tokenType + appKey</c>（无归属键段）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识（不可为 null / 空串）。</param>
    /// <param name="appKey">应用归属键（不可为 null / 空串）。</param>
    /// <exception cref="ArgumentNullException">段为 null。</exception>
    /// <exception cref="ArgumentException">段为空串。</exception>
    public static TokenScopeKey ForApp(string tokenType, string appKey)
    {
        return new TokenScopeKey(
            RequireSegment(tokenType, nameof(tokenType)),
            RequireSegment(appKey, nameof(appKey)),
            scopeKey: null);
    }

    /// <summary>
    /// 按归属键细分归属（如「一企一份」）：<c>tokenType + appKey + scopeKey</c>。
    /// </summary>
    /// <param name="tokenType">令牌类型标识（不可为 null / 空串）。</param>
    /// <param name="appKey">应用归属键（不可为 null / 空串）。</param>
    /// <param name="scopeKey">
    /// 归属键（如企业微信 <c>authCorpId</c>）——<b>不是 OAuth scope</b>（不可为 null / 空串）。
    /// </param>
    /// <exception cref="ArgumentNullException">段为 null。</exception>
    /// <exception cref="ArgumentException">段为空串。</exception>
    public static TokenScopeKey ForScope(string tokenType, string appKey, string scopeKey)
    {
        return new TokenScopeKey(
            RequireSegment(tokenType, nameof(tokenType)),
            RequireSegment(appKey, nameof(appKey)),
            RequireSegment(scopeKey, nameof(scopeKey)));
    }

    /// <summary>
    /// 全租户共享归属：<c>tokenType</c>（无应用段、无归属键段）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识（不可为 null / 空串）。</param>
    /// <exception cref="ArgumentNullException">段为 null。</exception>
    /// <exception cref="ArgumentException">段为空串。</exception>
    public static TokenScopeKey ForShared(string tokenType)
    {
        return new TokenScopeKey(
            RequireSegment(tokenType, nameof(tokenType)),
            appKey: null,
            scopeKey: null);
    }

    /// <summary>
    /// 编码键形态：输出长度前缀三段式文本（<c>{len}:{tokenType}{len}:{appKey}{len}:{scopeKey}</c>，
    /// 编码委托 internal <c>CompositeKey</c>，对任一分段内容天然单射）。
    /// 仅供下游当字典键 / 分布式键用，<b>不进</b> <see cref="TokenManagerBase"/> 缓存键。
    /// </summary>
    /// <returns>长度前缀编码后的三段式归属键文本。</returns>
    public string ToCompositeKey()
    {
        return CompositeKey.Combine(_tokenType, _appKey, _scopeKey);
    }

    /// <summary>与 <see cref="ToCompositeKey"/> 同值（长度前缀三段式文本）。</summary>
    /// <returns>长度前缀编码后的三段式归属键文本。</returns>
    public override string ToString()
    {
        return ToCompositeKey();
    }

    /// <summary>
    /// 定位符形态内部映射：按段投影到既有 <c>string[] scopes</c> 路径的路由入参——
    /// <c>ForScope</c> → <c>[scopeKey]</c>；<c>ForApp</c> / <c>ForShared</c> → null（"default"）。
    /// tokenType / appKey 段不参与此映射（绝不进缓存键）。
    /// </summary>
    internal string[]? ToScopes()
    {
        return _scopeKey == null ? null : new[] { _scopeKey };
    }

    /// <summary>值相等性（三段均按序数比较）。</summary>
    /// <param name="other">比较对象。</param>
    /// <returns>三段全部相等时为 true。</returns>
    public bool Equals(TokenScopeKey other)
    {
        return string.Equals(_tokenType, other._tokenType, StringComparison.Ordinal)
            && string.Equals(_appKey, other._appKey, StringComparison.Ordinal)
            && string.Equals(_scopeKey, other._scopeKey, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is TokenScopeKey other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = _tokenType == null ? 0 : StringComparer.Ordinal.GetHashCode(_tokenType);
            hash = (hash * 397) ^ (_appKey == null ? 0 : StringComparer.Ordinal.GetHashCode(_appKey));
            hash = (hash * 397) ^ (_scopeKey == null ? 0 : StringComparer.Ordinal.GetHashCode(_scopeKey));
            return hash;
        }
    }

    /// <summary>相等运算符。</summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>三段全部相等时为 true。</returns>
    public static bool operator ==(TokenScopeKey left, TokenScopeKey right) => left.Equals(right);

    /// <summary>不等运算符。</summary>
    /// <param name="left">左操作数。</param>
    /// <param name="right">右操作数。</param>
    /// <returns>任一段不等时为 true。</returns>
    public static bool operator !=(TokenScopeKey left, TokenScopeKey right) => !left.Equals(right);

    /// <summary>段合法性守卫：null / 空串无归属语义，构造期拒绝（fail-fast）。</summary>
    private static string RequireSegment(string? value, string paramName)
    {
        if (value == null)
            throw new ArgumentNullException(paramName);
        if (value.Length == 0)
            throw new ArgumentException("TokenScopeKey 的归属段不可为空串。", paramName);
        return value;
    }
}
