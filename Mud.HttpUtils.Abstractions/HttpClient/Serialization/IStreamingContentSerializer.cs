// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.IO;

namespace Mud.HttpUtils;

/// <summary>
/// 可选能力接口：流式增量反序列化响应体为 <see cref="IAsyncEnumerable{T}"/>。
/// </summary>
/// <remarks>
/// <para>
/// 用于 NDJSON / JSON 数组流式读取，不缓冲整个响应。
/// 注意：本接口职责为<strong>响应反序列化</strong>（非请求序列化）。
/// </para>
/// </remarks>
public interface IStreamingContentSerializer
{
    /// <summary>
    /// 增量反序列化流为异步序列。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="stream">响应体流。</param>
    /// <param name="format">流式内容帧格式（单 JSON 数组或换行分隔 NDJSON）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步序列，逐项产生反序列化结果。</returns>
    IAsyncEnumerable<T?> DeserializeStreamAsync<T>(
        Stream stream,
        StreamingContentFormat format,
        CancellationToken cancellationToken = default);
}
