// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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

    /// <summary>
    /// 加密二进制数据。
    /// </summary>
    /// <param name="data">要加密的二进制数据。</param>
    /// <returns>加密后的二进制数据。</returns>
    byte[] EncryptBytes(byte[] data);

    /// <summary>
    /// 解密二进制数据。
    /// </summary>
    /// <param name="encryptedData">要解密的二进制数据。</param>
    /// <returns>解密后的二进制数据。</returns>
    byte[] DecryptBytes(byte[] encryptedData);
}
