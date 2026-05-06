namespace Mud.HttpUtils;

/// <summary>
/// 基于内存的加密令牌存储实现，提供自动加密/解密功能的令牌存储。
/// </summary>
/// <remarks>
/// <para>
/// 此类继承 <see cref="MemoryTokenStore"/> 并实现 <see cref="IEncryptedTokenStore"/> 接口，
/// 在内存存储的基础上增加了自动加密和解密功能。所有写入的令牌数据都会先经过加密处理，
/// 读取时自动解密，确保敏感信息在内存中以密文形式存储。
/// </para>
/// <para>
/// 实现特点：
/// <list type="bullet">
///   <item>自动加密：写入令牌时自动调用 <see cref="IEncryptionProvider.Encrypt"/> 加密</item>
///   <item>自动解密：读取令牌时自动调用 <see cref="IEncryptionProvider.Decrypt"/> 解密</item>
///   <item>透明存储：对调用方完全透明，使用方式与普通存储一致</item>
///   <item>继承特性：保留 <see cref="MemoryTokenStore"/> 的线程安全和过期检查特性</item>
/// </list>
/// </para>
/// <para>
/// 注意：加密操作会带来一定的性能开销，适用于对安全性要求较高的场景。
/// 如果性能敏感且安全要求不高，可直接使用 <see cref="MemoryTokenStore"/>。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 注册到依赖注入容器
/// services.AddSingleton&lt;IEncryptionProvider, AesEncryptionProvider&gt;();
/// services.AddSingleton&lt;IEncryptedTokenStore, MemoryEncryptedTokenStore&gt;();
/// 
/// // 使用存储（加密/解密完全透明）
/// var store = serviceProvider.GetRequiredService&lt;IEncryptedTokenStore&gt;();
/// await store.SetAccessTokenAsync("TenantAccessToken", "sensitive_token", 3600);
/// var token = await store.GetAccessTokenAsync("TenantAccessToken"); // 自动解密返回
/// </code>
/// </example>
public class MemoryEncryptedTokenStore : MemoryTokenStore, IEncryptedTokenStore
{
    private readonly IEncryptionProvider _encryptionProvider;

    /// <summary>
    /// 初始化 <see cref="MemoryEncryptedTokenStore"/> 类的新实例。
    /// </summary>
    /// <param name="encryptionProvider">加密提供程序，用于令牌的加密和解密。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="encryptionProvider"/> 为 null 时抛出。</exception>
    public MemoryEncryptedTokenStore(IEncryptionProvider encryptionProvider)
    {
        _encryptionProvider = encryptionProvider ?? throw new ArgumentNullException(nameof(encryptionProvider));
    }

    /// <summary>
    /// 获取一个值，指示此存储实例是否已启用加密。
    /// </summary>
    /// <value>始终返回 <c>true</c>。</value>
    public bool IsEncryptionEnabled => true;

    /// <summary>
    /// 异步获取指定令牌类型的访问令牌（自动解密）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解密后的访问令牌字符串，如果不存在或已过期则返回 null。</returns>
    /// <remarks>
    /// 此方法先从基类获取加密后的令牌，然后使用 <see cref="IEncryptionProvider"/> 进行解密。
    /// 如果存储中不存在该令牌，则直接返回 null 而不进行解密操作。
    /// </remarks>
    public override async Task<string?> GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        var encrypted = await base.GetAccessTokenAsync(tokenType, cancellationToken).ConfigureAwait(false);
        if (encrypted == null)
            return null;

        return _encryptionProvider.Decrypt(encrypted);
    }

    /// <summary>
    /// 异步保存指定令牌类型的访问令牌（自动加密）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="accessToken">访问令牌（明文）。</param>
    /// <param name="expiresInSeconds">令牌有效时长（秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 保存前会使用 <see cref="IEncryptionProvider"/> 对令牌进行加密，
    /// 然后将加密后的数据存入基类存储。
    /// </remarks>
    public override async Task SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
    {
        var encrypted = _encryptionProvider.Encrypt(accessToken);
        await base.SetAccessTokenAsync(tokenType, encrypted, expiresInSeconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步获取指定令牌类型的刷新令牌（自动解密）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解密后的刷新令牌字符串，如果不存在则返回 null。</returns>
    /// <remarks>
    /// 此方法先从基类获取加密后的刷新令牌，然后使用 <see cref="IEncryptionProvider"/> 进行解密。
    /// 刷新令牌没有过期时间检查，只要存在就会尝试解密并返回。
    /// </remarks>
    public override async Task<string?> GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken = default)
    {
        var encrypted = await base.GetRefreshTokenAsync(tokenType, cancellationToken).ConfigureAwait(false);
        if (encrypted == null)
            return null;

        return _encryptionProvider.Decrypt(encrypted);
    }

    /// <summary>
    /// 异步保存指定令牌类型的刷新令牌（自动加密）。
    /// </summary>
    /// <param name="tokenType">令牌类型标识符。</param>
    /// <param name="refreshToken">刷新令牌（明文）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 保存前会使用 <see cref="IEncryptionProvider"/> 对刷新令牌进行加密，
    /// 然后将加密后的数据存入基类存储。
    /// </remarks>
    public override async Task SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken = default)
    {
        var encrypted = _encryptionProvider.Encrypt(refreshToken);
        await base.SetRefreshTokenAsync(tokenType, encrypted, cancellationToken).ConfigureAwait(false);
    }
}
