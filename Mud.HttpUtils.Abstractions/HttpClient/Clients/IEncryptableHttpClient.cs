// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
    /// <remarks>
    /// <b>Native AOT 注意</b>：此重载使用运行时类型 (<c>content.GetType()</c>) 进行 JSON 序列化，
    /// 在 AOT 下可能不安全。请在 AOT 场景下改用 <see cref="EncryptContent{T}"/> 泛型重载。
    /// </remarks>
    string EncryptContent(object content, string propertyName = "data", SerializeType serializeType = SerializeType.Json);

    /// <summary>
    /// 加密内容对象（AOT 安全泛型重载）。
    /// </summary>
    /// <typeparam name="T">内容对象的类型。该类型必须在 <c>JsonSerializerContext</c> 中声明。</typeparam>
    /// <param name="content">要加密的内容对象。</param>
    /// <param name="propertyName">加密数据所在的属性名称，默认为 "data"。</param>
    /// <returns>加密后的字符串内容。</returns>
    /// <remarks>
    /// 使用编译期类型 <typeparamref name="T"/> 进行 JSON 序列化，不依赖运行时反射，
    /// 适用于 Native AOT 场景。仅支持 JSON 序列化（不支持 XML）。
    /// </remarks>
    string EncryptContent<T>(T content, string propertyName = "data");

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
