// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 存储值信封（v2 形态）的<b>三种语义态</b> —— 把默认形态中被合并的「无 TTL 信息」与「已过期」分离。
/// </summary>
/// <remarks>
/// <para>
/// 默认形态 <c>{expiresInSeconds}|{token}</c> 的「相对秒 ≤ 0」（由
/// <see cref="ITokenStoreCodec.Encode"/> 契约钉死为「无 TTL 信息」，<c>SetRefreshTokenAsync</c> 依赖该语义）
/// 同一个取值同时承担了「无 TTL 信息」与「已过期」两种含义，导致：
/// ① 过期态<b>写不出来</b>（只能靠旧格式字面量模拟，新格式永远无法构造过期样本）；
/// ② 已过期值落到存储侧获得兜底 TTL（如 Redis 的 30 天僵尸 TTL），只能靠读路径判空兜底。
/// </para>
/// <para>
/// v2 信封把三态显式分离：有效（相对秒 &gt; 0）/ 无 TTL 信息（相对秒 ≤ 0）/ 显式已过期（<c>v2:expired|</c>）。
/// 0 值语义<b>不动</b>（仍为「无 TTL 信息」），第三态由独立标记承担。
/// </para>
/// </remarks>
public enum TokenStoreEnvelopeState
{
    /// <summary>有效值：携带相对剩余秒数（&gt; 0）。</summary>
    Active = 0,

    /// <summary>无 TTL 信息：令牌有效但写入方未给出剩余时长（读侧按存储兜底 TTL 处置，与默认形态 0 值同义）。</summary>
    NoTtlInformation = 1,

    /// <summary>
    /// 显式「已过期」：读侧按<b>无令牌</b>（miss / 等价于删键）处置。
    /// 该态不携带可消费令牌 —— 语义就是「此处有个值，但它已经过期」，与「此处没有值」在<b>下游可区分</b>。
    /// </summary>
    Expired = 2,
}

/// <summary>
/// <b>可选</b>的 v2 版本化信封编解码器 —— 在默认形态之外提供自描述前缀（<c>v2:</c>）与
/// 显式过期态，供「过期态需要被表达 / 需要与缺失态区分」的下游显式启用。
/// </summary>
/// <remarks>
/// <para>
/// <b>默认形态零变化</b>：本 codec 不被任何默认路径使用 —— 未显式启用时（如
/// <c>RedisTokenStoreOptions.UseVersionedEnvelope</c> 为 <c>false</c>），写入形态与
/// <see cref="DefaultTokenStoreCodec"/> 逐字节一致（默认形态不得漂移）。
/// </para>
/// <para>
/// <b>三态编码矩阵</b>：
/// <list type="bullet">
/// <item><description>有效：<c>v2:{expiresInSeconds}|{token}</c>（相对秒 &gt; 0）。</description></item>
/// <item><description>无 TTL 信息：<c>v2:{expiresInSeconds}|{token}</c>（相对秒 ≤ 0，与默认形态同义）。</description></item>
/// <item><description>显式已过期：<see cref="ExpiredValue"/>（<c>v2:expired|</c>）；解码投影为
/// <c>(null, 0)</c> —— 即「无令牌」，与墓碑/缺失语义一致。</description></item>
/// </list>
/// </para>
/// <para>
/// <b>两阶段切换纪律（混部署风险）</b>：<b>先升级全部读取方</b>（含 v2 前缀识别的库版本），再开启写入开关。
/// 原因是旧版本 reader 读到 v2 值必然解码失败 —— 前缀段 <c>v2:3600</c> / <c>v2:expired</c> 非数值，
/// 旧实现的 <c>long.TryParse</c> 失败后返回 <c>null</c>（miss）。该方向<b>安全</b>（走刷新路径，不会把过期值误判为有效令牌），
/// 但会让旧实例持续 miss 并重复刷新，故禁止「写入方先升级」的逆序切换。
/// </para>
/// <para>
/// <b>读取兼容</b>：<see cref="Decode"/> 对非 v2 形态（默认 v1 形态、迁移期旧格式）<b>委托默认 codec 直读</b>，
/// 故读取方换成本 codec 后旧数据不丢；同时 <see cref="DefaultTokenStoreCodec"/> 已具备 v2 前缀识别
/// （读取方先升级即可读 v2 值，无需改配置）。
/// </para>
/// <para>
/// <b>写入面</b>：<see cref="Encode"/> 恒写 v2 形态（本 codec 的存在意义即写入形态切换）；
/// 需要「写默认形态 + 读 v2」的过渡期组合时，直接用 <see cref="DefaultTokenStoreCodec"/>（已支持 v2 读取）。
/// </para>
/// <para>
/// <b>与迁移期格式组合</b>：存量数据为旧「绝对毫秒戳」（<c>{expireMs}|{token}</c>）的下游请用
/// <c>new MigrationTokenStoreCodec(newFormatCodec: VersionedTokenStoreCodec.Instance)</c> ——
/// 写 v2 形态、读侧仍是「旧绝对戳 / v2 信封 / 相对秒」三路兼容（v2 自描述值不参与量级判别）。
/// </para>
/// </remarks>
public sealed class VersionedTokenStoreCodec : ITokenStoreCodec
{
    /// <summary>版本前缀（默认 v1 形态无前缀；旧 reader 读到该前缀的值必然解码失败 = miss，安全方向）。</summary>
    public const string VersionPrefix = "v2:";

    /// <summary>显式「已过期」标记（占据相对秒段）。</summary>
    public const string ExpiredMarker = "expired";

    /// <summary>
    /// 显式「已过期」存储值（<c>v2:expired|</c>，尾段为可空占位）；写侧语义 = 删除该键（不写僵尸 TTL）。
    /// </summary>
    public const string ExpiredValue = VersionPrefix + ExpiredMarker + "|";

    /// <summary>共享默认实例（无状态、线程安全）。</summary>
    public static VersionedTokenStoreCodec Instance { get; } = new();

    /// <summary>
    /// 存储值是否为 v2 信封形态（<b>仅前缀识别</b>，不校验内部结构）。
    /// </summary>
    /// <param name="encoded">存储字符串。</param>
    /// <returns>以 <see cref="VersionPrefix"/> 起始时为 true。</returns>
    public static bool IsVersionedEnvelope(string? encoded)
        => encoded != null && encoded.StartsWith(VersionPrefix, StringComparison.Ordinal);

    /// <summary>
    /// 解析 v2 信封为<b>三态</b>结果（唯一能观察「显式已过期」态的公开面 ——
    /// <see cref="ITokenStoreCodec.Decode"/> 的二元组契约无法承载第三态）。
    /// </summary>
    /// <param name="encoded">存储字符串。</param>
    /// <param name="state">解析出的语义态；解析失败时无意义。</param>
    /// <param name="token">解析出的令牌明文；已过期态恒为 <c>null</c>，解析失败时无意义。</param>
    /// <param name="expiresInSeconds">解析出的相对剩余秒数；已过期态恒为 0，解析失败时无意义。</param>
    /// <returns>v2 形态且结构合法返回 <c>true</c>；非 v2 形态 / 结构非法（无分隔符、分隔符在首位、前导段既非数值也非
    /// <see cref="ExpiredMarker"/>）返回 <c>false</c>（miss 契约，不抛异常）。</returns>
    /// <remarks>
    /// 已过期态的尾段按<b>占位忽略</b>（不投影为令牌）—— 该态语义即「已过期」，不携带可消费令牌。
    /// </remarks>
    public static bool TryDecodeEnvelope(string? encoded, out TokenStoreEnvelopeState state, out string? token, out long expiresInSeconds)
    {
        state = TokenStoreEnvelopeState.NoTtlInformation;
        token = null;
        expiresInSeconds = 0;

        if (!IsVersionedEnvelope(encoded))
            return false;

        var body = encoded!.Substring(VersionPrefix.Length);
        var index = body.IndexOf(DefaultTokenStoreCodec.Separator);
        if (index <= 0)
            return false;   // 无分隔符 / 分隔符在首位 ⇒ 非法结构（不抛异常）

        var head = body.Substring(0, index);
        var tail = body.Substring(index + 1);

        if (string.Equals(head, ExpiredMarker, StringComparison.Ordinal))
        {
            state = TokenStoreEnvelopeState.Expired;
            return true;
        }

        if (!long.TryParse(head, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out expiresInSeconds))
        {
            expiresInSeconds = 0;
            return false;
        }

        token = tail.Length == 0 ? null : tail;
        state = expiresInSeconds > 0
            ? TokenStoreEnvelopeState.Active
            : TokenStoreEnvelopeState.NoTtlInformation;
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 写 v2 形态 <c>v2:{expiresInSeconds}|{token}</c>；空令牌返回 <c>null</c>（墓碑语义，与默认 codec 一致）。
    /// 显式过期态不经本方法写入（二叉组契约无法表达），用 <see cref="ExpiredValue"/> 写入。
    /// </remarks>
    public string? Encode(string? token, long expiresInSeconds)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        return string.Concat(
            VersionPrefix,
            expiresInSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DefaultTokenStoreCodec.Separator.ToString(),
            token);
    }

    /// <inheritdoc />
    /// <remarks>
    /// v2 形态走 <see cref="TryDecodeEnvelope"/> 投影（显式已过期态 ⇒ <c>(null, 0)</c>，即 miss 语义）；
    /// 非 v2 形态委托 <see cref="DefaultTokenStoreCodec"/> 直读（读取兼容，旧数据不丢）。
    /// 需要区分「已过期」与「无 TTL 信息」的调用方请改用 <see cref="TryDecodeEnvelope"/>。
    /// </remarks>
    public (string? Token, long ExpiresInSeconds)? Decode(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;

        if (IsVersionedEnvelope(encoded))
            return DecodeEnvelopeToTuple(encoded);

        return DefaultTokenStoreCodec.Instance.Decode(encoded);
    }

    /// <summary>
    /// v2 信封 → 二元组投影（显式已过期态投影为 <c>(null, 0)</c>）。
    /// 供 <see cref="DefaultTokenStoreCodec.Decode"/> 的前缀识别分支复用，避免双份解析逻辑漂移。
    /// </summary>
    /// <param name="encoded">存储字符串（<c>null</c>/非 v2 形态/结构非法均为 <c>null</c>）。</param>
    /// <returns>解析成功为（令牌, 剩余秒）；结构非法为 <c>null</c>。</returns>
    internal static (string? Token, long ExpiresInSeconds)? DecodeEnvelopeToTuple(string? encoded)
    {
        if (!TryDecodeEnvelope(encoded, out _, out var token, out var expiresInSeconds))
            return null;

        return (token, expiresInSeconds);
    }
}
