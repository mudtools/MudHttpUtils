// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// U-1：下游<b>旧存储值格式</b>的解码探针钩子 —— 供存量格式既非「绝对毫秒戳」也非「相对秒」的下游
/// （如自带前缀的自定义形态）注入自己的判别逻辑。
/// </summary>
/// <remarks>
/// <para>
/// <b>调用时机</b>：<see cref="MigrationTokenStoreCodec"/> 的内建判别（绝对毫秒戳 / 相对秒）对给定值
/// 解析失败时才回调本探针（内建优先，钩子兜底）；探针返回 <c>false</c> 时整体判为解析失败（返回 null，不抛异常）。
/// </para>
/// <para>
/// <b>契约</b>：与 <see cref="ITokenStoreCodec.Decode"/> 同口径 —— 实现对非法格式返回 <c>false</c>
/// 且不得抛异常；解析出的 <paramref name="expiresInSeconds"/> 语义为<b>剩余有效秒数</b>
/// （≤ 0 表示无 TTL 信息 / 已过期，由调用方按既有约定处置）。
/// </para>
/// </remarks>
public interface ILegacyFormatProbe
{
    /// <summary>尝试按下游旧格式解析存储值。</summary>
    /// <param name="encoded">存储字符串（非空，由调用方保证）。</param>
    /// <param name="token">解析出的令牌明文；返回 <c>false</c> 时无意义。</param>
    /// <param name="expiresInSeconds">解析出的剩余有效秒数；返回 <c>false</c> 时无意义。</param>
    /// <returns>解析成功返回 <c>true</c>；非本格式 / 非法输入返回 <c>false</c>（不抛异常）。</returns>
    bool TryDecodeLegacy(string encoded, out string? token, out long expiresInSeconds);
}

/// <summary>
/// U-1：官方<b>迁移期</b>双读编解码器 —— 供从「绝对毫秒戳」旧形态（两下游私有 codec 各自发明的
/// <c>{expireMs}|{token}</c> 存量数据）迁移到官方默认形态 <c>{expiresInSeconds}|{token}</c> 的下游使用。
/// </summary>
/// <remarks>
/// <para>
/// <b>生命周期定位：迁移期组件</b>。旧数据按条目自然过期收敛（最长一个令牌 TTL 周期）后，
/// 应切回 <see cref="DefaultTokenStoreCodec"/>；官方 README 给出
/// 「迁移期配置 → 收敛验证（SCAN 抽样值形态）→ 切回默认」三步指引。
/// </para>
/// <para>
/// <b>C-2 裁决对表</b>：<see cref="Encode"/> 与 <see cref="DefaultTokenStoreCodec"/> 逐字节一致
/// （单写相对秒，委托默认实例实现，杜绝复制粘贴漂移）——不违反「官方不提供绝对戳变体」裁决；
/// 仅 <see cref="Decode"/> 做双读。
/// </para>
/// <para>
/// <b>Decode 双读判别</b>（启发式，阈值可参数化）：
/// <list type="bullet">
/// <item><description>前导数值 ≥ 阈值（默认 10^10，两下游实测钉死：毫秒时间戳 13 位 ≈ 1.7×10^12、
/// 相对秒 ≤ 10 位且实际不超过数年 ≈ 10^8）→ 判为<b>旧绝对毫秒戳</b>，按
/// <c>expiresAtMs - now</c> 换算剩余秒投影（<b>floor 取整</b>：已过期旧值投影为负数，
/// 不会被「无 TTL 信息 = 0」语义吞掉）。</description></item>
/// <item><description>前导数值 &lt; 阈值 → 相对秒直读（与默认 codec 行为一致）。</description></item>
/// <item><description>内建判别失败（无分隔符 / 非数值前导段）→ 回调 <see cref="ILegacyFormatProbe"/>
/// 钩子；无钩子或钩子也失败 → 返回 <c>null</c>（miss，不抛异常）。</description></item>
/// </list>
/// </para>
/// <para>
/// <b>已知局限（文档钉死）</b>：若下游旧格式为「绝对<b>秒</b>戳」（10 位 ≈ 1.7×10^9 &lt; 阈值），
/// 会被误判为相对秒 —— 该情形请经 <see cref="ILegacyFormatProbe"/> 显式接管；
/// 相对秒实际不可能达到阈值量级（≈ 317 年），无反向误判面。
/// </para>
/// </remarks>
public sealed class MigrationTokenStoreCodec : ITokenStoreCodec
{
    /// <summary>
    /// 旧绝对毫秒戳判别阈值的默认值（10^10）：≥ 该值判为旧格式，&lt; 该值判为相对秒。
    /// </summary>
    public const long DefaultLegacyAbsoluteMillisecondsThreshold = 100_000_000_000L;

    /// <summary>共享默认实例（无状态、线程安全；内建判别 + 默认阈值）。</summary>
    public static MigrationTokenStoreCodec Instance { get; } = new();

    private readonly ITokenStoreCodec _newFormatCodec;
    private readonly long _threshold;
    private readonly ILegacyFormatProbe? _legacyProbe;
    private readonly Func<DateTimeOffset>? _utcNowProvider;

    /// <summary>
    /// 创建迁移期双读编解码器。
    /// </summary>
    /// <param name="legacyAbsoluteMillisecondsThreshold">
    /// 旧格式判别阈值；为 <c>null</c> 时取 <see cref="DefaultLegacyAbsoluteMillisecondsThreshold"/>（10^10）。
    /// </param>
    /// <param name="legacyProbe">旧格式探针钩子；为 <c>null</c> 时不启用钩子兜底。</param>
    /// <param name="newFormatCodec">
    /// 新格式编解码器（<see cref="Encode"/> 全权委托、<see cref="Decode"/> 直读分支复用）；
    /// 为 <c>null</c> 时取 <see cref="DefaultTokenStoreCodec.Instance"/>。
    /// </param>
    /// <param name="utcNowProvider">
    /// UTC 时钟注入（旧格式投影 <c>expiresAtMs - now</c> 用）；为 <c>null</c> 时取
    /// <see cref="DateTimeOffset.UtcNow"/>。测试 / 确定性解码场景可注入固定时钟。
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">阈值为非正数。</exception>
    public MigrationTokenStoreCodec(
        long? legacyAbsoluteMillisecondsThreshold = null,
        ILegacyFormatProbe? legacyProbe = null,
        ITokenStoreCodec? newFormatCodec = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        if (legacyAbsoluteMillisecondsThreshold.HasValue && legacyAbsoluteMillisecondsThreshold.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(legacyAbsoluteMillisecondsThreshold));

        _threshold = legacyAbsoluteMillisecondsThreshold ?? DefaultLegacyAbsoluteMillisecondsThreshold;
        _legacyProbe = legacyProbe;
        _newFormatCodec = newFormatCodec ?? DefaultTokenStoreCodec.Instance;
        _utcNowProvider = utcNowProvider;
    }

    /// <inheritdoc />
    /// <remarks>与 <see cref="DefaultTokenStoreCodec"/> 逐字节一致（单写相对秒，C-2 裁决对表项）。</remarks>
    public string? Encode(string? token, long expiresInSeconds)
    {
        return _newFormatCodec.Encode(token, expiresInSeconds);
    }

    /// <inheritdoc />
    /// <remarks>双读判别规则见类型 remarks；非法格式返回 <c>null</c>（miss，不抛异常）。</remarks>
    public (string? Token, long ExpiresInSeconds)? Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;

        var decoded = _newFormatCodec.Decode(encoded);
        if (decoded.HasValue)
        {
            var (token, value) = decoded.Value;
            if (value >= _threshold)
            {
                // 旧绝对毫秒戳：按 expiresAtMs - now 投影剩余秒（floor 取整，过期旧值投影为负）。
                var nowMs = (_utcNowProvider?.Invoke() ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
                var diff = value - nowMs;
                var remaining = diff / 1000;
                if (diff % 1000 != 0 && diff < 0)
                    remaining--;   // C# 整除向零取整，负余数需再退一步才是 floor
                return (token, remaining);
            }

            // 相对秒直读（含 0 / 负数 = 无 TTL 信息的既有语义）。
            return (token, value);
        }

        // 内建判别失败 → 钩子兜底；无钩子或钩子也失败 → miss。
        if (_legacyProbe != null && _legacyProbe.TryDecodeLegacy(encoded, out var probeToken, out var probeSeconds))
            return (probeToken, probeSeconds);

        return null;
    }
}
