// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Attributes;

/// <summary>
/// 序列化方法
/// </summary>
public enum SerializationMethod
{
    /// <summary>
    /// 使用 JSON 序列化（默认）
    /// </summary>
    Json,

    /// <summary>
    /// 使用 XML 序列化
    /// </summary>
    Xml,

    /// <summary>
    /// 使用表单 URL 编码序列化
    /// </summary>
    FormUrlEncoded
}

/// <summary>
/// 标记接口或方法，指定请求体的默认序列化方法。
/// </summary>
/// <remarks>
/// <para>
/// 应用于接口或方法，指定 [Body] 参数的默认序列化方法。
/// 方法级特性优先于接口级特性。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [HttpClientApi]
/// [SerializationMethod(SerializationMethod.Xml)]
/// public interface IXmlApi
/// {
///     [Post("/api/data")]
///     Task SendDataAsync([Body] DataModel data);
///     // 使用 XML 序列化请求体
/// 
///     [Post("/api/json-data")]
///     [SerializationMethod(SerializationMethod.Json)]
///     Task SendJsonDataAsync([Body] DataModel data);
///     // 方法级覆盖，使用 JSON 序列化
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SerializationMethodAttribute : Attribute
{
    /// <summary>
    /// 初始化 <see cref="SerializationMethodAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="method">序列化方法。</param>
    public SerializationMethodAttribute(SerializationMethod method)
    {
        Method = method;
    }

    /// <summary>
    /// 获取序列化方法。
    /// </summary>
    public SerializationMethod Method { get; }
}
