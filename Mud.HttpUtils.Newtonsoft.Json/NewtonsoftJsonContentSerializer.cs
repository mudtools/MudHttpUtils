// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using System.Reflection;

namespace Mud.HttpUtils.Newtonsoft.Json;

/// <summary>
/// 基于 <see cref="Newtonsoft.Json.JsonSerializer"/> 的 <see cref="IHttpContentSerializer"/> 实现。
/// 同时实现 <see cref="ISynchronousContentSerializer"/>（Newtonsoft.Json 本身是同步序列化）。
/// </summary>
/// <remarks>
/// <para>
/// <b>Native AOT 注意</b>：Newtonsoft.Json 使用反射，在 AOT 下不安全。
/// AOT 场景应使用 <c>SystemTextJsonContentSerializer</c>（含 <c>JsonSerializerContext</c>）。
/// 本实现标注 <c>[RequiresUnreferencedCode]</c> 声明为非 AOT 路径。
/// </para>
/// </remarks>
/// <remarks>
/// 初始化 <see cref="NewtonsoftJsonContentSerializer"/> 实例。
/// </remarks>
/// <param name="settings">JSON 序列化设置。为 null 时使用默认设置。</param>
public class NewtonsoftJsonContentSerializer(JsonSerializerSettings? settings = null) : IHttpContentSerializer, ISynchronousContentSerializer
{
    private readonly JsonSerializerSettings _settings = settings ?? new JsonSerializerSettings();

    /// <summary>
    /// 获取此序列化器使用的 <see cref="JsonSerializerSettings"/>。
    /// </summary>
    public JsonSerializerSettings Settings => _settings;

    /// <inheritdoc/>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Newtonsoft.Json uses reflection. Not AOT-compatible.")]
#endif
    public HttpContent? ToHttpContent<T>(T item, object? options = null)
    {
        if (item is null) return null;
        var json = JsonConvert.SerializeObject(item, _settings);
        return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    }

    /// <inheritdoc/>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Newtonsoft.Json uses reflection. Not AOT-compatible.")]
#endif
    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, object? options = null, CancellationToken cancellationToken = default)
    {
#if NET5_0_OR_GREATER
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        using var reader = new System.IO.StreamReader(stream);
        using var jsonReader = new JsonTextReader(reader);
        var serializer = JsonSerializer.Create(_settings);
        return serializer.Deserialize<T?>(jsonReader);
    }

    /// <inheritdoc/>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Newtonsoft.Json uses reflection. Not AOT-compatible.")]
#endif
    public string Serialize<T>(T item, object? options = null)
    {
        return JsonConvert.SerializeObject(item, _settings);
    }

    /// <inheritdoc/>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Newtonsoft.Json uses reflection. Not AOT-compatible.")]
#endif
    public string Serialize(object? item, Type type, object? options = null)
    {
        return JsonConvert.SerializeObject(item, type, _settings);
    }

    /// <inheritdoc/>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Newtonsoft.Json uses reflection. Not AOT-compatible.")]
#endif
    public T? Deserialize<T>(string json, object? options = null)
    {
        return JsonConvert.DeserializeObject<T>(json, _settings);
    }

    /// <inheritdoc/>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Newtonsoft.Json uses reflection. Not AOT-compatible.")]
#endif
    public string? GetFieldNameForProperty(PropertyInfo propertyInfo)
    {
        var attr = propertyInfo.GetCustomAttribute<JsonPropertyAttribute>();
        return attr?.PropertyName;
    }

    // ========================================================================
    // ISynchronousContentSerializer 实现
    // ========================================================================

    /// <inheritdoc/>
    /// <remarks>
    /// Newtonsoft.Json 本身是同步序列化，直接调用 <c>JsonConvert.SerializeObject</c>。
    /// </remarks>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Newtonsoft.Json uses reflection. Not AOT-compatible.")]
#endif
    public HttpContent ToHttpContentSynchronous<T>(T item)
    {
        if (item is null) return new ByteArrayContent(Array.Empty<byte>());
        var json = JsonConvert.SerializeObject(item, _settings);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        return content;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 返回 <see cref="PushStreamContent"/>，在发送时通过同步 <c>JsonSerializer.Serialize</c> 写入请求流。
    /// </remarks>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Newtonsoft.Json uses reflection. Not AOT-compatible.")]
#endif
    public HttpContent ToStreamingHttpContent<T>(T item)
    {
        return new NewtonsoftStreamingContent<T>(item, _settings);
    }
}
