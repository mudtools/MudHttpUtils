// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 基于内存的用户级令牌存储实现，支持按用户标识隔离令牌数据。
/// </summary>
/// <remarks>
/// <para>
/// 此类实现 <see cref="IUserTokenStore"/> 接口（继承自 <see cref="ITokenStore"/>），
/// 提供按用户 ID 隔离的令牌存储能力。每个用户拥有独立的令牌存储空间，
/// 不同用户的同名令牌类型互不干扰。
/// </para>
/// <para>
/// 实现特点：
/// <list type="bullet">
///   <item>用户隔离：每个用户的令牌存储在独立的字典中</item>
///   <item>线程安全：使用嵌套的 <see cref="ConcurrentDictionary{String, TokenEntry}"/> 确保并发访问安全</item>
///   <item>过期检查：获取令牌时自动验证过期时间</item>
///   <item>桶数有界：写入路径在用户桶数超过 1 万时惰性清扫空壳桶，防海量短命 userId 造成内存滞留（M6-HC-28）</item>
///   <item>显式接口实现：<see cref="ITokenStore"/> 的无用户 ID 方法通过显式接口实现提供，调用时需通过接口类型引用</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 注册到依赖注入容器
/// services.AddSingleton&lt;IUserTokenStore, MemoryUserTokenStore&gt;();
/// 
/// // 使用存储
/// var store = serviceProvider.GetRequiredService&lt;IUserTokenStore&gt;();
/// await store.SetAccessTokenAsync("user123", "UserAccessToken", "access_token", 3600);
/// var token = await store.GetAccessTokenAsync("user123", "UserAccessToken");
/// </code>
/// </example>
public class MemoryUserTokenStore : IUserTokenStore
{
    // SR-H4（P1.5，D5）userId 外层比较器改 Ordinal：
    // 原实现 OrdinalIgnoreCase 令 "User1"/"user1"/大小写不同的邮箱型 ID 共享同一令牌桶（跨用户读取令牌），
    // 且与 KeyedLockTable（Ordinal）、MemoryCacheTokenCache._keys（默认 Ordinal）两套身份判定分裂。
    // userId 的大小写归一化责任在调用方入口，存储与缓存层一律 Ordinal。
    // 内层（tokenType）保留 OrdinalIgnoreCase：tokenType 语义不区分大小写，与 MemoryTokenStore 一致。
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, MemoryTokenStore.TokenEntry>> _userStore = new(StringComparer.Ordinal);

    // M6-HC-28：用户桶数上限。超过该值后，下一次写入会顺带触发一次全量空壳桶清扫。
    // 取 1 万：单个空 ConcurrentDictionary 壳约百字节级，1 万个壳的内存占用仍在可接受范围，
    // 而全量扫描（1 万次 IsEmpty 判定，无分配）在命中阈值时的一次性成本可忽略。
    internal const int UserBucketSweepThreshold = 10000;

    // 全量清扫门控（0=空闲，1=进行中）：并发写入者同时越阈值时只放行一次扫描，避免惊群。
    private int _bucketSweepInProgress;

    /// <summary>
    /// M6-HC-28：测试观测钩子（经 <c>InternalsVisibleTo</c>）—— 当前用户桶数量，用于断言「桶数有界」。
    /// </summary>
    internal int UserBucketCount => _userStore.Count;

    /// <summary>
    /// M6-HC-28：惰性空壳桶清扫 —— 仅在桶数超过 <see cref="UserBucketSweepThreshold"/> 时触发全量扫描，
    /// 移除并发 <see cref="RemoveAsync"/> 与过期移除后残留的空内层字典（这些正是桶数无界增长的唯一来源）。
    /// </summary>
    /// <remarks>
    /// 弱一致：并发写入者可能刚向某个空桶填充条目——<c>IsEmpty</c> 为真才移除，误删由下次 Set 恢复（同 SR-L5 口径）。
    /// 未采用后台心跳清扫：令牌存储是纯内存实现，引入 <c>TokenRefreshBackgroundService</c> 依赖会污染
    /// <see cref="ITokenStore"/> 的构造契约（其可在无后台服务的宿主中单独注册）。
    /// </remarks>
    private void SweepEmptyBucketsIfNeeded()
    {
        if (_userStore.Count <= UserBucketSweepThreshold) return;
        if (Interlocked.Exchange(ref _bucketSweepInProgress, 1) == 1) return;

        try
        {
            foreach (var pair in _userStore)
            {
                if (pair.Value.IsEmpty)
                    _userStore.TryRemove(pair.Key, out _);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _bucketSweepInProgress, 0);
        }
    }

    Task<string?> ITokenStore.GetAccessTokenAsync(string tokenType, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task ITokenStore.SetAccessTokenAsync(string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task<string?> ITokenStore.GetRefreshTokenAsync(string tokenType, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task ITokenStore.SetRefreshTokenAsync(string tokenType, string refreshToken, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task ITokenStore.RemoveAsync(string tokenType, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("MemoryUserTokenStore 不支持无用户 ID 的操作，请使用带 userId 参数的重载。");
    }

    Task<IEnumerable<string>> ITokenStore.GetTokenTypesAsync(CancellationToken cancellationToken)
    {
        var allTypes = _userStore.Values
            .SelectMany(dict => dict.Keys)
            .Distinct()
            .ToList();
        return Task.FromResult<IEnumerable<string>>(allTypes);
    }

    /// <summary>
    /// 清空全部用户令牌缓存条目。
    /// </summary>
    /// <param name="cancellationToken">取消令牌（内存实现不检查取消，仅为契约对齐）。</param>
    /// <returns>表示清空操作完成的任务。</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _userStore.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步获取指定用户和令牌类型的访问令牌。
    /// </summary>
    public Task<string?> GetAccessTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens) &&
            userTokens.TryGetValue(tokenType, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
                return Task.FromResult<string?>(entry.AccessToken);

            // 过期条目条件移除（按引用比对），避免陈旧条目滞留内存直到 ClearUserAsync
#if NET5_0_OR_GREATER
            userTokens.TryRemove(new KeyValuePair<string, MemoryTokenStore.TokenEntry>(tokenType, entry));
#else
            // ns2.0 无 TryRemove(KeyValuePair) 重载，回退普通移除（弱一致：误删会被下次 Set 恢复）
            userTokens.TryRemove(tokenType, out _);
#endif
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// 异步保存指定用户和令牌类型的访问令牌。
    /// </summary>
    public Task SetAccessTokenAsync(string userId, string tokenType, string accessToken, long expiresInSeconds, CancellationToken cancellationToken = default)
    {
        var userTokens = _userStore.GetOrAdd(userId, _ => new ConcurrentDictionary<string, MemoryTokenStore.TokenEntry>(StringComparer.OrdinalIgnoreCase));

        // M2-#15：AddOrUpdate + 不可变条目派生 —— 与 MemoryTokenStore 同一模式，防并发丢更新
        userTokens.AddOrUpdate(tokenType,
            _ => new MemoryTokenStore.TokenEntry(accessToken, null, DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)),
            (_, existing) => existing.WithAccessToken(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)));

        // M6-HC-28：写入路径顺带做惰性空壳桶清扫。必须放在本次 AddOrUpdate 之后 ——
        // 此时自身桶已非空，扫描不会回收自身刚创建的空桶（否则 AddOrUpdate 会写入已脱离 _userStore 的孤儿字典，
        // 令牌静默丢失）。桶数未超阈值时为一次 int 比较，不产生扫描。
        SweepEmptyBucketsIfNeeded();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步获取指定用户和令牌类型的刷新令牌。
    /// </summary>
    public Task<string?> GetRefreshTokenAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens) &&
            userTokens.TryGetValue(tokenType, out var entry))
        {
            return Task.FromResult(entry.RefreshToken);
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// 异步保存指定用户和令牌类型的刷新令牌。
    /// </summary>
    public Task SetRefreshTokenAsync(string userId, string tokenType, string refreshToken, CancellationToken cancellationToken = default)
    {
        var userTokens = _userStore.GetOrAdd(userId, _ => new ConcurrentDictionary<string, MemoryTokenStore.TokenEntry>(StringComparer.OrdinalIgnoreCase));

        // M2-#15：不可变条目派生（保留 AccessToken/ExpiresAt）
        userTokens.AddOrUpdate(tokenType,
            _ => new MemoryTokenStore.TokenEntry(null, refreshToken, DateTimeOffset.MaxValue),
            (_, existing) => existing.WithRefreshToken(refreshToken));

        // M6-HC-28：同 SetAccessTokenAsync，写入后顺带惰性清扫空壳桶
        SweepEmptyBucketsIfNeeded();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步移除指定用户和令牌类型的所有令牌数据。
    /// </summary>
    public Task RemoveAsync(string userId, string tokenType, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens))
        {
            userTokens.TryRemove(tokenType, out _);

            // SR-L5（P3.8，D14）空内层字典清扫：消除海量短命 userId 的空字典壳滞留。
            // 竞态弱一致可接受——误删（刚被并发 Set 重新填充即被移除）由下次 Set 恢复；
            // 外层 TryRemove(key) 重载在 ns2.0 一直可用（§0.3-V4），无条件编译。
            if (userTokens.IsEmpty)
                _userStore.TryRemove(userId, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步获取指定用户的所有令牌类型标识符。
    /// </summary>
    public Task<IEnumerable<string>> GetTokenTypesAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_userStore.TryGetValue(userId, out var userTokens))
        {
            return Task.FromResult<IEnumerable<string>>([.. userTokens.Keys]);
        }

        return Task.FromResult<IEnumerable<string>>([]);
    }

    /// <summary>
    /// 异步移除指定用户的所有令牌数据。
    /// </summary>
    public Task ClearUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        _userStore.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}
