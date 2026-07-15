// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Mud.HttpUtils;

/// <summary>
/// HTTP 内容序列化器抽象。允许运行时切换序列化实现（System.Text.Json / Newtonsoft.Json / XML）。
/// </summary>
/// <remarks>
/// <para>
/// 此接口是 Mud.HttpUtils 序列化的统一入口。所有 JSON 序列化/反序列化操作
/// （请求体序列化、响应反序列化、NDJSON 流式解析、加密内容序列化等）
/// 都通过此抽象层进行，不再直接调用 <c>JsonSerializer</c>。
/// </para>
/// <para>
/// 默认实现 <c>SystemTextJsonContentSerializer</c> 行为等价于直接调用 <c>JsonSerializer</c>，
/// 保证向后兼容。消费方可注入自定义实现以切换序列化引擎。
/// </para>
/// <para>
/// <b>Native AOT 注意</b>：<see cref="GetFieldNameForProperty"/> 方法接收 <see cref="PropertyInfo"/>，
/// 在 AOT 下需反射读取属性特性。建议 AOT 路径中由源生成器在编译期提供字段名映射，绕过此方法。
/// </para>
/// </remarks>
public interface IHttpContentSerializer
{
    /// <summary>
    /// 将对象序列化为 <see cref="HttpContent"/>。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="item">要序列化的对象。</param>
    /// <param name="options">序列化选项（实现特定，如 <c>JsonSerializerOptions</c>）。可为 null，使用默认选项。</param>
    /// <returns>表示序列化内容的 <see cref="HttpContent"/> 实例；如果 <paramref name="item"/> 为 null 则返回 null。</returns>
    HttpContent? ToHttpContent<T>(T item, object? options = null);

    /// <summary>
    /// 从 <see cref="HttpContent"/> 异步反序列化为指定类型。
    /// </summary>
    /// <typeparam name="T">反序列化目标类型。</typeparam>
    /// <param name="content">HTTP 内容。</param>
    /// <param name="options">序列化选项（实现特定，如 <c>JsonSerializerOptions</c>）。可为 null，使用序列化器默认选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化后的对象。</returns>
    Task<T?> FromHttpContentAsync<T>(HttpContent content, object? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将对象序列化为 JSON 字符串（用于加密内容等需要字符串中间结果的场景）。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="item">要序列化的对象。</param>
    /// <param name="options">序列化选项。可为 null，使用默认选项。</param>
    /// <returns>JSON 字符串。</returns>
    string Serialize<T>(T item, object? options = null);

    /// <summary>
    /// 将对象序列化为 JSON 字符串（非泛型重载，用于运行时类型场景）。
    /// </summary>
    /// <param name="item">要序列化的对象。</param>
    /// <param name="type">对象的运行时类型。</param>
    /// <param name="options">序列化选项。可为 null，使用默认选项。</param>
    /// <returns>JSON 字符串。</returns>
    string Serialize(object? item, System.Type type, object? options = null);

    /// <summary>
    /// 从 JSON 字符串反序列化为指定类型（用于 NDJSON 逐行解析、执行器字符串反序列化等场景）。
    /// </summary>
    /// <typeparam name="T">反序列化目标类型。</typeparam>
    /// <param name="json">JSON 字符串。</param>
    /// <param name="options">序列化选项。可为 null，使用默认选项。</param>
    /// <returns>反序列化后的对象。</returns>
    T? Deserialize<T>(string json, object? options = null);

    /// <summary>
    /// 获取属性的序列化字段名（用于 form-urlencoded 和查询参数命名）。
    /// </summary>
    /// <param name="propertyInfo">属性信息。</param>
    /// <returns>序列化字段名；如果无法确定则返回 null。</returns>
    /// <remarks>
    /// <b>Native AOT 注意</b>：此方法接收 <see cref="PropertyInfo"/>，在 AOT 下需反射读取属性特性
    /// （如 <c>JsonPropertyNameAttribute</c>）。建议 AOT 路径中由源生成器在编译期提供字段名映射，绕过此方法。
    /// </remarks>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("GetFieldNameForProperty 使用反射读取属性特性，AOT 场景应由源生成器在编译期提供字段名映射。")]
#endif
    string? GetFieldNameForProperty(PropertyInfo propertyInfo);
}
