// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任。
// -----------------------------------------------------------------------

#if NET8_0_OR_GREATER
using System.Text.Json.Serialization.Metadata;

namespace Mud.HttpUtils;

/// <summary>
/// 可选能力接口：基于 <see cref="JsonTypeInfo{T}"/> 的 AOT 安全 JSON 序列化快车道。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="ISynchronousContentSerializer"/> / <see cref="IStreamingContentSerializer"/> 同属
/// "可选能力接口" 范式：实现者通常同时实现 <see cref="IHttpContentSerializer"/>，但本接口不强制继承。
/// </para>
/// <para>
/// <b>为什么需要它</b>：<see cref="IHttpContentSerializer"/> 的泛型方法依赖注入的
/// <c>JsonSerializerOptions.TypeInfoResolver</c> 在运行时解析 <c>T</c> 的元数据。
/// 当调用方持有确切的 <see cref="JsonTypeInfo{T}"/>（例如由消费方源生成
/// <c>JsonSerializerContext</c> 直接暴露）时，通过本接口可以绕过 resolver 配置，
/// 实现零反射、零动态代码的序列化，且不依赖运行时的 resolver 组合顺序。
/// </para>
/// <para>
/// 调用方应通过能力探测（<c>serializer as IAotJsonContentSerializer</c>）选择快车道；
/// 未实现时回退到 <see cref="IHttpContentSerializer"/> 的 options 路径。
/// </para>
/// </remarks>
public interface IAotJsonContentSerializer
{
    /// <summary>
    /// 使用显式 <see cref="JsonTypeInfo{T}"/> 将对象序列化为 <see cref="System.Net.Http.HttpContent"/>。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="item">要序列化的对象；为 null 时返回 null。</param>
    /// <param name="typeInfo">该类型的源生成元数据。</param>
    /// <returns>表示 JSON 内容的 <see cref="System.Net.Http.HttpContent"/>；<paramref name="item"/> 为 null 时返回 null。</returns>
    System.Net.Http.HttpContent? ToHttpContent<T>(T item, JsonTypeInfo<T> typeInfo);

    /// <summary>
    /// 使用显式 <see cref="JsonTypeInfo{T}"/> 从 <see cref="System.Net.Http.HttpContent"/> 异步反序列化。
    /// </summary>
    /// <typeparam name="T">反序列化目标类型。</typeparam>
    /// <param name="content">HTTP 内容。</param>
    /// <param name="typeInfo">该类型的源生成元数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>反序列化后的对象。</returns>
    System.Threading.Tasks.Task<T?> FromHttpContentAsync<T>(
        System.Net.Http.HttpContent content,
        JsonTypeInfo<T> typeInfo,
        System.Threading.CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用显式 <see cref="JsonTypeInfo{T}"/> 将对象序列化为 JSON 字符串。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="item">要序列化的对象。</param>
    /// <param name="typeInfo">该类型的源生成元数据。</param>
    /// <returns>JSON 字符串。</returns>
    string Serialize<T>(T item, JsonTypeInfo<T> typeInfo);

    /// <summary>
    /// 使用显式 <see cref="JsonTypeInfo{T}"/> 从 JSON 字符串反序列化。
    /// </summary>
    /// <typeparam name="T">反序列化目标类型。</typeparam>
    /// <param name="json">JSON 字符串。</param>
    /// <param name="typeInfo">该类型的源生成元数据。</param>
    /// <returns>反序列化后的对象。</returns>
    T? Deserialize<T>(string json, JsonTypeInfo<T> typeInfo);
}
#endif
