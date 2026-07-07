// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 描述方法的响应处理策略，由源生成器在编译期填充。
/// </summary>
public sealed class ResponseDescriptor
{
    /// <summary>
    /// 是否允许任意状态码（不抛出异常）。
    /// 对应 [AllowAnyStatusCode] 特性。
    /// </summary>
    public bool AllowAnyStatusCode { get; set; }

    /// <summary>
    /// 返回类型是否为 Response{T}。
    /// 为 true 时，执行器构造 Response{T} 包装返回值。
    /// </summary>
    public bool IsResponseType { get; set; }

    /// <summary>
    /// 响应内容类型（"application/json"、"application/xml" 等）。
    /// 为 null 时使用默认 JSON。
    /// </summary>
    public string? ResponseContentType { get; set; }

    /// <summary>
    /// 是否启用响应解密。
    /// 对应 [Get(ResponseEnableDecrypt = true)] 特性。
    /// </summary>
    public bool EnableDecrypt { get; set; }

    /// <summary>
    /// 是否为 void 返回类型（Task 或 Task&lt;void&gt;）。
    /// 为 true 时执行器不执行反序列化。
    /// </summary>
    public bool IsVoidReturn { get; set; }

    /// <summary>
    /// XML 序列化器引用（仅 XML 响应时使用）。
    /// 由生成器预创建的静态 XmlSerializer 字段。
    /// </summary>
    public object? XmlSerializer { get; set; }
}
