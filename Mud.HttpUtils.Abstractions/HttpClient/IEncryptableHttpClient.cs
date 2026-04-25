namespace Mud.HttpUtils;

/// <summary>
/// 可加密 HTTP 客户端接口，提供内容加密和解密功能。
/// </summary>
public interface IEncryptableHttpClient
{
    /// <summary>
    /// 加密内容对象。
    /// </summary>
    /// <param name="content">要加密的内容对象。</param>
    /// <param name="propertyName">加密数据所在的属性名称，默认为 "data"。</param>
    /// <param name="serializeType">序列化类型，指定使用 JSON 或 XML 序列化。</param>
    /// <returns>加密后的字符串内容。</returns>
    string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json);

    /// <summary>
    /// 解密内容字符串。
    /// </summary>
    /// <param name="encryptedContent">要解密的字符串内容。</param>
    /// <returns>解密后的字符串内容。</returns>
    string DecryptContent(string encryptedContent);

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
