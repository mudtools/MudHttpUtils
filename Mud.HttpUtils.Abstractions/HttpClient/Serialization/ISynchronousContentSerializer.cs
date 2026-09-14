// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Net.Http;

namespace Mud.HttpUtils;

/// <summary>
/// 可选能力接口：同步序列化请求体（启用 STJ 源生成 fast-path）。
/// </summary>
/// <remarks>
/// <para>
/// 当 <see cref="RequestBodySerializationMode"/> 为 <see cref="RequestBodySerializationMode.Buffered"/> 或
/// <see cref="RequestBodySerializationMode.Streamed"/> 时使用。
/// 实现者通常同时实现 <see cref="IHttpContentSerializer"/>，但本接口不强制继承，
/// 允许仅提供同步 fast-path 能力。
/// </para>
/// </remarks>
public interface ISynchronousContentSerializer
{
    /// <summary>
    /// 同步序列化为缓冲 <see cref="HttpContent"/>（如 <see cref="ByteArrayContent"/>），
    /// 启用 STJ <c>SerializeToUtf8Bytes</c> fast-path。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="item">要序列化的对象。</param>
    /// <returns>表示序列化内容的缓冲 <see cref="HttpContent"/> 实例。</returns>
    HttpContent ToHttpContentSynchronous<T>(T item);

    /// <summary>
    /// 创建流式 <see cref="HttpContent"/>，在发送时通过同步 fast-path 写入请求流（不缓冲全部内容）。
    /// 适用于大文件上传等场景。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="item">要序列化的对象。</param>
    /// <returns>在发送时写入请求流的 <see cref="HttpContent"/> 实例。</returns>
    HttpContent ToStreamingHttpContent<T>(T item);
}
