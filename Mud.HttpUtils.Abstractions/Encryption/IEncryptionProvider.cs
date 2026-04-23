namespace Mud.HttpUtils;

/// <summary>
/// 加密提供程序接口，定义加密和解密操作的标准方法。
/// </summary>
public interface IEncryptionProvider
{
    /// <summary>
    /// 加密明文数据。
    /// </summary>
    /// <param name="plainText">要加密的明文数据。</param>
    /// <returns>加密后的密文字符串。</returns>
    string Encrypt(string plainText);

    /// <summary>
    /// 解密密文数据。
    /// </summary>
    /// <param name="cipherText">要解密的密文数据。</param>
    /// <returns>解密后的明文字符串。</returns>
    string Decrypt(string cipherText);
}
