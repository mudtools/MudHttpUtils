// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.IO;

namespace Mud.HttpUtils;

/// <summary>
/// 允许直接写入目标流的 <see cref="System.Net.Http.HttpContent"/>，避免中间缓冲。
/// </summary>
/// <remarks>
/// <para>
/// 用于大文件上传 / 流式请求体，序列化器直接写入网络流。
/// </para>
/// <para>
/// 使用方式：<c>new PushStreamContent(stream => serializer.Serialize(stream, item), "application/json")</c>
/// </para>
/// </remarks>
public sealed class PushStreamContent : System.Net.Http.HttpContent
{
    private readonly Action<Stream> _onStreamAvailable;
    private readonly string _mediaType;

    /// <summary>
    /// 初始化 <see cref="PushStreamContent"/> 实例。
    /// </summary>
    /// <param name="onStreamAvailable">当目标流可用时调用的回调，直接写入流。</param>
    /// <param name="mediaType">Content-Type 媒体类型。</param>
    public PushStreamContent(Action<Stream> onStreamAvailable, string mediaType = "application/json")
    {
        _onStreamAvailable = onStreamAvailable ?? throw new ArgumentNullException(nameof(onStreamAvailable));
        _mediaType = mediaType;
        Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
    }

    /// <inheritdoc/>
    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        _onStreamAvailable(stream);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}
