// -----------------------------------------------------------------------
// 作者：Mud Studio 版权所有 (c) Mud Studio 2026
// Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
// 本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
// 不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Collections.Concurrent;

namespace Mud.HttpUtils;

/// <summary>
/// 字典式 <see cref="ITokenManagerRegistry"/> 官方实现（键 → 令牌管理器的静态映射）。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要</b>：核心的 <see cref="DelegateTokenManagerRegistry"/> 是纯委托透传，
/// 键 → 管理器的映射隐藏在委托内部，注册期<b>不可枚举</b> ⇒ 多产品线令牌键冲突
/// （如 <c>Wechat.Channels.AccessToken</c> 与公众号令牌键相撞）只能在运行期表现为
/// <c>Resolve</c> 命中错误管理器 / 返回 null，errcode 令牌自愈<b>静默失效</b>。
/// 本实现把键放到宿主侧可见的字典里，冲突在<b>注册期</b>即可检出。
/// </para>
/// <para>
/// <b>重复键语义</b>（键比较一律 <see cref="StringComparer.Ordinal"/>，与 AppKey 序数语义一致，
/// 大小写不同视为不同键）：
/// <list type="number">
/// <item><description>冲突键记录到 <see cref="DuplicateKeys"/>，并构造含「原管理器类型 → 新管理器类型」的诊断消息；</description></item>
/// <item><description>若提供 <c>duplicateKeyDiagnostics</c> 回调则回调该消息（注册期诊断出口）；</description></item>
/// <item><description><c>throwOnDuplicateKey = true</c>（默认）时随即抛出 <see cref="InvalidOperationException"/>（fail-fast）；
/// <c>false</c> 时<b>后键覆盖</b>（后注册者胜），由启动期校验
/// （<c>AppManagementStartupValidator</c> + <c>MudHttpAppManagementOptions.AllowDuplicateTokenManagerKeys</c>）
/// 决定升级为 Error 抛出还是降级 Warning。</description></item>
/// </list>
/// </para>
/// <para>
/// <b>典型接线</b>：<c>new TokenManagerKeyRegistry()</c> 逐键 <see cref="Register"/> 后
/// <c>services.AddSingleton&lt;ITokenManagerRegistry&gt;(registry)</c>；
/// 或使用 <c>AddTokenManagerKeyRegistry</c> 帮助器（自动挂接启动期升级）。
/// </para>
/// </remarks>
public sealed class TokenManagerKeyRegistry : ITokenManagerRegistry
{
    private readonly ConcurrentDictionary<string, ITokenManager> _map = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _duplicateKeys = new(StringComparer.Ordinal);
    private readonly Action<string>? _duplicateKeyDiagnostics;
    private readonly bool _throwOnDuplicateKey;

    /// <summary>
    /// 初始化令牌管理器键注册表。
    /// </summary>
    /// <param name="throwOnDuplicateKey">
    /// 重复键是否直接抛出（默认 <c>true</c>，fail-fast）。
    /// 传 <c>false</c> 时改为「记录 + 诊断 + 后键覆盖」，由启动期校验决定最终严重级别
    /// （<c>MudHttpAppManagementOptions.AllowDuplicateTokenManagerKeys</c>：默认 Error 抛出，逃生开关 Warning）。
    /// </param>
    /// <param name="duplicateKeyDiagnostics">
    /// 重复键的注册期诊断回调（可选，接收完整诊断消息）。为 null 且 <paramref name="throwOnDuplicateKey"/> 为 false 时仅记录不输出。
    /// </param>
    public TokenManagerKeyRegistry(
    bool throwOnDuplicateKey = true,
    Action<string>? duplicateKeyDiagnostics = null)
    {
        _throwOnDuplicateKey = throwOnDuplicateKey;
        _duplicateKeyDiagnostics = duplicateKeyDiagnostics;
    }

    /// <summary>
    /// 已检出的重复键列表（序数比较；同一键多次冲突只记录一次）。
    /// 供启动期校验升级为 Error（默认）或降级为 Warning（逃生开关）。
    /// </summary>
    public IReadOnlyCollection<string> DuplicateKeys => _duplicateKeys.Keys.ToArray();

    /// <summary>
    /// 注册令牌管理器。键比较为序数（大小写敏感）。
    /// </summary>
    /// <param name="tokenManagerKey">令牌管理器键（非空）。</param>
    /// <param name="manager">令牌管理器实例。</param>
    /// <exception cref="ArgumentException"><paramref name="tokenManagerKey"/> 为 null 或空白。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="manager"/> 为 null。</exception>
    /// <exception cref="InvalidOperationException">
    /// 重复键且 <c>throwOnDuplicateKey = true</c>（默认）时抛出。
    /// </exception>
    public void Register(string tokenManagerKey, ITokenManager manager)
    {
        if (string.IsNullOrWhiteSpace(tokenManagerKey))
            throw new ArgumentException("令牌管理器键不能为空。", nameof(tokenManagerKey));
        if (manager is null)
            throw new ArgumentNullException(nameof(manager));

        // 后键覆盖语义：AddOrUpdate 一步完成"冲突检出 + 覆盖"。
        _map.AddOrUpdate(
        tokenManagerKey,
        _ => manager,
        (key, existing) =>
        {
            // 同一键多次冲突只记录一次（TryAdd 幂等）。
            _duplicateKeys.TryAdd(key, 0);

            var message =
     $"令牌管理器键 '{AppKeyValidator.ToSafeText(key)}' 重复注册（序数比较）：" +
     $"原管理器 {existing.GetType().Name} → 新管理器 {manager.GetType().Name}。" +
     "重复键会让 Resolve 命中错误的管理器（errcode 令牌自愈静默失效），请改用不同键或移除重复注册。";

            _duplicateKeyDiagnostics?.Invoke(message);

            if (_throwOnDuplicateKey)
                throw new InvalidOperationException(message);

            return manager; // 后键覆盖
        });
    }

    /// <inheritdoc />
    public ITokenManager? Resolve(string tokenManagerKey)
    => !string.IsNullOrEmpty(tokenManagerKey) && _map.TryGetValue(tokenManagerKey!, out var manager)
    ? manager
    : null;
}
