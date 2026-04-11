// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Models.Analysis;

/// <summary>
/// 方法分析结果
/// </summary>
/// <remarks>
/// 用于存储接口方法的分析信息，包括 HTTP 方法、URL 模板、参数等。
/// </remarks>
internal class MethodAnalysisResult
{
    /// <summary>
    /// 方法实际所属的接口名称（用于引用注释）
    /// </summary>
    public string MethodOwnerInterfaceName { get; set; } = string.Empty;

    /// <summary>
    /// 当前正在处理的接口名称（用于接口级别的partial方法）
    /// </summary>
    public string CurrentInterfaceName { get; set; } = string.Empty;

    /// <summary>
    /// 方法是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 方法名称
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// HTTP 方法（GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS）
    /// </summary>
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>
    /// URL 模板，支持参数占位符
    /// </summary>
    public string UrlTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 返回类型显示字符串
    /// </summary>
    public string ReturnType { get; set; } = string.Empty;

    /// <summary>
    /// 是否是异步方法（返回类型为 Task 或 Task<T>）
    /// </summary>
    public bool IsAsyncMethod { get; set; }

    /// <summary>
    /// 异步方法的内部返回类型（如果是 Task<T>，这里是 T；如果是 Task，这里是 void）
    /// </summary>
    public string AsyncInnerReturnType { get; set; } = string.Empty;

    /// <summary>
    /// 方法参数列表
    /// </summary>
    public IReadOnlyList<ParameterInfo> Parameters { get; set; } = [];

    /// <summary>
    /// 是否忽略生成实现 [IgnoreImplement]
    /// </summary>
    public bool IgnoreImplement { get; set; }

    /// <summary>
    /// 是否忽略生成包装接口 [IgnoreWrapInterface]
    /// </summary>
    public bool IgnoreWrapInterface { get; set; }

    /// <summary>
    /// 接口特性列表（用于存储Header:Authorization、Query:Authorization等）
    /// </summary>
    public HashSet<string> InterfaceAttributes { get; set; } = [];

    /// <summary>
    /// 接口Header特性列表（用于存储所有Header特性的名称和值）
    /// </summary>
    public List<InterfaceHeaderAttributeInfo> InterfaceHeaderAttributes { get; set; } = [];

    /// <summary>
    /// 方法级别的内容类型（从HTTP方法特性的ContentType属性获取，如 [Post(ContentType = "application/xml")]）
    /// </summary>
    public string? MethodContentType { get; set; }

    /// <summary>
    /// Body参数级别的内容类型（从[Body]特性的ContentType参数获取）
    /// </summary>
    public string? BodyContentType { get; set; }

    /// <summary>
    /// 响应内容类型（由HTTP方法特性的ResponseContentType指定）
    /// </summary>
    public string? ResponseContentType { get; set; }

    /// <summary>
    /// 响应是否启用解密
    /// </summary>
    public bool ResponseEnableDecrypt { get; set; }

    /// <summary>
    /// Body参数是否启用加密
    /// </summary>
    public bool BodyEnableEncrypt { get; set; }

    /// <summary>
    /// Body加密序列化类型（Json/Xml）
    /// </summary>
    public string? BodyEncryptSerializeType { get; set; }

    /// <summary>
    /// Body加密后数据包装属性名
    /// </summary>
    public string? BodyEncryptPropertyName { get; set; }

    /// <summary>
    /// 接口级Token注入模式（Header/Query/Path）
    /// </summary>
    public string? InterfaceTokenInjectionMode { get; set; }

    /// <summary>
    /// 接口级Token名称
    /// </summary>
    public string? InterfaceTokenName { get; set; }

    /// <summary>
    /// 获取最终的内容类型（Body参数级 > 方法级）
    /// <para>如果都未定义则返回null，调用方应使用接口级默认值（从HttpClientApi特性获取）</para>
    /// </summary>
    /// <returns>内容类型字符串，如果都未定义则返回null</returns>
    public string? GetEffectiveContentType()
    {
        return BodyContentType ?? MethodContentType;
    }

    /// <summary>
    /// 无效的分析结果实例
    /// </summary>
    public static MethodAnalysisResult Invalid => new() { IsValid = false };
}
