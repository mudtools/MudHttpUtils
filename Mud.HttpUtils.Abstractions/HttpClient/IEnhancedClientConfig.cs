// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 增强型 HTTP 客户端的共享配置契约。
/// </summary>
/// <remarks>
/// <para>
/// 此接口定义 <see cref="EnhancedHttpClientOptions"/>（编程式 DI 路径）与
/// <see cref="GeneratedClientOptions"/>（无 DI 工厂路径）之间共享的配置属性。
/// </para>
/// <para>
/// 当新增或修改这些共享属性时，两个实现类必须同步更新，此接口作为编译期契约确保一致性。
/// </para>
/// </remarks>
public interface IEnhancedClientConfig
{
    /// <summary>
    /// 获取或设置异常擦除器（在异常传播前清除敏感数据）。
    /// </summary>
    /// <value>默认为 <c>null</c>（不执行擦除）。</value>
    IExceptionRedactor? ExceptionRedactor { get; set; }

    /// <summary>
    /// 获取或设置错误响应体最大读取字符数（防止恶意/超大错误响应导致 OOM）。
    /// </summary>
    /// <value>默认为 <c>null</c>（无限制）。设置后错误响应体截断到指定字符数。</value>
    int? MaxExceptionContentLength { get; set; }

    /// <summary>
    /// 获取或设置是否在发送前捕获请求体字符串（用于异常调试）。
    /// </summary>
    /// <value>默认为 <c>false</c>（不捕获）。</value>
    bool CaptureRequestContent { get; set; }

    /// <summary>
    /// 获取或设置写入 <see cref="System.Net.Http.HttpRequestMessage"/> 的键值对预设。
    /// </summary>
    /// <value>默认为 <c>null</c>（不预设）。</value>
    Dictionary<string, object?>? HttpRequestMessageOptions { get; set; }

#if NET6_0_OR_GREATER
    /// <summary>
    /// 获取或设置 HTTP 版本。
    /// </summary>
    Version? HttpVersion { get; set; }

    /// <summary>
    /// 获取或设置 HTTP 版本策略。
    /// </summary>
    System.Net.Http.HttpVersionPolicy? HttpVersionPolicy { get; set; }
#endif

#if NET8_0_OR_GREATER
    /// <summary>
    /// 获取或设置 Native AOT 下用于 JSON 源生成的类型解析器（<see cref="System.Text.Json.Serialization.JsonSerializerContext"/>）。
    /// </summary>
    System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? JsonTypeInfoResolver { get; set; }
#endif
}
