// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// AES 加密选项类，用于配置 AES 加密算法的密钥。
/// </summary>
/// <remarks>
/// 从 v1.8.0 起，IV 不再需要配置，加密时会自动随机生成 IV 并附加到密文前。
/// CFG-27：仅用于向后兼容的 <c>IV</c> 属性已移除（运行时无消费点），新代码不应再设置 IV。
/// </remarks>
public class AesEncryptionOptions
{
    /// <summary>
    /// 配置节的名称，用于从配置文件中读取 AES 加密配置。
    /// </summary>
    public const string SectionName = "MudHttpAesEncryption";

    private byte[]? _key;

    /// <summary>
    /// 获取或设置 AES 加密密钥。
    /// </summary>
    /// <remarks>
    /// 密钥长度必须为 16、24 或 32 字节（对应 AES-128、AES-192 或 AES-256）。
    /// Getter 返回密钥的副本，防止外部代码直接访问原始数据。
    /// </remarks>
    public byte[] Key
    {
        get => _key != null ? (byte[])_key.Clone() : Array.Empty<byte>();
        set => _key = value is null ? null : (byte[])value.Clone();
    }

    // CFG-27：原 IV 属性已移除 —— 从 v1.8.0 起 IV 在每次加密时自动随机生成，
    // 运行时无任何消费点（Validate 不校验、Provider 不读取），属静默失效配置。

    /// <summary>
    /// 获取或设置是否强制产出可跨运行时解密的密文格式。
    /// </summary>
    /// <value>默认为 <c>false</c>。</value>
    /// <remarks>
    /// <para>
    /// 本库加密时始终使用<b>认证加密</b>（AEAD / Encrypt-then-MAC），密文带 1 字节版本前缀：
    /// <list type="bullet">
    /// <item><c>0x02</c> AesGcm —— <c>[0x02][nonce(12)][tag(16)][密文]</c>：仅 net8.0+ 且 <c>AesGcm.IsSupported</c> 时产出，且<b>仅能</b>在 net8.0+ 解密。</item>
    /// <item><c>0x03</c> CBC + HMAC-SHA256 —— <c>[0x03][IV(16)][MAC(32)][密文]</c>（Encrypt-then-MAC）：可在全部目标框架（netstandard2.0 / net6.0 / net8.0 / net10.0）解密。</item>
    /// </list>
    /// 解密<b>仅</b>按首字节版本前缀分派，与任何配置无关。
    /// </para>
    /// <para>
    /// 设为 <c>true</c> 时，即使在 net8.0+ 上也强制产出 <c>0x03</c> 格式，
    /// 用于密文需要跨进程/跨服务传输、而对端目标框架可能低于 net8.0 的场景
    /// （例如经 <c>IEncryptableHttpClient.EncryptContent</c> 加密后由 net6.0 服务解密）。
    /// </para>
    /// </remarks>
    public bool RequireCrossRuntimePortable { get; set; }

    /// <summary>
    /// M5-HC-13：是否启用 AES/HMAC 密钥分离（HKDF 派生 enc/mac 子密钥），默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 启用后 CBC+HMAC 路径产出信封 <c>0x04</c>（<c>[0x04][IV(16)][MAC(32)][密文]</c>），
    /// AES 加密与 HMAC 使用不同子密钥（密码学"密钥分离"最佳实践）。
    /// 旧格式 <c>0x03</c> 仍可解密（向后兼容）。设为 <c>false</c> 可回退到 <c>0x03</c> 共用主密钥行为。
    /// </remarks>
    public bool EnableKeySeparation { get; set; } = true;

    /// <summary>
    /// 验证 AES 加密选项的有效性。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 当密钥长度不是 16、24 或 32 字节时抛出。
    /// </exception>
    public void Validate()
    {
        if (_key == null || (_key.Length != 16 && _key.Length != 24 && _key.Length != 32))
            throw new InvalidOperationException(
                $"AES Key 长度必须为 16、24 或 32 字节，当前为 {_key?.Length ?? 0} 字节。");

        // IV 在 v1.8.0 起自动随机生成，不再校验用户设置的 IV
    }

    /// <summary>
    /// 安全清除密钥，防止敏感数据残留在内存中。
    /// 注意：此方法会清零 Key 数组，调用后此实例将不可用。
    /// TMR-06 修订：DefaultAesEncryptionProvider 不再调用此方法（IOptions<T>.Value 视为只读）。
    /// 此方法保留供用户显式调用（如启动期一次性构造后销毁 options）。
    /// </summary>
    public void ClearSensitiveData()
    {
        SecurityHelper.ClearBytes(_key);
        _key = null;
    }
}
