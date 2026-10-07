// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯用户合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 敏感 URL 键登记门面（R-P1-05① 的下游可达形态）：把词表外的自定义凭据参数名
/// 登记进进程级强制掩码集合（<c>SensitiveUrlRedactor.ExtraSensitiveKeys</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 典型场景：Query 凭据参数名既非通用 <c>token</c>/<c>secret</c> 变体、也不含 <c>_token</c> 后缀
/// （如企业微信群机器人的 <c>key</c>），静态词表刻意不收全局通用名 ⇒ 由下游 SDK 在模块注册期显式登记。
/// </para>
/// <para>
/// 登记后的键<b>无论 <see cref="MudHttpObservabilityOptions.RedactUrlInTelemetry"/> 开关为何都强制掩码</b>
/// —— 该开关只影响"词表外的未知参数"，不应成为已确认为凭据的键的逃生门（与 R-P1-05① 语义一致）。
/// 仅作用于 URL query 脱敏；JSON 消息体脱敏（<c>MessageSanitizer</c>）仍以静态词表为准。
/// </para>
/// <para>
/// <b>登记范围提示</b>：登记为进程级生效，仅登记确认为凭据的参数名；过于宽泛的键名
/// （如 <c>id</c>、<c>name</c>）会造成大面积脱敏影响排障，组件侧不做语义校验（保持机制中立）。
/// </para>
/// </remarks>
public static class SensitiveUrlKeys
{
    /// <summary>登记一个进程级敏感键（幂等、线程安全、空值与空白项忽略）。</summary>
    /// <param name="name">参数名（大小写不敏感，精确匹配）。</param>
    public static void Register(string? name)
    {
        // 空白键名永不匹配真实参数，登记进进程级集合无意义 ⇒ 与批量重载共用同一过滤语义。
        if (string.IsNullOrWhiteSpace(name))
            return;

        Helpers.SensitiveUrlRedactor.RegisterExtraSensitiveKey(name);
    }

    /// <summary>批量登记进程级敏感键（逐项幂等；空值与空白项忽略）。</summary>
    /// <param name="names">参数名集合（可 null；逐项大小写不敏感，精确匹配）。</param>
    /// <remarks>
    /// 命名为 <c>RegisterAll</c> 而非 <c>Register</c> 重载：评审实现期发现，与 <c>Register(string?)</c>
    /// 构成重载时，<c>Register(null)</c> 字面量调用在两个引用类型参数间产生 CS0121 二义性，
    /// 是下游会实际踩中的可用性陷阱。
    /// </remarks>
    public static void RegisterAll(IEnumerable<string?> names)
    {
        if (names is null)
            return;

        // 逐项转发到单参重载，复用既有锁与幂等语义（两重载过滤规则一致）。
        foreach (var name in names)
        {
            Register(name);
        }
    }
}
