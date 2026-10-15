// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Redis;

/// <summary>
/// F-3：官方分布式令牌存储（<see cref="RedisTokenStore"/>）的配置。
/// </summary>
/// <remarks>
/// 键布局见 <see cref="RedisTokenStoreKeyBuilder"/>：<c>{KeyPrefix}:{tokenType}:{appKey}:access|refresh</c>
/// （appKey 段为空时取 <see cref="SharedAppKeySegment"/>，与方案的
/// <c>mud:httputils:{tokenType}:{appKey}</c> 基形一致，<c>:access</c>/<c>:refresh</c> 为字段判别后缀）。
/// </remarks>
public sealed class RedisTokenStoreOptions
{
    /// <summary>配置节名（宿主 JSON 绑定用）。</summary>
    public const string SectionName = "MudHttpRedisTokenStore";

    /// <summary>
    /// <see cref="AppKey"/> 为空时的占位段（共享命名空间，单应用场景）。
    /// 告警：真实应用的 appKey 不应取值 <c>"shared"</c>，否则与共享命名空间碰撞。
    /// </summary>
    public const string SharedAppKeySegment = "shared";

    /// <summary>
    /// 键前缀。默认 <c>mud:httputils</c>（与方案 F-3 钉死的键前缀一致）。
    /// </summary>
    public string KeyPrefix { get; set; } = "mud:httputils";

    /// <summary>
    /// 应用维度段（多应用隔离）。为空时取 <see cref="SharedAppKeySegment"/>。
    /// </summary>
    public string? AppKey { get; set; }

    /// <summary>
    /// 兜底 TTL：写入侧无过期信息（<c>expiresInSeconds &lt;= 0</c>）时使用。
    /// 为 <c>null</c> 时对应键不设 TTL（Redis 侧永久保留，直至被移除/覆盖）。
    /// <para>
    /// 安全默认（C1，宁缺勿滥）：<b>不设置</b>兜底 TTL 时无界滞留的风险由宿主承担，
    /// 建议生产环境显式配置（如 30 天）。
    /// </para>
    /// </summary>
    public TimeSpan? DefaultTtl { get; set; }

    /// <summary>
    /// Redis 数据库编号；<c>-1</c> 表示连接默认库（<see cref="StackExchange.Redis.IDatabase"/> 的约定值）。
    /// </summary>
    public int Database { get; set; } = -1;

    /// <summary>
    /// U-2：写入侧启用 <b>v2 版本化信封</b>（<c>v2:{expiresInSeconds}|{token}</c> + 显式过期态
    /// <c>v2:expired|</c>）。默认 <c>false</c> = 逐位沿用默认形态 <c>{expiresInSeconds}|{token}</c>（零行为变化）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>两阶段切换纪律</b>：<b>先升级全部读取实例</b>（已含 v2 前缀识别的库版本），再开启本开关。
    /// 旧版本 reader 读到 v2 值必然解码失败（前导段 <c>v2:3600</c> / <c>v2:expired</c> 非数值 ⇒
    /// <c>null</c> = miss），方向安全（不会把过期值误判为有效令牌，走刷新路径），
    /// 但会造成旧实例持续 miss 与重复刷新风暴；逆序切换（写入方先升级）被明令禁止。
    /// </para>
    /// <para>
    /// 开启时必须同时置 <see cref="AcknowledgeVersionedEnvelopeReadersUpgraded"/> ——
    /// 未确认时构造 <see cref="RedisTokenStore"/> 直接 fail-fast（见该属性说明）。
    /// </para>
    /// </remarks>
    public bool UseVersionedEnvelope { get; set; }

    /// <summary>
    /// U-2：两阶段切换的<b>显式确认</b> —— 声明「全部读取实例已升级至含 v2 解码的库版本」。
    /// </summary>
    /// <remarks>
    /// 本库无法探测其他实例的库版本，故把 U-2 的混部署风险处置为<b>强制确认</b>：
    /// <see cref="UseVersionedEnvelope"/> 为 <c>true</c> 而本项为 <c>false</c> 时，
    /// 注册（<c>AddMudHttpRedisTokenStore</c>）与构造 <see cref="RedisTokenStore"/> 均立即抛出
    /// <see cref="System.InvalidOperationException"/>，并在消息中给出两阶段切换步骤 ——
    /// 杜绝「写入方先升级」的静默误配置。
    /// </remarks>
    public bool AcknowledgeVersionedEnvelopeReadersUpgraded { get; set; }
}
