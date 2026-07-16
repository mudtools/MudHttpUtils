// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using System.Net;

namespace Mud.HttpUtils.Newtonsoft.Json;


/// <summary>
/// 流式 JSON <see cref="HttpContent"/>：在发送时通过 Newtonsoft.Json 同步写入请求流。
/// </summary>
internal sealed class NewtonsoftStreamingContent<T> : HttpContent
{
    private readonly T _item;
    private readonly JsonSerializerSettings _settings;

    public NewtonsoftStreamingContent(T item, JsonSerializerSettings settings)
    {
        _item = item;
        _settings = settings;
        Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
    }

#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Newtonsoft.Json uses reflection. Not AOT-compatible.")]
#endif
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        using var writer = new System.IO.StreamWriter(stream);
        using var jsonWriter = new JsonTextWriter(writer);
        var serializer = JsonSerializer.Create(_settings);
        serializer.Serialize(jsonWriter, _item);
        await jsonWriter.FlushAsync();
        return;
    }


    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }
}