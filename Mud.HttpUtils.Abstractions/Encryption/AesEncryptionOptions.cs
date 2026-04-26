using Mud.HttpUtils.Encryption;

namespace Mud.HttpUtils;

/// <summary>
/// AES 加密选项类，用于配置 AES 加密算法的密钥和初始化向量。
/// </summary>
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
    /// </remarks>
    public byte[] Key
    {
        get => _key ?? Array.Empty<byte>();
        set => _key = value;
    }

    /// <summary>
    /// 获取或设置 AES 加密的初始化向量（IV）。
    /// </summary>
    /// <remarks>
    /// 初始化向量长度必须为 16 字节。
    /// </remarks>
    public byte[] IV
    {
        get => _iv ?? Array.Empty<byte>();
        set => _iv = value;
    }

    /// <summary>
    /// 验证 AES 加密选项的有效性。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 当密钥长度不是 16、24 或 32 字节，或初始化向量长度不是 16 字节时抛出。
    /// </exception>
    public void Validate()
    {
        if (_key == null || (_key.Length != 16 && _key.Length != 24 && _key.Length != 32))
            throw new InvalidOperationException(
                $"AES Key 长度必须为 16、24 或 32 字节，当前为 {_key?.Length ?? 0} 字节。");

        if (_iv == null || _iv.Length != 16)
            throw new InvalidOperationException(
                $"AES IV 长度必须为 16 字节，当前为 {_iv?.Length ?? 0} 字节。");
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
