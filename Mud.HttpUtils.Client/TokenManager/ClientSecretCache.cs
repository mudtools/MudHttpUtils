// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// P1.8（TK-13）客户端密钥 TTL 缓存：取代 <c>Lazy&lt;Task&lt;string?&gt;&gt;</c>，支持按 TTL 轮换密钥，
/// 且工厂故障不缓存（下一次调用重新解析）。
/// </summary>
/// <remarks>
/// <para>
/// 原 <see cref="System.Lazy{T}"/> 永久缓存解析结果，密钥轮换不生效；若解析 Task 故障，故障结果也会被永久缓存。
/// 本实现改为：命中缓存且未过期直接返回；未命中/已过期则进入信号量闸内重新解析。
/// </para>
/// <para>
/// 线程安全：<see cref="SemaphoreSlim"/> 确保同一时刻只有一个线程执行工厂解析并写入缓存，
/// 避免缓存击穿（Stampede）；读取路径通过 <see cref="Volatile.Read(ref long)"/> 保证 <c>_expiresAtTicks</c>
/// 跨线程可见性。
/// </para>
/// </remarks>
internal sealed class ClientSecretCache
{
    private readonly TimeSpan _ttl;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _value;
    private long _expiresAtTicks;

    /// <summary>
    /// 初始化 <see cref="ClientSecretCache"/> 实例。
    /// </summary>
    /// <param name="ttl">缓存有效期。<see cref="TimeSpan.Zero"/> 表示不缓存（每次重新解析）。</param>
    public ClientSecretCache(TimeSpan ttl)
    {
        _ttl = ttl;
    }

    /// <summary>
    /// 获取缓存的密钥；未命中或已过期时通过 <paramref name="factory"/> 解析并写入缓存。
    /// </summary>
    /// <param name="factory">解析工厂（内部应已 try/catch 回退配置值，不应抛出）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析出的密钥。</returns>
    public async Task<string?> GetAsync(Func<Task<string?>> factory, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        if (_value != null && now < Volatile.Read(ref _expiresAtTicks))
            return _value;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 双重检查：等待闸期间可能有其他线程已写入有效缓存
            now = DateTimeOffset.UtcNow.UtcTicks;
            if (_value != null && now < _expiresAtTicks)
                return _value;

            var resolved = await factory().ConfigureAwait(false);
            _value = resolved;
            _expiresAtTicks = _ttl > TimeSpan.Zero ? now + _ttl.Ticks : long.MaxValue;
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }
}