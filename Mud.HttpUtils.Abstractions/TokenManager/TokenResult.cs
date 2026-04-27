// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 令牌结果结构体，封装了令牌获取操作的结果信息。
/// </summary>
/// <remarks>
/// 该结构体是不可变的（readonly struct），用于安全地传递令牌信息。
/// 包含访问令牌、过期时间和作用域等核心信息。
/// <para>
/// 适用场景：
/// <list type="bullet">
///   <item>令牌管理器返回令牌信息</item>
///   <item>令牌刷新操作的结果传递</item>
///   <item>令牌有效性检查和过期判断</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 创建令牌结果
/// var tokenResult = new TokenResult("access_token_123", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(), "read write");
/// 
/// // 检查是否为空
/// if (!tokenResult.IsEmpty)
/// {
///     Console.WriteLine($"令牌: {tokenResult.AccessToken}");
/// }
/// 
/// // 检查是否即将过期（默认5分钟内）
/// if (tokenResult.IsExpiringSoon())
/// {
///     Console.WriteLine("令牌即将过期，需要刷新");
/// }
/// 
/// // 使用静态空值
/// var emptyResult = TokenResult.Empty;
/// </code>
/// </example>
/// <seealso cref="CredentialToken"/>
/// <seealso cref="ITokenManager"/>
public readonly struct TokenResult
{
    /// <summary>
    /// 获取访问令牌字符串。
    /// </summary>
    /// <value>访问令牌，用于API认证的Bearer令牌。</value>
    /// <remarks>
    /// 此属性永远不会为 <c>null</c>，在构造函数中会进行空值检查。
    /// </remarks>
    public string AccessToken { get; }

    /// <summary>
    /// 获取令牌的过期时间（Unix时间戳，毫秒）。
    /// </summary>
    /// <value>过期时间的Unix时间戳（毫秒），0表示无过期时间。</value>
    /// <remarks>
    /// 该值表示令牌失效的绝对时间点。可以使用 <see cref="IsExpiringSoon"/> 方法检查是否即将过期。
    /// </remarks>
    public long ExpireTime { get; }

    /// <summary>
    /// 获取令牌的作用域。
    /// </summary>
    /// <value>令牌的作用域，可能为 <c>null</c>。</value>
    /// <remarks>
    /// 作用域定义了令牌的权限范围，例如 "read", "write", "admin" 等。
    /// 不同的认证系统可能使用不同的作用域格式。
    /// </remarks>
    public string? Scope { get; }

    /// <summary>
    /// 获取一个值，指示令牌结果是否为空。
    /// </summary>
    /// <value>如果 <see cref="AccessToken"/> 为 <c>null</c> 或空字符串，则为 <c>true</c>；否则为 <c>false</c>。</value>
    /// <remarks>
    /// 此属性提供了一种快速检查令牌结果是否有效的方法。
    /// 通常用于判断令牌获取操作是否成功。
    /// </remarks>
    public bool IsEmpty => string.IsNullOrEmpty(AccessToken);

    /// <summary>
    /// 初始化 <see cref="TokenResult"/> 结构体的新实例。
    /// </summary>
    /// <param name="accessToken">访问令牌，不能为 <c>null</c>。</param>
    /// <param name="expireTime">过期时间（Unix时间戳，毫秒）。</param>
    /// <param name="scope">可选的令牌作用域。</param>
    /// <exception cref="System.ArgumentNullException">当 <paramref name="accessToken"/> 为 <c>null</c> 时抛出。</exception>
    /// <remarks>
    /// 构造函数会验证 <paramref name="accessToken"/> 参数，确保其不为 <c>null</c>。
    /// </remarks>
    public TokenResult(string accessToken, long expireTime, string? scope = null)
    {
        AccessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        ExpireTime = expireTime;
        Scope = scope;
    }

    /// <summary>
    /// 获取一个空的令牌结果实例。
    /// </summary>
    /// <value>表示空令牌结果的 <see cref="TokenResult"/> 实例。</value>
    /// <remarks>
    /// 此静态属性返回一个默认的 <see cref="TokenResult"/> 实例，其 <see cref="AccessToken"/> 为 <c>null</c>。
    /// 通常用于表示令牌获取失败或无效的情况。
    /// </remarks>
    public static TokenResult Empty => default;

    /// <summary>
    /// 从 <see cref="CredentialToken"/> 创建 <see cref="TokenResult"/> 实例。
    /// </summary>
    /// <param name="token">凭据令牌对象。</param>
    /// <returns>
    /// 如果 <paramref name="token"/> 为 <c>null</c> 或其 <see cref="CredentialToken.AccessToken"/> 为空，则返回 <see cref="Empty"/>；
    /// 否则返回包含令牌信息的 <see cref="TokenResult"/> 实例。
    /// </returns>
    /// <remarks>
    /// 此方法提供了一种从 <see cref="CredentialToken"/> 转换为 <see cref="TokenResult"/> 的便捷方式。
    /// </remarks>
    /// <example>
    /// <code>
    /// var credentialToken = new CredentialToken { AccessToken = "token123", Expire = 1234567890, Scope = "read" };
    /// var result = TokenResult.FromCredentialToken(credentialToken);
    /// </code>
    /// </example>
    public static TokenResult FromCredentialToken(CredentialToken token)
    {
        if (token == null || string.IsNullOrEmpty(token.AccessToken))
            return Empty;

        return new TokenResult(token.AccessToken, token.Expire, token.Scope);
    }

    /// <summary>
    /// 检查令牌是否即将过期。
    /// </summary>
    /// <param name="thresholdSeconds">过期阈值（秒），默认为 300 秒（5分钟）。</param>
    /// <returns>如果令牌将在指定的阈值时间内过期，则为 <c>true</c>；否则为 <c>false</c>。</returns>
    /// <remarks>
    /// 此方法用于判断令牌是否需要在过期前进行刷新。
    /// <para>
    /// 特殊处理：
    /// <list type="bullet">
    ///   <item>如果 <see cref="ExpireTime"/> 为 0 或负数，则认为令牌即将过期</item>
    ///   <item>比较基于当前UTC时间与过期时间的差值</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 检查是否在5分钟内过期
    /// if (tokenResult.IsExpiringSoon(300))
    /// {
    ///     // 刷新令牌
    /// }
    /// 
    /// // 检查是否在1分钟内过期
    /// if (tokenResult.IsExpiringSoon(60))
    /// {
    ///     // 立即刷新令牌
    /// }
    /// </code>
    /// </example>
    public bool IsExpiringSoon(int thresholdSeconds = 300)
    {
        if (ExpireTime <= 0)
            return true;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return ExpireTime - (thresholdSeconds * 1000L) <= now;
    }
}
