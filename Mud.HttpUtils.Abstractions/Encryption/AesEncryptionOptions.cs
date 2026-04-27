// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Mud.HttpUtils.Encryption;

namespace Mud.HttpUtils;

/// <summary>
/// AES 加密选项类，用于配置 AES 加密算法的密钥。
/// </summary>
/// <remarks>
/// 从 v1.8.0 起，IV 不再需要配置，加密时会自动随机生成 IV 并附加到密文前。
/// 保留 IV 属性仅为向后兼容，新代码无需设置 IV。
/// </remarks>
public class AesEncryptionOptions
{
    /// <summary>
    /// 配置节的名称，用于从配置文件中读取 AES 加密配置。
    /// </summary>
    public const string SectionName = "MudHttpAesEncryption";

    private byte[]? _key;
    private byte[]? _iv;

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
        set => _key = value;
    }

    /// <summary>
    /// 获取或设置 AES 加密的初始化向量（IV）。
    /// </summary>
    /// <remarks>
    /// 从 v1.8.0 起，IV 不再需要配置，加密时会自动随机生成。
    /// 保留此属性仅为向后兼容。Getter 返回 IV 的副本。
    /// </remarks>
    [Obsolete("从 v1.8.0 起，IV 在每次加密时自动随机生成，无需手动设置。此属性将在未来版本中移除。")]
    public byte[] IV
    {
        get => _iv != null ? (byte[])_iv.Clone() : Array.Empty<byte>();
        set => _iv = value;
    }

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

#pragma warning disable CS0618
        if (_iv != null && _iv.Length != 0 && _iv.Length != 16)
            throw new InvalidOperationException(
                $"AES IV 长度必须为 16 字节，当前为 {_iv.Length} 字节。");
#pragma warning restore CS0618
    }

    /// <summary>
    /// 安全清除密钥和初始化向量，防止敏感数据残留在内存中。
    /// 注意：此方法会清零 Key 和 IV 数组，调用后此实例将不可用。
    /// 通常不需要手动调用，因为 DefaultAesEncryptionProvider 会在构造时克隆密钥。
    /// </summary>
    public void ClearSensitiveData()
    {
        SecurityHelper.ClearBytes(_key);
        SecurityHelper.ClearBytes(_iv);
        _key = null;
        _iv = null;
    }
}
