// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 标记参数作为 HTTP 请求体（Body）内容。
/// </summary>
/// <remarks>
/// <para>
/// 应用于方法参数，指示该参数应作为 HTTP 请求体发送。支持 JSON 序列化、加密、原始字符串等多种模式。
/// </para>
/// <para>
/// 默认情况下，参数会被 JSON 序列化后发送。可以通过设置 <see cref="ContentType"/> 自定义内容类型，
/// 或设置 <see cref="RawString"/> 为 true 直接发送字符串而不进行序列化。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // JSON 序列化（默认）
/// [Post("/api/users")]
/// Task&lt;User&gt; CreateUserAsync([Body] User user);
/// 
/// // 自定义内容类型
/// [Post("/api/data")]
/// Task SendDataAsync([Body("application/xml")] string xmlData);
/// 
/// // 原始字符串（不序列化）
/// [Post("/api/text")]
/// Task SendTextAsync([Body(RawString = true)] string plainText);
/// 
/// // 加密请求体
/// [Post("/api/secure")]
/// Task SendSecureDataAsync([Body(EnableEncrypt = true)] SecureData data);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class BodyAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="BodyAttribute"/> 类的新实例。
    /// </summary>
    public BodyAttribute()
    {
    }

    /// <summary>
    /// 初始化 <see cref="BodyAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="contentType">请求体的内容类型（如 "application/json"、"application/xml" 等）。</param>
    public BodyAttribute(string contentType) => ContentType = contentType;

    /// <summary>
    /// 获取或设置请求体的内容类型（Content-Type）。
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示是否将参数作为字符串内容发送。
    /// </summary>
    public bool UseStringContent { get; set; }

    /// <summary>
    /// 是否将参数作为原始字符串发送（不进行 JSON 序列化，也不调用 ToString()）。
    /// 适用于直接发送纯文本或预格式化字符串的场景。
    /// </summary>
    public bool RawString { get; set; }

    /// <summary>
    /// 获取或设置一个值，该值指示是否对请求体进行加密。
    /// </summary>
    /// <value>默认为 false。</value>
    /// <remarks>
    /// 启用后，请求体会在发送前进行加密处理。需要配合加密提供程序使用。
    /// </remarks>
    public bool EnableEncrypt { get; set; } = false;

    /// <summary>
    /// 获取或设置加密时使用的序列化类型。
    /// </summary>
    /// <value>默认为 <see cref="SerializeType.Json"/>。</value>
    public SerializeType EncryptSerializeType { get; set; } = SerializeType.Json;

    /// <summary>
    /// 获取或设置加密属性的名称。如果设置，只有指定属性会被加密。
    /// </summary>
    public string? EncryptPropertyName { get; set; }
}
