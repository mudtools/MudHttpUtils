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

    /// <summary>
    /// 获取或设置 AES 加密密钥。
    /// </summary>
    /// <remarks>
    /// 密钥长度必须为 16、24 或 32 字节（对应 AES-128、AES-192 或 AES-256）。
    /// </remarks>
    public byte[] Key { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 获取或设置 AES 加密的初始化向量（IV）。
    /// </summary>
    /// <remarks>
    /// 初始化向量长度必须为 16 字节。
    /// </remarks>
    public byte[] IV { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 验证 AES 加密选项的有效性。
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 当密钥长度不是 16、24 或 32 字节，或初始化向量长度不是 16 字节时抛出。
    /// </exception>
    public void Validate()
    {
        if (Key == null || (Key.Length != 16 && Key.Length != 24 && Key.Length != 32))
            throw new InvalidOperationException(
                $"AES Key 长度必须为 16、24 或 32 字节，当前为 {Key?.Length ?? 0} 字节。");

        if (IV == null || IV.Length != 16)
            throw new InvalidOperationException(
                $"AES IV 长度必须为 16 字节，当前为 {IV?.Length ?? 0} 字节。");
    }
}
