namespace Mud.HttpUtils;

/// <summary>
/// 可加密 HTTP 客户端接口，提供内容加密功能。
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
}
