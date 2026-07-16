// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mud.HttpUtils;

/// <summary>
/// 基于 <see cref="JsonSerializer"/> 的 <see cref="IHttpContentSerializer"/> 默认实现。
/// 同时实现 <see cref="ISynchronousContentSerializer"/>、<see cref="ISynchronousContentDeserializer"/>、
/// <see cref="IStreamingContentSerializer"/> 三个可选能力接口，支持同步 fast-path 与流式反序列化。
/// </summary>
/// <remarks>
/// <para>
/// 此实现行为等价于直接调用 <c>JsonSerializer</c>，保证向后兼容。
/// 请求体序列化使用 <see cref="StringContent"/> 包装 JSON 字符串（与原 EnhancedHttpClient 行为一致）。
/// </para>
/// <para>
/// <b>Native AOT</b>：构造时传入的 options 应包含 <c>TypeInfoResolver</c>
/// （由 <c>JsonSerializerContext</c> 提供），以确保 AOT 下序列化/反序列化路径安全。
/// </para>
/// <para>
/// <b>v3.3 Phase 1 T1.3</b>：实现 <see cref="ISynchronousContentSerializer"/> 启用 STJ 源生成 fast-path
/// （<c>SerializeToUtf8Bytes</c> / <c>Utf8JsonWriter</c>）。
/// </para>
/// </remarks>
public class SystemTextJsonContentSerializer : IHttpContentSerializer,
    ISynchronousContentSerializer, ISynchronousContentDeserializer, IStreamingContentSerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// 获取此序列化器使用的 <see cref="JsonSerializerOptions"/>。
    /// </summary>
    public JsonSerializerOptions Options => _options;

    /// <summary>
    /// 初始化 <see cref="SystemTextJsonContentSerializer"/> 实例。
    /// </summary>
    /// <param name="options">JSON 序列化选项。为 null 时使用默认选项。</param>
    public SystemTextJsonContentSerializer(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions();
    }

    /// <inheritdoc/>
    public HttpContent? ToHttpContent<T>(T item, object? options = null)
    {
        if (item is null) return null;
        var opts = ResolveOptions(options);
        var json = JsonSerializer.Serialize(item, opts);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    /// <inheritdoc/>
    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, object? options = null, CancellationToken cancellationToken = default)
    {

        var opts = ResolveOptions(options);
#if NET5_0_OR_GREATER
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, opts, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public string Serialize<T>(T item, object? options = null)
    {
        var opts = ResolveOptions(options);
        return JsonSerializer.Serialize(item, opts);
    }

    /// <inheritdoc/>
    public string Serialize(object? item, System.Type type, object? options = null)
    {
        var opts = ResolveOptions(options);
        return JsonSerializer.Serialize(item, type, opts);
    }

    /// <inheritdoc/>
    public T? Deserialize<T>(string json, object? options = null)
    {
#if NET6_0_OR_GREATER
        if (options is System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo)
        {
            return JsonSerializer.Deserialize(json, jsonTypeInfo);
        }
#endif
        var opts = ResolveOptions(options);
        return JsonSerializer.Deserialize<T>(json, opts);
    }

    /// <inheritdoc/>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("GetFieldNameForProperty 使用反射读取属性特性，AOT 场景应由源生成器在编译期提供字段名映射。")]
#endif
    public string? GetFieldNameForProperty(PropertyInfo propertyInfo)
    {
        var attr = propertyInfo.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attr?.Name ?? _options.PropertyNamingPolicy?.ConvertName(propertyInfo.Name);
    }

    private JsonSerializerOptions ResolveOptions(object? options)
    {
        return options as JsonSerializerOptions ?? _options;
    }

    // ========================================================================
    // ISynchronousContentSerializer 实现
    // ========================================================================

    /// <inheritdoc/>
    /// <remarks>
    /// 使用 <c>SerializeToUtf8Bytes</c> 同步路径返回 <see cref="ByteArrayContent"/>（启用 STJ 源生成 fast-path）。
    /// netstandard2.0 下回退到 <c>Serialize&lt;T&gt;</c> + <see cref="StringContent"/>。
    /// </remarks>
    public HttpContent ToHttpContentSynchronous<T>(T item)
    {
        if (item is null) return new ByteArrayContent(Array.Empty<byte>());

        var opts = _options;
#if NET5_0_OR_GREATER
        var bytes = JsonSerializer.SerializeToUtf8Bytes(item, opts);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return content;
#else
        var json = JsonSerializer.Serialize(item, opts);
        return new StringContent(json, Encoding.UTF8, "application/json");
#endif
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 返回自定义 <see cref="HttpContent"/>，在 <c>SerializeToStreamAsync</c> 中使用 <c>Utf8JsonWriter</c> 写入请求流。
    /// 不缓冲全部内容，适用于大 payload 上传。
    /// </remarks>
    public HttpContent ToStreamingHttpContent<T>(T item)
    {
        return new StreamingJsonContent<T>(item, _options);
    }

    // ========================================================================
    // ISynchronousContentDeserializer 实现
    // ========================================================================

    /// <inheritdoc/>
    /// <remarks>
    /// 委托现有 <see cref="Deserialize{T}(string, object?)"/> 实现（已有 <c>JsonTypeInfo&lt;T&gt;</c> fast-path）。
    /// </remarks>
    public T? DeserializeFromString<T>(string content)
    {
        return Deserialize<T>(content);
    }

    // ========================================================================
    // IStreamingContentSerializer 实现
    // ========================================================================

    /// <inheritdoc/>
    /// <remarks>
    /// <para><see cref="StreamingContentFormat.JsonLines"/>：逐行读取，每行反序列化为一个 <typeparamref name="T"/>。</para>
    /// <para><see cref="StreamingContentFormat.JsonArray"/>：使用 <c>DeserializeAsyncEnumerable&lt;T&gt;</c> 增量枚举（net6+）。</para>
    /// </remarks>
    public async IAsyncEnumerable<T?> DeserializeStreamAsync<T>(
        Stream stream,
        StreamingContentFormat format,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var opts = _options;

        switch (format)
        {
            case StreamingContentFormat.JsonLines:
                // NDJSON: 逐行读取，每行一个 JSON 值
                using (var reader = new StreamReader(stream))
                {
                    string? line;
                    while ((line = await ReadLineAsyncWithCancellation(reader, cancellationToken).ConfigureAwait(false)) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        yield return JsonSerializer.Deserialize<T>(line, opts);
                    }
                }
                yield break;

            case StreamingContentFormat.JsonArray:
                // 单个 JSON 数组，增量枚举
#if NET6_0_OR_GREATER
                await foreach (var item in JsonSerializer
                    .DeserializeAsyncEnumerable<T>(stream, opts, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return item;
                }
#else
                // netstandard2.0 回退：整体反序列化后枚举
                var list = await JsonSerializer.DeserializeAsync<List<T>>(stream, opts, cancellationToken).ConfigureAwait(false);
                if (list != null)
                {
                    foreach (var item in list) yield return item;
                }
#endif
                yield break;

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported streaming content format.");
        }
    }

    /// <summary>
    /// 读取一行并支持取消令牌（.NET 7+ 原生支持，旧版本回退到无取消版本）。
    /// </summary>
    private static async Task<string?> ReadLineAsyncWithCancellation(StreamReader reader, CancellationToken cancellationToken)
    {
#if NET7_0_OR_GREATER
        return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
#else
        cancellationToken.ThrowIfCancellationRequested();
        return await reader.ReadLineAsync().ConfigureAwait(false);
#endif
    }

    // ========================================================================
    // 内部类型：流式 JSON HttpContent
    // ========================================================================

    /// <summary>
    /// 流式 JSON <see cref="HttpContent"/>：在发送时使用 <c>Utf8JsonWriter</c> 同步写入请求流，不缓冲全部内容。
    /// </summary>
    private sealed class StreamingJsonContent<T> : HttpContent
    {
        private readonly T _item;
        private readonly JsonSerializerOptions _options;

        public StreamingJsonContent(T item, JsonSerializerOptions options)
        {
            _item = item;
            _options = options;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsyncCore(stream, CancellationToken.None);

#if NET5_0_OR_GREATER
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => SerializeToStreamAsyncCore(stream, cancellationToken);
#endif

        private async Task SerializeToStreamAsyncCore(Stream stream, CancellationToken cancellationToken)
        {
            await using var writer = new Utf8JsonWriter(stream);
            JsonSerializer.Serialize(writer, _item, _options);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
