// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils;

/// <summary>
/// 可选能力接口：同步反序列化已缓冲的响应体字符串。
/// </summary>
/// <remarks>
/// <para>
/// 用于 <see cref="ApiException"/> 的 <c>TryDeserializeContent</c> / <c>DeserializeContent</c> 等 catch 过滤器场景
/// （CLR 不允许 await）。与现有 <see cref="IHttpContentSerializer.Deserialize{T}"/> 互补：
/// 现有方法已支持同步反序列化，本接口作为<strong>能力声明</strong>供运行时检测。
/// </para>
/// </remarks>
public interface ISynchronousContentDeserializer
{
    /// <summary>
    /// 从已缓冲的字符串同步反序列化为指定类型。
    /// </summary>
    /// <typeparam name="T">反序列化目标类型。</typeparam>
    /// <param name="content">已缓冲的内容字符串。</param>
    /// <returns>反序列化后的对象。</returns>
    T? DeserializeFromString<T>(string content);
}
