// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Redis;

/// <summary>
/// <see cref="RedisTokenStoreOptions"/> 的一致性校验（注册期与构造期共用单一事实源）。
/// </summary>
/// <remarks>
/// 本库无法探测「是否还有其他实例运行旧版本库」，故把混部署风险处置为 <b>强制确认</b>：
/// 开启 v2 写入开关却不确认读取方已升级 = 明确的误配置，直接 fail-fast（不静默降级）。
/// </remarks>
internal static class RedisTokenStoreOptionsValidation
{
    /// <summary>
    /// 校验 v2 信封两阶段切换的确认项（<see cref="RedisTokenStoreOptions.UseVersionedEnvelope"/>
    /// 与 <see cref="RedisTokenStoreOptions.AcknowledgeVersionedEnvelopeReadersUpgraded"/> 的一致性）。
    /// </summary>
    /// <param name="options">待校验的配置。</param>
    /// <exception cref="System.InvalidOperationException">开启 v2 写入开关但未确认读取方已升级。</exception>
    internal static void EnsureVersionedEnvelopeConfirmed(RedisTokenStoreOptions options)
    {
        if (!options.UseVersionedEnvelope || options.AcknowledgeVersionedEnvelopeReadersUpgraded)
            return;

        throw new InvalidOperationException(
            "UseVersionedEnvelope = true 需先完成两阶段切换：① 升级并发布全部读取实例（已含 v2 前缀识别的库版本）；" +
            "② 确认全部实例已升级后，再置 AcknowledgeVersionedEnvelopeReadersUpgraded = true。" +
            "旧版本 reader 读到 v2 值必然解码失败（miss，方向安全但会持续重复刷新），因此禁止「写入方先升级」的逆序切换。");
    }
}
