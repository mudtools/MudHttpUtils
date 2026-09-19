// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net.Http;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// 基于 <see cref="XmlSerializer"/> 的 <see cref="IHttpContentSerializer"/> 实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>Native AOT 注意</b>：<c>XmlSerializer</c> 在运行时生成动态程序集，
/// 在 Native AOT 下不支持。已标注 <c>[RequiresDynamicCode]</c> 和 <c>[RequiresUnreferencedCode]</c>。
/// AOT 场景应使用 JSON 序列化（<c>SystemTextJsonContentSerializer</c> + <c>JsonSerializerContext</c>）。
/// </para>
/// </remarks>
public class XmlContentSerializer : IHttpContentSerializer
{
    private readonly XmlContentSerializerSettings _settings;

    /// <summary>
    /// 获取此序列化器使用的设置。
    /// </summary>
    public XmlContentSerializerSettings Settings => _settings;

    /// <summary>
    /// 初始化 <see cref="XmlContentSerializer"/> 实例。
    /// </summary>
    /// <param name="settings">XML 序列化设置。为 null 时使用默认设置。</param>
    public XmlContentSerializer(XmlContentSerializerSettings? settings = null)
    {
        _settings = settings ?? new XmlContentSerializerSettings();
    }

    /// <inheritdoc/>
#if NET7_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("XmlSerializer generates dynamic assemblies. Not AOT-compatible.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#elif NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#endif
    public HttpContent? ToHttpContent<T>(T item, object? options = null)
    {
        if (item is null) return null;
        var bytes = SerializeToBytes(item, typeof(T));
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_settings.MediaType)
        {
            CharSet = _settings.WriterSettings.Encoding.WebName
        };
        return content;
    }

    /// <summary>
    /// 将对象序列化为字节数组。写入 <see cref="System.IO.MemoryStream"/> 而非 <see cref="System.IO.StringWriter"/>，
    /// 以确保 XML 声明中的 encoding 与实际字节编码一致。
    /// </summary>
    private byte[] SerializeToBytes(object? item, Type type)
    {
        var serializer = new XmlSerializer(type);
        using var ms = new System.IO.MemoryStream();
        using var xmlWriter = XmlWriter.Create(ms, _settings.WriterSettings);
        serializer.Serialize(xmlWriter, item);
        xmlWriter.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// 将对象序列化为字符串。使用声明 <see cref="System.Text.Encoding"/> 等于
    /// <see cref="XmlContentSerializerSettings.WriterSettings"/>.Encoding 的 StringWriter，
    /// 确保 XML 声明中的 encoding 与声明一致且不含 BOM 字符
    /// （默认 StringWriter.Encoding 恒为 utf-16，会导致声明与设置不符；
    /// 直接写字节流则会引入 BOM，破坏 TextReader 反序列化路径）。
    /// </summary>
    private string SerializeToString(object? item, Type type)
    {
        var serializer = new XmlSerializer(type);
        // [跨平台] XmlWriter 包装 TextWriter 时采用 TextWriter.NewLine 作为缩进换行符，
        // 而 StringWriter.NewLine 默认为 Environment.NewLine（Linux 为 "\n"、Windows 为 "\r\n"），
        // 会导致缩进输出随平台漂移。显式对齐 WriterSettings.NewLineChars（默认 "\r\n"），
        // 使字符串路径与 MemoryStream 路径（直接使用 NewLineChars）跨平台一致。
        using var sw = new EncodingAwareStringWriter(_settings.WriterSettings.Encoding)
        {
            NewLine = _settings.WriterSettings.NewLineChars
        };
        using var xmlWriter = XmlWriter.Create(sw, _settings.WriterSettings);
        serializer.Serialize(xmlWriter, item);
        xmlWriter.Flush();
        return sw.ToString();
    }

    private sealed class EncodingAwareStringWriter : System.IO.StringWriter
    {
        private readonly System.Text.Encoding _encoding;

        public EncodingAwareStringWriter(System.Text.Encoding encoding)
        {
            _encoding = encoding;
        }

        public override System.Text.Encoding Encoding => _encoding;
    }

    /// <inheritdoc/>
#if NET7_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("XmlSerializer generates dynamic assemblies. Not AOT-compatible.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#elif NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#endif
    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, object? options = null, CancellationToken cancellationToken = default)
    {
#if NET5_0_OR_GREATER
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        var serializer = new XmlSerializer(typeof(T));
        return (T?)serializer.Deserialize(stream);
    }

    /// <inheritdoc/>
#if NET7_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("XmlSerializer generates dynamic assemblies. Not AOT-compatible.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#elif NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#endif
    public string Serialize<T>(T item, object? options = null)
    {
        return SerializeToString(item, typeof(T));
    }

    /// <inheritdoc/>
#if NET7_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("XmlSerializer generates dynamic assemblies. Not AOT-compatible.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#elif NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#endif
    public string Serialize(object? item, Type type, object? options = null)
    {
        return SerializeToString(item, type);
    }

    /// <inheritdoc/>
#if NET7_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("XmlSerializer generates dynamic assemblies. Not AOT-compatible.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#elif NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("XmlSerializer uses reflection. Not AOT-compatible.")]
#endif
    public T? Deserialize<T>(string xml, object? options = null)
    {
        var serializer = new XmlSerializer(typeof(T));
        using var reader = new System.IO.StringReader(xml);
        return (T?)serializer.Deserialize(reader);
    }

    /// <inheritdoc/>
    public string? GetFieldNameForProperty(PropertyInfo propertyInfo)
    {
        var attr = propertyInfo.GetCustomAttribute<XmlElementAttribute>();
        return attr?.ElementName;
    }
}

/// <summary>
/// XML 内容序列化器设置。
/// </summary>
public class XmlContentSerializerSettings
{
    /// <summary>
    /// 获取或设置 XML 写入器设置。
    /// </summary>
    public XmlWriterSettings WriterSettings { get; set; } = new()
    {
        Encoding = System.Text.Encoding.UTF8,
        Indent = false,
        OmitXmlDeclaration = false
    };

    /// <summary>
    /// 获取或设置 Content-Type 媒体类型。
    /// </summary>
    public string MediaType { get; set; } = "application/xml";
}
