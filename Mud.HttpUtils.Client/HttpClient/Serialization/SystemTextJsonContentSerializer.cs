// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

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
#if NET8_0_OR_GREATER
    , IAotJsonContentSerializer
#endif
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// 获取此序列化器使用的 <see cref="JsonSerializerOptions"/>。
    /// </summary>
    public JsonSerializerOptions Options => _options;

    /// <summary>
    /// 初始化 <see cref="SystemTextJsonContentSerializer"/> 实例。
    /// </summary>
    /// <param name="options">JSON 序列化选项。为 null 时使用 <see cref="HttpContentSerializerFactory.BuildOptions"/> 合并库内置上下文后的默认选项。</param>
    /// <exception cref="InvalidOperationException">
    /// 在 Native AOT 运行时而 <paramref name="options"/> 未携带 <c>TypeInfoResolver</c> 时抛出，
    /// 以避免运行期静默的反射失败。
    /// </exception>
    public SystemTextJsonContentSerializer(JsonSerializerOptions? options = null)
    {
        var resolved = options ?? HttpContentSerializerFactory.BuildOptions(null);

#if NET8_0_OR_GREATER
        // AOT 守卫：无源生成 resolver 时快速失败，避免静默反射失败 / 运行时 NotSupportedException。
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
            && resolved.TypeInfoResolver is null)
        {
            throw new InvalidOperationException(
                "Native AOT 下 JsonSerializerOptions.TypeInfoResolver 不能为空。" +
                "请使用 HttpContentSerializerFactory.CreateDefault()、" +
                "AddMudHttpContentSerializer(context) 或 AddMudHttpClientJsonContext(context) 注入源生成上下文。");
        }
#endif

        _options = resolved;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 序列化经 <see cref="JsonSerializerOptions.TypeInfoResolver"/> 解析 <typeparamref name="T"/> 的元数据。
    /// <b>Native AOT 契约</b>：options 必须携带源生成 <c>JsonSerializerContext</c> 且覆盖
    /// <typeparamref name="T"/>；否则运行时会抛出受控异常（见构造函数守卫与
    /// <see cref="IAotJsonContentSerializer"/>）。
    /// <para>
    /// [T4 修复] options 槽位接受 <c>JsonSerializerOptions</c> 或 <c>JsonTypeInfo&lt;T&gt;</c>，
    /// 与 <see cref="Deserialize{T}(string, object?)"/> 全方法对称。
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "契约要求：AOT 下 options 的 TypeInfoResolver 恒为源生成上下文（构造函数守卫强制），序列化不需要反射元数据。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：AOT 下 resolver 恒为源生成；无 resolver 时构造函数已抛 InvalidOperationException，不会走到动态代码。")]
    public HttpContent? ToHttpContent<T>(T item, object? options = null)
    {
        if (item is null) return null;
#if NET6_0_OR_GREATER
        // [T4 修复] options 槽位对称支持 JsonTypeInfo<T>，与 Deserialize 一致
        if (options is System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> ti)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(item, ti);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return content;
        }
#endif
        var opts = ResolveOptions(options);
#if NET5_0_OR_GREATER
        // [P1-4] 纵深防御：AOT 下默认走 SerializeToUtf8Bytes → ByteArrayContent，
        // 避免 string→UTF8 双次编码。JIT 保持 StringContent 既有语义。
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(item, opts);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return content;
        }
#endif
        var json = JsonSerializer.Serialize(item, opts);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "契约要求：AOT 下 options 的 TypeInfoResolver 恒为源生成上下文（构造函数守卫强制），反序列化不需要反射元数据。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：AOT 下 resolver 恒为源生成；无 resolver 时构造函数已抛 InvalidOperationException，不会走到动态代码。")]
    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, object? options = null, CancellationToken cancellationToken = default)
    {

        var opts = ResolveOptions(options);
#if NET5_0_OR_GREATER
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        {
            // 空响应体（chunked 无 Content-Length 空体）返回 default 而非抛 JsonException
            return await DeserializeWithEmptyToleranceAsync<T>(
                stream,
                s => JsonSerializer.DeserializeAsync<T>(s, opts, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "契约要求：AOT 下 options 的 TypeInfoResolver 恒为源生成上下文（构造函数守卫强制），序列化不需要反射元数据。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：AOT 下 resolver 恒为源生成；无 resolver 时构造函数已抛 InvalidOperationException，不会走到动态代码。")]
    public string Serialize<T>(T item, object? options = null)
    {
#if NET6_0_OR_GREATER
        // [T4 修复] options 槽位对称支持 JsonTypeInfo<T>，与 Deserialize 一致
        if (options is System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> ti)
        {
            return JsonSerializer.Serialize(item, ti);
        }
#endif
        var opts = ResolveOptions(options);
        return JsonSerializer.Serialize(item, opts);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>非 AOT 路径</b>：使用运行时 <see cref="System.Type"/> 分派，AOT 下需要动态元数据生成。
    /// AOT 场景请改用 <see cref="IAotJsonContentSerializer"/> 的 <c>JsonTypeInfo&lt;T&gt;</c> 重载，
    /// 或泛型重载 <see cref="Serialize{T}(T, object?)"/>。
    /// </remarks>
#if NET6_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Serialize(object, Type, options) 使用运行时类型分派，Native AOT 不支持。请改用 IAotJsonContentSerializer.Serialize<T>(item, JsonTypeInfo<T>)。")]
#endif
#if NET7_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Serialize(object, Type, options) 使用运行时类型分派，Native AOT 不支持。请改用 IAotJsonContentSerializer.Serialize<T>(item, JsonTypeInfo<T>)。")]
#endif
    public string Serialize(object? item, System.Type type, object? options = null)
    {
        var opts = ResolveOptions(options);
        return JsonSerializer.Serialize(item, type, opts);
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "契约要求：AOT 下 options 的 TypeInfoResolver 恒为源生成上下文（构造函数守卫强制），反序列化不需要反射元数据。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：AOT 下 resolver 恒为源生成；无 resolver 时构造函数已抛 InvalidOperationException，不会走到动态代码。")]
    public T? Deserialize<T>(string json, object? options = null)
    {
        // 空响应体（chunked 无 Content-Length 空体，经调用方读为空串）返回 default
        // 而非抛 JsonException，与流式路径 DeserializeWithEmptyToleranceAsync 语义一致
        if (string.IsNullOrEmpty(json))
            return default;
#if NET6_0_OR_GREATER
        if (options is System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo)
        {
            return JsonSerializer.Deserialize(json, jsonTypeInfo);
        }
#endif
        var opts = ResolveOptions(options);
        return JsonSerializer.Deserialize<T>(json, opts);
    }

    // ========================================================================
    // IAotJsonContentSerializer 实现（AOT 快车道，NET8+）
    // ========================================================================

#if NET8_0_OR_GREATER
    /// <inheritdoc/>
    public HttpContent? ToHttpContent<T>(T item, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        if (item is null) return null;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(item, typeInfo);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return content;
    }

    /// <inheritdoc/>
    public async Task<T?> FromHttpContentAsync<T>(
        HttpContent content,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        // 与 object? 路径同语义 —— 空响应体返回 default
        return await DeserializeWithEmptyToleranceAsync<T>(
            stream,
            s => JsonSerializer.DeserializeAsync(s, typeInfo, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string Serialize<T>(T item, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(item, typeInfo);

    /// <inheritdoc/>
    public T? Deserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize(json, typeInfo);
#endif

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

    /// <summary>
    /// 空响应体容忍反序列化 —— 空流返回 <c>default(T)</c>，非空流照常反序列化。
    /// </summary>
    /// <remarks>
    /// 背景：chunked 响应无 Content-Length，空体不会命中调用方的 <c>Content-Length == 0</c> 预检，
    /// 此前会直接进入 <c>JsonSerializer.DeserializeAsync</c> 并抛 JsonException。
    /// 可 seek 的流（缓冲/内存流）直接判断长度，零额外读取；不可 seek 的网络流探测首字节，
    /// EOF 视为空体，否则经 <see cref="PrependedByteStream"/> 回填首字节后反序列化（不丢字节）。
    /// </remarks>
    private static async Task<T?> DeserializeWithEmptyToleranceAsync<T>(
        Stream stream,
        Func<Stream, ValueTask<T?>> deserialize,
        CancellationToken cancellationToken)
    {
        // 可 seek 的流：长度可直接判断，无额外读取开销
        if (stream.CanSeek)
        {
            return stream.Length == 0
                ? default
                : await deserialize(stream).ConfigureAwait(false);
        }

        // 不可 seek 的流（chunked 网络流）：探测首字节
        var first = await ReadFirstByteAsync(stream, cancellationToken).ConfigureAwait(false);
        if (first is null)
            return default;

        return await deserialize(new PrependedByteStream(first.Value, stream)).ConfigureAwait(false);
    }

    /// <summary>
    /// 从流中预读 1 字节；流已结束（空体）返回 null。
    /// </summary>
    private static async Task<byte?> ReadFirstByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
#if NETSTANDARD2_0
        var read = await stream.ReadAsync(buffer, 0, 1, cancellationToken).ConfigureAwait(false);
#else
        var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
#endif
        return read == 0 ? null : buffer[0];
    }

    /// <summary>
    /// 把预读的首字节拼回流头部的只读转发装饰器（用于不可 seek 流的"先探测后反序列化"）。
    /// 不拥有内部流 —— 响应流的释放由 <see cref="HttpResponseMessage"/> 负责。
    /// </summary>
    private sealed class PrependedByteStream : Stream
    {
        private readonly Stream _inner;
        private readonly byte _first;
        private bool _firstConsumed;

        public PrependedByteStream(byte first, Stream inner)
        {
            _first = first;
            _inner = inner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_firstConsumed)
            {
                _firstConsumed = true;
                if (count > 0)
                {
                    buffer[offset] = _first;
                    return 1;
                }
                return 0;
            }
            return _inner.Read(buffer, offset, count);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (!_firstConsumed)
            {
                _firstConsumed = true;
                if (count > 0)
                {
                    buffer[offset] = _first;
                    return 1;
                }
                return 0;
            }
            return await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    // ========================================================================
    // ISynchronousContentSerializer 实现
    // ========================================================================

    /// <inheritdoc/>
    /// <remarks>
    /// 使用 <c>SerializeToUtf8Bytes</c> 同步路径返回 <see cref="ByteArrayContent"/>（启用 STJ 源生成 fast-path）。
    /// netstandard2.0 下回退到 <c>Serialize&lt;T&gt;</c> + <see cref="StringContent"/>。
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "契约要求：AOT 下 options 的 TypeInfoResolver 恒为源生成上下文（构造函数守卫强制），同步序列化不需要反射元数据。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：AOT 下 resolver 恒为源生成；无 resolver 时构造函数已抛 InvalidOperationException，不会走到动态代码。")]
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
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "NDJSON/流式路径经 _options 的源生成 resolver 解析元素类型；AOT 契约由构造函数守卫保证。")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
        Justification = "同上：AOT 下 resolver 恒为源生成；无 resolver 时构造函数已抛 InvalidOperationException，不会走到动态代码。")]
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

        // M3-#28：两参遗留重载仅 netstandard2.0 / 旧 HttpClient 路径可达（.NET 5+ 的 HttpClient
        // 优先调用下方三参重载并传递真实 CancellationToken）。两参重载拿不到令牌，故使用
        // CancellationToken.None —— 这是有意为之，不是缺陷。
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsyncCore(stream, CancellationToken.None);

#if NET5_0_OR_GREATER
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => SerializeToStreamAsyncCore(stream, cancellationToken);
#endif

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "流式路径经 _options 的源生成 resolver 解析 T；AOT 契约由构造函数守卫保证。")]
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050",
            Justification = "同上：AOT 下 resolver 恒为源生成；无 resolver 时构造函数已抛 InvalidOperationException，不会走到动态代码。")]
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
