// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Xml;
using System.Xml.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// Xml序列化工具类，提供对象与XML字符串之间的转换功能
/// </summary>
public sealed class XmlSerialize
{
    // <summary>
    /// 将对象序列化为XML字符串
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="obj">要序列化的对象</param>
    /// <returns>XML字符串</returns>
    public static string Serialize<T>(T obj)
    {
        return Serialize(obj, Encoding.UTF8);
    }

    /// <summary>
    /// 将对象序列化为XML字符串（指定编码）
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="obj">要序列化的对象</param>
    /// <param name="encoding">编码方式</param>
    /// <returns>XML字符串</returns>
    public static string Serialize<T>(T obj, Encoding encoding)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        if (encoding == null)
            throw new ArgumentNullException(nameof(encoding));

        try
        {
            var serializer = new XmlSerializer(typeof(T));

            var settings = new XmlWriterSettings
            {
                Encoding = encoding,
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = false
            };

            using var stream = new MemoryStream();
            using var writer = XmlWriter.Create(stream, settings);
            // 添加命名空间（可选）
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add("", ""); // 移除默认命名空间

            serializer.Serialize(writer, obj, namespaces);
            return encoding.GetString(stream.ToArray());
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"XML序列化失败: 类型 {typeof(T).Name} 可能没有无参构造函数或属性设置器", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"XML序列化失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从XML字符串反序列化为对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="xml">XML字符串</param>
    /// <returns>反序列化后的对象</returns>
    public static T Deserialize<T>(string xml)
    {
        return Deserialize<T>(xml, Encoding.UTF8);
    }

    /// <summary>
    /// 从XML字符串反序列化为对象（指定编码）
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="xml">XML字符串</param>
    /// <param name="encoding">编码方式</param>
    /// <returns>反序列化后的对象</returns>
    public static T Deserialize<T>(string xml, Encoding encoding)
    {
        if (string.IsNullOrEmpty(xml))
            throw new ArgumentNullException(nameof(xml));

        if (encoding == null)
            throw new ArgumentNullException(nameof(encoding));

        try
        {
            var serializer = new XmlSerializer(typeof(T));

            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true
            };

            using var stream = new MemoryStream(encoding.GetBytes(xml));
            using var reader = XmlReader.Create(stream, settings);
            return (T)serializer.Deserialize(reader);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"XML反序列化失败: XML格式可能不正确或与类型 {typeof(T).Name} 不匹配", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"XML反序列化失败: {ex.Message}", ex);
        }
    }
}
